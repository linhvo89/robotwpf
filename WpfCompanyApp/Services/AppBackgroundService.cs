using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Sinks.File;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Xml;
using VM.Core;
using VM.PlatformSDKCS;
using VMControls.Interface;
using VMControls.WPF.Release;
using WpfCompanyApp.CalibRobot;
using WpfCompanyApp.Data;
using WpfCompanyApp.Logging;
using WpfCompanyApp.Models;
using WpfCompanyApp.Views;

namespace WpfCompanyApp.Services
{
    // === STATE CHÍNH ===
    public enum AppState
    {
        Init,
        Connect,
        Idle,    // trạng thái STOP / chờ Start
        Running, // đang chạy chu trình
        Paused,  // tạm dừng
        Homing,  // chạy về Home
        Error    // có lỗi, chờ Reset
    }

    // === STATE CON READY ===
    public enum ReadySubState
    {
        CheckStatus,
        MoveHome,
        CheckCNC0,
        CompleteHome,
        CheckCNC,
      
        Complete,
        InitBasketCycle,
        SelectNextBasket,
        MoveClearCamera,
        TriggerBasketCamera,
        WaitBasketCamera,
        ConfirmBasketEmpty,
        PickByTools,
        LiftSafeAfterPick,
        CheckHoldingProducts,
        DropPickedProducts,
        WaitFullWorkClear,
        FinishAllBaskets,
    }

    // === STATE CON MANUAL ===
    public enum ManualSubState
    {
        MoveRobot,
        CheckSensor
    }

    // === STATE CON SETTINGS ===
    public enum SettingsSubState
    {
        WaitUserEdit,
        SaveChanges
    }

    public enum PickToolSubState
    {
        Idle,
        PrepareToolList,
        SelectTool,
        LoadCalibration,
        PickProduct,
        HandlePickResult,
        ConfirmCylinderSensors,
        Complete
    }

    public enum DropToolSubState
    {
        Idle,
        MoveForwardPose,
        ReleaseAllTools,
        MoveReturnPose,
        Complete
    }

    public partial class AppBackgroundService
    {
        private readonly AppDataService _data;
        private readonly CancellationTokenSource _cts = new();
        private readonly DatabaseRobot _db = new();
        private readonly INIFile _ini;
        private readonly FileLogger _logger;
        private readonly ModbusRtuToolSensorService _toolSensorRtu;
        private Task? _loopTask;

        // STATE CHÍNH
        private AppState _state = AppState.Init;

        // STATE CON
        private ReadySubState _readyState = ReadySubState.CheckStatus;
        private ManualSubState _manualState = ManualSubState.CheckSensor;
        private bool _manualBlockedLogged;
        private DateTime _nextManualStatusUpdateUtc = DateTime.MinValue;
        private static readonly TimeSpan ManualStatusUpdateInterval = TimeSpan.FromMilliseconds(100);
        private SettingsSubState _settingsState = SettingsSubState.WaitUserEdit;

        // Robot điều khiển
        private readonly ConmandRobot _robot;

        // Config robot
        private string _ipRobot = "192.168.10.10";
        private int _portRobot = 10003;
        private int _readTimeout = 500;
        bool _manualStep1CycleActive = false;
        bool _manualStep2CycleActive = false;
        bool _manualStep3CycleActive = false;
        // Robot đã kết nối chưa
        private bool _isRobotConnected = false;
        private bool _robotConnectAttemptLogged = false;
        private bool _robotConnectFailureLogged = false;
        private bool? _lastRobotReadyStatus;
        private string _lastRobotStatusSignature = "";
        private DateTime _nextRobotStatusCheckUtc = DateTime.MinValue;
        private static readonly TimeSpan RobotStatusCheckInterval = TimeSpan.FromSeconds(1);
        private DateTime _nextHomePositionCheckUtc = DateTime.MinValue;
        private static readonly TimeSpan HomePositionCheckInterval = TimeSpan.FromSeconds(1);
        private const string DropForwardPathName = "ABGO";
        private const string DropReturnPathName = "ABGOBACK";
        private const double DropPathBlendRadius = 0.05;
        private PosMoveJ? _forwardPose1Joint;
        private PosMoveJ? _forwardPose6Joint;
        private PosMoveJ? _returnPose6Joint;

        // ✅ đã kẹp sản phẩm sau bước CompleteSP hay chưa
        private bool _productLoaded = false;

        // ✅ có yêu cầu dừng sau khi chạy hết chu trình hiện tại không
        private bool _stopAfterCycle = false;
        private bool _stopPendingPickResult = false;
        private bool _startupRecoveryDrop = false;
        private bool _pausedByDoorInterlock = false;
        private bool _lastCi4Start;
        private bool _lastCi5Stop;
        private bool _lastCi6Reset;
        private bool _robotControlCiReadFailed;

        // Cycle time is measured only while the machine is actually Running.
        // One sample represents one successfully released product.
        private readonly Stopwatch _cycleActiveTime = new Stopwatch();
        private readonly Stopwatch _machineRunTime = new Stopwatch();
        private readonly Queue<double> _recentProductCycleSeconds = new Queue<double>();
        private double _activeSecondsAtLastRelease;
        private int _completedProductCount;
        private int _displayCompletedProductCount;
        private bool _cycleTimingSuspendedByFullWork;

        // ✅ cờ lỗi chung
        private bool _hasError = false;
        private string _lastError = "";

        private readonly string _logFolder;
   
        bool IsPointInPolygonXY(IReadOnlyList<PosMoveL> poly, PosMoveL p)
        {
            int n = poly.Count;
            bool inside = false;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                bool intersect =
                    ((poly[i].Y > p.Y) != (poly[j].Y > p.Y)) &&
                    (p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) /
                    (poly[j].Y - poly[i].Y) + poly[i].X);

                if (intersect)
                    inside = !inside;
            }

            return inside;
        }

        bool IsRobotInsideWorkspace(IReadOnlyList<PosMoveL> boundary, PosMoveL robotPos)
        {
            if (boundary == null || boundary.Count != 10)
                return false;

            // 10 điểm XYZ phải tạo được một khối có thể tích.
            // Nếu tất cả điểm đồng phẳng thì không thể xác định không gian làm việc 3D.
            const double volumeEpsilon = 1e-6;
            bool hasVolume = false;
            for (int a = 0; a < boundary.Count - 3 && !hasVolume; a++)
            for (int b = a + 1; b < boundary.Count - 2 && !hasVolume; b++)
            for (int c = b + 1; c < boundary.Count - 1 && !hasVolume; c++)
            for (int d = c + 1; d < boundary.Count && !hasVolume; d++)
            {
                PosMoveL p0 = boundary[a];
                PosMoveL p1 = boundary[b];
                PosMoveL p2 = boundary[c];
                PosMoveL p3 = boundary[d];

                double ux = p1.X - p0.X;
                double uy = p1.Y - p0.Y;
                double uz = p1.Z - p0.Z;
                double vx = p2.X - p0.X;
                double vy = p2.Y - p0.Y;
                double vz = p2.Z - p0.Z;
                double wx = p3.X - p0.X;
                double wy = p3.Y - p0.Y;
                double wz = p3.Z - p0.Z;

                double nx = uy * vz - uz * vy;
                double ny = uz * vx - ux * vz;
                double nz = ux * vy - uy * vx;
                double sixTimesVolume = nx * wx + ny * wy + nz * wz;
                hasVolume = Math.Abs(sixTimesVolume) > volumeEpsilon;
            }

            if (!hasVolume)
                return false;

            // Mỗi bộ ba điểm có thể tạo một mặt đỡ của bao lồi.
            // Robot phải nằm cùng phía với toàn bộ khối đối với tất cả các mặt đỡ.
            const double distanceToleranceMm = 0.5;
            bool foundHullFace = false;

            for (int i = 0; i < boundary.Count - 2; i++)
            for (int j = i + 1; j < boundary.Count - 1; j++)
            for (int k = j + 1; k < boundary.Count; k++)
            {
                PosMoveL a = boundary[i];
                PosMoveL b = boundary[j];
                PosMoveL c = boundary[k];

                double abx = b.X - a.X;
                double aby = b.Y - a.Y;
                double abz = b.Z - a.Z;
                double acx = c.X - a.X;
                double acy = c.Y - a.Y;
                double acz = c.Z - a.Z;

                double nx = aby * acz - abz * acy;
                double ny = abz * acx - abx * acz;
                double nz = abx * acy - aby * acx;
                double normalLength = Math.Sqrt(nx * nx + ny * ny + nz * nz);

                if (normalLength <= volumeEpsilon)
                    continue;

                nx /= normalLength;
                ny /= normalLength;
                nz /= normalLength;

                bool hasPositive = false;
                bool hasNegative = false;

                foreach (PosMoveL point in boundary)
                {
                    double signedDistance =
                        nx * (point.X - a.X) +
                        ny * (point.Y - a.Y) +
                        nz * (point.Z - a.Z);

                    if (signedDistance > distanceToleranceMm)
                        hasPositive = true;
                    else if (signedDistance < -distanceToleranceMm)
                        hasNegative = true;
                }

                // Có điểm ở cả hai phía: tam giác này nằm bên trong khối,
                // không phải mặt biên của bao lồi.
                if (hasPositive && hasNegative)
                    continue;

                foundHullFace = true;
                double robotDistance =
                    nx * (robotPos.X - a.X) +
                    ny * (robotPos.Y - a.Y) +
                    nz * (robotPos.Z - a.Z);

                if (hasPositive && robotDistance < -distanceToleranceMm)
                    return false;

                if (hasNegative && robotDistance > distanceToleranceMm)
                    return false;
            }

            return foundHullFace;
        }

        private bool TryLoadWorkspaceBoundary(out List<PosMoveL> boundary, out string error)
        {
            boundary = new List<PosMoveL>(10);
            var missingPoints = new List<string>();

            // Đọc theo tên thay vì theo index để không phụ thuộc thứ tự bản ghi database.
            for (int i = 1; i <= 10; i++)
            {
                string poseName = $"WorkP{i}";
                RobotTrajectory point = _db.GetRobotTrajectoryByNamePoses(poseName);
                if (point == null)
                {
                    missingPoints.Add(poseName);
                    continue;
                }

                boundary.Add(new PosMoveL
                {
                    X = point.X,
                    Y = point.Y,
                    Z = point.Z
                });
            }

            if (missingPoints.Count > 0)
            {
                error = $"Thiếu điểm vùng làm việc: {string.Join(", ", missingPoints)}.";
                boundary.Clear();
                return false;
            }

            error = string.Empty;
            return true;
        }
        partial void ManualRobot(PosMoveL currentPos);
        public AppBackgroundService(
            AppDataService data,
            INIFile ini,
            FileLogger logger,
            ModbusRtuToolSensorService toolSensorRtu)
        {
            VmSolution.OnWorkStatusEvent += VmSolution_OnWorkStatusEvent;
            _data = data;
            _ini = ini;
            _logger = logger;
            _toolSensorRtu = toolSensorRtu;

            _robot = new ConmandRobot();

            _logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(_logFolder);
        }
        public ProcessInfoList vmProcessInfoList = new ProcessInfoList();
        VmProcedure vmProcedure;
        float[] xpixel = Array.Empty<float>();
        float[] ypixel = Array.Empty<float>();
        bool triggerRun = false;
        private string _activeTriggerCamera = "Camera1";
        private string _activeCalibTool = "Tool1";
        private bool _settingsTriggerCameraPending = false;

        private sealed class VisionProduct
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float CirclePointCount { get; set; }
            public float Radius { get; set; }
            public float Confidence { get; set; }
        }

        private string GetSelectedTriggerFlowName()
        {
            return GetTriggerFlowName(_data.SelectedTriggerCamera);
        }

        private string GetTriggerFlowName(string camera)
        {
            return string.Equals(camera, "Camera2", StringComparison.OrdinalIgnoreCase)
                ? "Flow2"
                : "Flow1";
        }

        private Affine2D? GetCameraAffine(string cameraName, string toolName)
        {
            return _data.GetCalibAffine(toolName, cameraName);
        }

        private void LoadCalibAffines()
        {
            string[] tools = { "Tool1", "Tool2", "Tool3" };
            string[] cameras = { "Camera1", "Camera2" };

            _data.CalibAffines.Clear();

            foreach (string tool in tools)
            {
                foreach (string camera in cameras)
                {
                    string calibName = _data.GetCalibName(tool, camera);
                    var points = _db.GetCalibPoints(calibName);
                    Affine2D? affine = null;

                    if (points.Count >= 3)
                    {
                        try
                        {
                            affine = Affine2D.FitFromCalibPoints(points);
                        }
                        catch (Exception ex)
                        {
                            AddMachineLog(
                                $"[START] Calibration {calibName} không hợp lệ: {ex.Message}");
                        }
                    }

                    _data.SetCalibAffine(tool, camera, affine);
                }
            }

            // Đồng bộ các field cũ vẫn còn được dùng ở một số chức năng.
            _data.AffineCamera1 = _data.GetCalibAffine(camera: "Camera1");
            _data.AffineCamera2 = _data.GetCalibAffine(camera: "Camera2");
            _data._affine1 = _data.AffineCamera1;
            _data._affine2 = _data.AffineCamera2;
            AddMachineLog("[START] Đã tải lại calibration từ database.");
        }

        private void HandleVisionTriggerResult(int count)
        {
            if (_settingsTriggerCameraPending)
            {
                _settingsTriggerCameraPending = false;
                triggerRun = false;
                HandleTriggerCamera(count);
                return;
            }

            if (_readyCameraPending)
            {
                _readyCameraPending = false;
                _readyCameraResultReady = true;
                _readyCameraResultCount = count;
                triggerRun = count > 0;
                return;
            }

            triggerRun = _state == AppState.Running && count > 0;
        }

        private void ReadVisionResult(VmProcedure procedure)
        {
            // Trigger trong Settings/Calibration vẫn dùng outX, outY như trước.
            // Chỉ chu trình READY (Auto) mới dùng chuỗi kết quả của hai hàm tìm kiếm.
            if (!_readyCameraPending || _settingsTriggerCameraPending)
            {
                xpixel = procedure.ModuResult.GetOutputFloat("outX").pFloatVal
                    ?? Array.Empty<float>();
                ypixel = procedure.ModuResult.GetOutputFloat("outY").pFloatVal
                    ?? Array.Empty<float>();
                HandleVisionTriggerResult(Math.Min(xpixel.Length, ypixel.Length));
                return;
            }

            string rawResult = procedure.ModuResult
                .GetOutputString("ketqua").astStringVal[0].strValue;

            if (!TryMergeVisionProducts(rawResult, out List<VisionProduct> products, out string error))
                throw new FormatException($"Chuỗi ketqua không hợp lệ: {error}");

            xpixel = products.Select(product => product.X).ToArray();
            ypixel = products.Select(product => product.Y).ToArray();
            AddMachineLog(
                $"[READY] Basket{_readyCurrentBasket}: đã gộp kết quả hai hàm tìm kiếm, " +
                $"còn {products.Count} sản phẩm không trùng.");
            HandleVisionTriggerResult(products.Count);
        }

        private static bool TryMergeVisionProducts(
            string rawResult,
            out List<VisionProduct> mergedProducts,
            out string error)
        {
            mergedProducts = new List<VisionProduct>();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(rawResult))
                return true;

            string normalized = rawResult.Trim().TrimStart('[').TrimEnd(']');
            string[] groups = normalized.Split('#');
            if (groups.Length == 0)
            {
                error = "không có nhóm dữ liệu";
                return false;
            }

            var parsedGroups = new List<List<VisionProduct>>();
            for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                string[] fields = groups[groupIndex]
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(field => field.Trim())
                    .ToArray();

                if (fields.Length == 0)
                {
                    parsedGroups.Add(new List<VisionProduct>());
                    continue;
                }

                if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ||
                    count < 0)
                {
                    // Một số flow Vision nối thêm dữ liệu phụ sau dấu '#'
                    // nhưng không đặt số lượng ở đầu nhóm. Khi gặp định dạng này,
                    // giữ các sản phẩm hợp lệ đã đọc trước '#' và bỏ phần còn lại.
                    if (groupIndex > 0)
                        break;

                    error = $"số lượng nhóm {groupIndex + 1} không hợp lệ";
                    return false;
                }

                int requiredFieldCount = 1 + count * 5;
                if (fields.Length < requiredFieldCount)
                {
                    error =
                        $"nhóm {groupIndex + 1} khai báo {count} sản phẩm nhưng thiếu dữ liệu";
                    return false;
                }

                var products = new List<VisionProduct>(count);
                for (int productIndex = 0; productIndex < count; productIndex++)
                {
                    int offset = 1 + productIndex * 5;
                    if (!TryParseVisionFloat(fields[offset], out float x) ||
                        !TryParseVisionFloat(fields[offset + 1], out float y) ||
                        !TryParseVisionFloat(fields[offset + 2], out float circlePointCount) ||
                        !TryParseVisionFloat(fields[offset + 3], out float radius) ||
                        !TryParseVisionFloat(fields[offset + 4], out float confidence))
                    {
                        error =
                            $"sản phẩm {productIndex + 1} của nhóm {groupIndex + 1} có giá trị không hợp lệ";
                        return false;
                    }

                    products.Add(new VisionProduct
                    {
                        X = x,
                        Y = y,
                        CirclePointCount = circlePointCount,
                        Radius = Math.Abs(radius),
                        Confidence = confidence
                    });
                }

                parsedGroups.Add(products);
            }

            // Gộp tuần tự tất cả kết quả và loại mọi tọa độ trùng, kể cả trường hợp
            // một hàm tìm kiếm tự trả cùng một sản phẩm nhiều lần.
            for (int groupIndex = 0; groupIndex < parsedGroups.Count; groupIndex++)
            {
                foreach (VisionProduct candidate in parsedGroups[groupIndex])
                {
                    int duplicateIndex = mergedProducts.FindIndex(existing =>
                    {
                        double deltaX = existing.X - candidate.X;
                        double deltaY = existing.Y - candidate.Y;
                        double centerDistance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                        return centerDistance < Math.Max(existing.Radius, candidate.Radius);
                    });

                    if (duplicateIndex < 0)
                    {
                        mergedProducts.Add(candidate);
                    }
                    else if (candidate.Confidence > mergedProducts[duplicateIndex].Confidence)
                    {
                        // Hai hàm cùng thấy một sản phẩm: dùng tọa độ của kết quả tin cậy hơn.
                        mergedProducts[duplicateIndex] = candidate;
                    }
                }
            }

            return true;
        }

        private static bool TryParseVisionFloat(string value, out float result)
        {
            return float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result);
        }

        private bool TrySortProductsForPicking(out string error)
        {
            error = string.Empty;
            float[] currentXPixel = xpixel ?? Array.Empty<float>();
            float[] currentYPixel = ypixel ?? Array.Empty<float>();
            if (currentXPixel.Length == 0 || currentYPixel.Length == 0)
            {
                error = "Kết quả camera không có đủ tọa độ X/Y.";
                return false;
            }

            string cameraName = GetBasketCameraName(_readyCurrentBasket);
            var affine = GetCameraAffine(cameraName: cameraName, toolName: "Tool1");
            if (affine == null)
            {
                error = $"Chưa load calibration cho {_data.GetCalibName("Tool1", cameraName)}.";
                return false;
            }

            int productCount = Math.Min(currentXPixel.Length, currentYPixel.Length);
            var orderedProducts = Enumerable.Range(0, productCount)
                .Select(index =>
                {
                    var (robotX, robotY) = affine.PixelToRobot(
                        currentXPixel[index],
                        currentYPixel[index]);
                    return new
                    {
                        PixelX = currentXPixel[index],
                        PixelY = currentYPixel[index],
                        RobotX = robotX,
                        RobotY = robotY,
                        // Nhóm 1 (X lớn hơn mốc) có thứ tự 0 để được gắp trước.
                        GroupOrder = robotX > moveLPickProduct.X ? 0 : 1
                    };
                })
                .OrderBy(product => product.GroupOrder)
                .ThenByDescending(product => product.RobotX)
                .ThenBy(product => product.RobotY)
                .ToList();

            xpixel = orderedProducts.Select(product => product.PixelX).ToArray();
            ypixel = orderedProducts.Select(product => product.PixelY).ToArray();

            int group1Count = orderedProducts.Count(product => product.GroupOrder == 0);
            int group2Count = orderedProducts.Count - group1Count;
            AddMachineLog(
                $"[READY] Basket{_readyCurrentBasket}: đã sắp thứ tự gắp theo tọa độ robot. " +
                $"Nhóm 1 (X > {moveLPickProduct.X.ToString(CultureInfo.InvariantCulture)}): " +
                $"{group1Count} sản phẩm; Nhóm 2: {group2Count} sản phẩm.");
            return true;
        }

        private void VmSolution_OnWorkStatusEvent(VM.PlatformSDKCS.ImvsSdkDefine.IMVS_MODULE_WORK_STAUS workStatusInfo)
        {
            if (workStatusInfo.nWorkStatus == 0)//When the process is running, the nWorkStatus is 1
            {
                try
                {
                    Task.Run(() =>
                    {
                        //display.vmRenderControl.UpdateVMResultShow();
                        switch (workStatusInfo.nProcessID)
                        {   //camera1: 10000
                            case 10000:


                                if (vmProcessInfoList.nNum == 0) return;
                                try
                                {
                                    vmProcedure = (VmProcedure)VmSolution.Instance[vmProcessInfoList.astProcessInfo[0].strProcessName];
                                    if (vmProcedure == null) return;
                                    List<VmDynamicIODefine.IoNameInfo> ioNameInfos = vmProcedure.ModuResult.GetAllOutputNameInfo();
                                 }
                                catch (Exception ex)
                                {
                                    StackTrace stackTrace = new StackTrace(true);
                                    StackFrame frame = stackTrace.GetFrame(0);
                                    string errRow = $" row: {frame.GetFileLineNumber()} ";
                                    AddMachineLog($"Error: {ex.Message}" + errRow);
                                    return;
                                }

                                string vmResult2 = "", vmResultdata1 = "", ketquado = "";
                                string kp1 = "", kp2 = "", kp3 = "", kp4 = "";
                                try
                                {
                                    var pro = VmSolution.Instance[GetTriggerFlowName(_activeTriggerCamera)] as VmProcedure;
                                    if (pro != null)
                                    {

                                        try
                                        {
                                            ReadVisionResult(vmProcedure);
                                        
                                        }
                                        catch (Exception ex)
                                        {
                                            AddMachineLog(
                                                $"[READY] Không đọc được kết quả {ex.Message}");
                                        }
                                       
                                        Task.Run(() =>
                                        {
                                            try
                                            {
                                               
                                                //   UpdateResult_OK_NG(kp1, kp2, kp3, kp4);
                                            }
                                            catch { }


                                        });
                                       


                                    }

                                }
                                catch (Exception ex)
                                {
                                    StackTrace stackTrace = new StackTrace(true);
                                    StackFrame frame = stackTrace.GetFrame(0);
                                    string errRow = $" row: {frame.GetFileLineNumber()} ";
                                    AddMachineLog($"Error: {ex.Message}" + errRow);
                                }
                                //string vmResult3 = "";
                                string okng = "";

                                try
                                {
                                    Task.Run(() =>
                                    {
                                      

                                        //	wLogs.WriteToFile(Messebox, NameMode, DCSInfo.pathimage, 1);
                                    });
                                    vmResult2 = "";
                                }
                                catch( Exception ex)
                                {
                                    Task.Run(() =>
                                    {
                                        StackTrace stackTrace = new StackTrace(true);
                                        StackFrame frame = stackTrace.GetFrame(0);
                                        string errRow = $" row: {frame.GetFileLineNumber()} ";
                                        AddMachineLog($"Error: {ex.Message}" + errRow);
                                    });
                                }
                                Task.Run(() =>
                                {
                                   
                                });


                                break;
                            case 10001:


                                if (vmProcessInfoList.nNum == 0) return;
                                try
                                {
                                    vmProcedure = (VmProcedure)VmSolution.Instance[vmProcessInfoList.astProcessInfo[1].strProcessName];
                                    if (vmProcedure == null) return;
                                    List<VmDynamicIODefine.IoNameInfo> ioNameInfos = vmProcedure.ModuResult.GetAllOutputNameInfo();
                                }
                                catch (Exception ex)
                                {
                                    StackTrace stackTrace = new StackTrace(true);
                                    StackFrame frame = stackTrace.GetFrame(0);
                                    string errRow = $" row: {frame.GetFileLineNumber()} ";
                                    AddMachineLog($"Error: {ex.Message}" + errRow);
                                    return;
                                }

                         
                                try
                                {
                                    var pro = VmSolution.Instance[GetTriggerFlowName(_activeTriggerCamera)] as VmProcedure;
                                    if (pro != null)
                                    {

                                        try
                                        {
                                            ReadVisionResult(vmProcedure);
                                        }
                                        catch (Exception ex)
                                        {
                                            AddMachineLog(
                                                $"[READY] Không đọc được kết quả Vision Basket2: {ex.Message}");
                                        }

                                        Task.Run(() =>
                                        {
                                            try
                                            {

                                                //   UpdateResult_OK_NG(kp1, kp2, kp3, kp4);
                                            }
                                            catch { }


                                        });



                                    }

                                }
                                catch (Exception ex)
                                {
                                    StackTrace stackTrace = new StackTrace(true);
                                    StackFrame frame = stackTrace.GetFrame(0);
                                    string errRow = $" row: {frame.GetFileLineNumber()} ";
                                    AddMachineLog($"Error: {ex.Message}" + errRow);
                                }
                                //string vmResult3 = "";
                          

                                try
                                {
                                    Task.Run(() =>
                                    {


                                        //	wLogs.WriteToFile(Messebox, NameMode, DCSInfo.pathimage, 1);
                                    });
                                    vmResult2 = "";
                                }
                                catch (Exception ex)
                                {
                                    Task.Run(() =>
                                    {
                                        StackTrace stackTrace = new StackTrace(true);
                                        StackFrame frame = stackTrace.GetFrame(0);
                                        string errRow = $" row: {frame.GetFileLineNumber()} ";
                                        AddMachineLog($"Error: {ex.Message}" + errRow);
                                    });
                                }
                                Task.Run(() =>
                                {

                                });


                                break;
                            default:
                                break;
                        }
                    });
                }
                catch (VmException ex)
                {
                    Task.Run(() =>
                    {
                        StackTrace stackTrace = new StackTrace(true);
                        StackFrame frame = stackTrace.GetFrame(0);
                        string errRow = $" row: {frame.GetFileLineNumber()} ";
                        AddMachineLog($"Error: {ex.Message}" + errRow);
                    });
                    return;
                }
                catch (Exception ex)
                {
                    Task.Run(() =>
                    {
                        StackTrace stackTrace = new StackTrace(true);
                        StackFrame frame = stackTrace.GetFrame(0);
                        string errRow = $" row: {frame.GetFileLineNumber()} ";
                        AddMachineLog($"Error: {ex.Message}" + errRow);
                    });
                    return;
                }

            }


        }

        // ========= LOG =========
        private void AddMachineLog(string msg)
        {
            // THÊM DẤU CHẤM HỎI (?) ĐỂ KIỂM TRA NULL
            Application.Current?.Dispatcher.Invoke(() =>
            {
                string line = $"{DateTime.Now:HH:mm:ss} {msg}";
                _data.MachineLog.Insert(0, line);
                if (_data.MachineLog.Count > 3000)
                    _data.MachineLog.RemoveAt(_data.MachineLog.Count - 1);
            });

            if (_data.WriteLog)
                _logger.LogMachine(msg);
        }

        private void AddRobotHistory(string msg)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                string line = $"{DateTime.Now:HH:mm:ss} {msg}";
                _data.RobotHistory.Insert(0, line);
                if (_data.RobotHistory.Count > 1000)
                    _data.RobotHistory.RemoveAt(_data.RobotHistory.Count - 1);
            });

            if (_data.WriteLog)
                _logger.LogRobotHistory(msg);
        }

        // ========= HÀM LỖI CHUNG =========
        private void RaiseError(string msg)
        {
            // Nếu đã ở Error thì thôi, tránh spam log
            if (_hasError && _state == AppState.Error)
                return;

            _hasError = true;
            _lastError = msg;

            AddMachineLog("[ERROR] " + msg);

            Application.Current?.Dispatcher.Invoke(() =>
            {
                _data.HasError = true;
                _data.ErrorMessage = msg;
            });

            // Có thể gửi lệnh dừng khẩn robot ở đây nếu cần:
            // _robot.EStop();

            _cycleActiveTime.Stop();
            _state = AppState.Error;
        }

        private void StartCycleStatistics()
        {
            _cycleTimingSuspendedByFullWork = false;
            _cycleActiveTime.Restart();
            _machineRunTime.Restart();
            _recentProductCycleSeconds.Clear();
            _activeSecondsAtLastRelease = 0;
            _completedProductCount = 0;
            _displayCompletedProductCount = (int)_data.CycleCount;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                _data.InstantCycleTime = 0;
                _data.AverageCycleTime = 0;
                _data.CycleTime = 0;
                _data.CycleTimeDisplay = "00:00:00";
            });
        }

        private void UpdateProductionDisplay()
        {
            if (_data.ClearCycleRequested)
            {
                _data.ClearCycleRequested = false;

                _cycleActiveTime.Reset();
                if (_state == AppState.Running && !_cycleTimingSuspendedByFullWork)
                    _cycleActiveTime.Start();

                _machineRunTime.Reset();
                if ((_state == AppState.Running || _state == AppState.Error) &&
                    !_cycleTimingSuspendedByFullWork)
                    _machineRunTime.Start();

                _recentProductCycleSeconds.Clear();
                _activeSecondsAtLastRelease = 0;
                _completedProductCount = 0;
                _displayCompletedProductCount = 0;

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _data.InstantCycleTime = 0;
                    _data.AverageCycleTime = 0;
                    _data.Basket1Count = 0;
                    _data.Basket2Count = 0;
                    _data.CycleTime = 0;
                    _data.CycleTimeDisplay = "00:00:00";
                    _data.CycleCount = 0;
                });
            }

            double elapsedSeconds = Math.Floor(_machineRunTime.Elapsed.TotalSeconds);
            TimeSpan elapsed = TimeSpan.FromSeconds(elapsedSeconds);
            string elapsedDisplay = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                _data.CycleTime = elapsedSeconds;
                _data.CycleTimeDisplay = elapsedDisplay;
                _data.CycleCount = _data.Basket1Count + _data.Basket2Count;
            }));
        }

        private void RecordReleasedProducts(int productCount)
        {
            if (productCount <= 0)
                return;

            double activeSeconds = _cycleActiveTime.Elapsed.TotalSeconds;
            double batchSeconds = Math.Max(0, activeSeconds - _activeSecondsAtLastRelease);
            double secondsPerProduct = batchSeconds / productCount;

            for (int i = 0; i < productCount; i++)
            {
                _recentProductCycleSeconds.Enqueue(secondsPerProduct);
                if (_recentProductCycleSeconds.Count > 9)
                    _recentProductCycleSeconds.Dequeue();
            }

            _activeSecondsAtLastRelease = activeSeconds;
            _completedProductCount += productCount;
            _displayCompletedProductCount += productCount;

            double instant = _recentProductCycleSeconds.Average();
            double average = activeSeconds / _completedProductCount;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _data.InstantCycleTime = Math.Round(instant, 2);
                _data.AverageCycleTime = Math.Round(average, 2);
                if (_readyCurrentBasket == 1)
                    _data.Basket1Count += productCount;
                else if (_readyCurrentBasket == 2)
                    _data.Basket2Count += productCount;
            });
        }

        private void ClearErrorStatus()
        {
            _hasError = false;
            _lastError = "";
            index = 0;
            
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _data.HasError = false;
                _data.ErrorMessage = "";
            });
        }

        private void ReportRecoverableInterlock(string message)
        {
            // Basket/áp suất là điều kiện vận hành có thể tự phục hồi, không phải
            // lỗi robot bắt buộc Reset. Vẫn hiển thị nguyên nhân nhưng giữ máy ở Idle.
            _hasError = false;
            _lastError = message;
            AddMachineLog("[INTERLOCK] " + message);

            Application.Current?.Dispatcher.Invoke(() =>
            {
                _data.HasError = true;
                _data.ErrorMessage = message;
            });
        }

        // ========= CHECK SAFETY =========
        private void CheckSafetySignals()
        {
            // Nếu đã ở Error thì không cần check nữa
            if (_state == AppState.Error)
                return;

            // EMG STOP
            //if (_data.Di2EmgStop)
            //{
            //    RaiseError("EMG STOP được nhấn, dừng khẩn cấp robot.");
            //    return;
            //}

            // Cửa mở trong khi robot đang chạy hoặc homing
            //if ((_state == AppState.Running || _state == AppState.Homing) )
            //{
            //    RaiseError("Door OPEN khi robot đang di chuyển.");
            //    return;
            //}

            // Có thể thêm các safety khác: limit, collision, v.v.
        }

        // ========= VÒNG LẶP NỀN =========
        public void Start(int intervalMs = 1000)
        {
            if (_loopTask != null && !_loopTask.IsCompleted)
                return;

            // Truyền trực tiếp callback để mọi trạng thái PLC luôn xuất hiện
            // trong Machine Log trên Home.
            _toolSensorRtu.Start(AddMachineLog);
            _loopTask = Task.Run(async () =>
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        // Demo cập nhật text 3 tab
                        _data.HomeData = $"Home updated {DateTime.Now:HH:mm:ss} {_data.Ketqua}";
                        _data.ManualData = $"Manual updated {DateTime.Now:HH:mm:ss}";
                        _data.SettingsData = $"Settings updated {DateTime.Now:HH:mm:ss}";

                        // Đọc nút cứng từ robot ở mọi tab. Các request được đưa vào
                        // đúng state machine đang xử lý nút Start/Stop/Reset trên UI.
                        if (_isRobotConnected)
                            PollRobotControlInputs();

                        // === state chính ===
                        switch (_state)
                        {
                            case AppState.Init:
                                HandleInit();
                                break;
                            case AppState.Connect:
                                HandleConnect();
                                break;
                            case AppState.Idle:
                                HandleIdle();
                                break;
                            case AppState.Running:
                                HandleRunning();
                                break;
                            case AppState.Paused:
                                HandlePaused();
                                break;
                            case AppState.Homing:
                                HandleHoming();
                                break;
                            case AppState.Error:
                                HandleError();
                                break;
                        }
                        _data.CurrentState = _state;
                        UpdateProductionDisplay();

                        // === xử lý Manual & Settings theo tab đang mở ===
                        if (_isRobotConnected)
                        {
                            if (_data.ManualActive)
                                HandleManual();

                            if (_data.SettingsActive)
                                HandleSettings();

                            // sau khi đọc IO/manual/settings -> check safety
                            CheckSafetySignals();
                        }
                        await LoadJobAsync();
                        HandleShutdown();
                        HandleRestart();
                        await Task.Delay(intervalMs, _cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    AddMachineLog($"[LOOP][EXCEPTION] {ex}");
                    RaiseError("Exception trong vòng lặp nền: " + ex.Message);
                }
            });
        }

        public async Task StopAsync()
        {
            _cts.Cancel();
            if (_loopTask != null)
            {
                try
                {
                    await _loopTask;
                }
                catch (OperationCanceledException)
                {
                }
            }
            _isRobotConnected = false;
        }

        public void Stop()
        {
            _cts.Cancel();
            _isRobotConnected = false;
        }

        // ========= STATE HANDLER =========
        private async Task LoadJobAsync()
        {
            try
            {
                if (_data.LoadJob)
                {
                    _data.LoadJob = false;
                    VmSolutionInfo vmSolutionInfo = new VmSolutionInfo();
                    string path111 = AppDomain.CurrentDomain.BaseDirectory + "Solution\\" + _data.JobName + ".sol";
                    vmSolutionInfo.vmSolutionPath = path111;
                    if (VmSolution.Instance.SolutionPath != null)
                    {
                        // VisionMaster cần được gọi trên UI thread, nhưng thời gian chờ
                        // giữa các bước không được khóa Dispatcher.
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            VmSolution.Save();
                        });

                        await Task.Delay(1500, _cts.Token);

                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            VmSolution.Instance.CloseSolution();
                        });

                        await Task.Delay(500, _cts.Token);
                    }

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        VmSolution.Load(vmSolutionInfo.vmSolutionPath, "196370");
                    });

                    await Task.Delay(1000, _cts.Token);

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        vmProcessInfoList = VmSolution.Instance.GetAllProcedureList();
                        vmProcedure = VmSolution.Instance[
                            vmProcessInfoList.astProcessInfo[0].strProcessName] as VmProcedure;
                        _data.ModuleSource = vmProcedure;
                    });
                    AutoCloseToast.ShowSuccess("Load Solution successfulg ✔", 1000);
                }
                
           
            }
            catch (OperationCanceledException)
            {
                // Ứng dụng đang dừng; không hiển thị thông báo lỗi Load Job.
            }
            catch
            {
                AutoCloseToast.ShowError("Load Solution Error", 1000);
            }
        }
        private void HandleInit()
        {
            try
            {
                
                var ip = _ini.Read("IPAddr", "RobotTCP");
                if (!string.IsNullOrWhiteSpace(ip))
                    _ipRobot = ip;

                // IP, port và timeout của robot phải lấy cùng một section RobotTCP.
                var portStr = _ini.Read("Port", "RobotTCP");
                if (int.TryParse(portStr, out int port))
                    _portRobot = port;

                var timeoutStr = _ini.Read("TimeOut", "RobotTCP");
                if (int.TryParse(timeoutStr, out int timeout))
                    _readTimeout = timeout;

                var cameraResultTimeoutStr = _ini.Read("ResultTimeOut", "Camera");
                if (int.TryParse(cameraResultTimeoutStr, out int cameraResultTimeoutMs) &&
                    cameraResultTimeoutMs > 0)
                {
                    _readyCameraTimeout = TimeSpan.FromMilliseconds(cameraResultTimeoutMs);
                }

                _robotConnectAttemptLogged = false;
                _robotConnectFailureLogged = false;
                _data.HomeData = $"Đã load config: IP={_ipRobot}, Port={_portRobot}, TO={_readTimeout}";
                AddMachineLog(
                    $"[ROBOT TCP] Đã tải cấu hình từ [RobotTCP]: IP={_ipRobot}, " +
                    $"Port={_portRobot}, TimeOut={_readTimeout} ms.");
                AddMachineLog(
                    $"[CAMERA] Timeout chờ kết quả={_readyCameraTimeout.TotalMilliseconds:0} ms.");
                
                AddRobotHistory("[INIT] Load config OK");

                _state = AppState.Connect;
            }
            catch (Exception ex)
            {
                AddMachineLog($"[INIT][ERROR] {ex.Message}");
                RaiseError("Không đọc được file cấu hình: " + ex.Message);
            }
        }
        private bool _stopPressedBeforeStart = false;

        private void HandleConnect()
        {
            try
            {
                if (!_robotConnectAttemptLogged)
                {
                    _robotConnectAttemptLogged = true;
                    AddMachineLog(
                        $"[ROBOT TCP] Bắt đầu kết nối robot {_ipRobot}:{_portRobot}...");
                }

                bool ok = _robot.tcpConnect(_ipRobot, _portRobot, _readTimeout);
                //  bool ok = true;
                if (ok)
                {
                    _isRobotConnected = true;
                    AddMachineLog(
                        _robotConnectFailureLogged
                            ? "[ROBOT TCP] Đã kết nối lại robot thành công."
                            : "[ROBOT TCP] Kết nối robot thành công.");
                    AddRobotHistory("[ROBOT TCP] Connected OK");
                    UpdateRobotStatusHistory(force: true);
                    _robotConnectFailureLogged = false;

                    _state = AppState.Idle;
                    _readyState = ReadySubState.CheckStatus;
                }
                else
                {
                    _isRobotConnected = false;
                    if (!_robotConnectFailureLogged)
                    {
                        _robotConnectFailureLogged = true;
                        AddMachineLog(
                            "[ROBOT TCP] Không thể kết nối robot. Đang tự động thử lại...");
                    }
                    Thread.Sleep(500);
                }
            }
            catch (Exception ex)
            {
                _isRobotConnected = false;
                if (!_robotConnectFailureLogged)
                {
                    _robotConnectFailureLogged = true;
                    AddMachineLog(
                        $"[ROBOT TCP] Lỗi kết nối robot: {ex.Message}. " +
                        "Đang tự động thử lại...");
                }
                Thread.Sleep(500);
            }
        }

        private void UpdateRobotStatusHistory(bool force = false)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (!force && nowUtc < _nextRobotStatusCheckUtc)
                return;

            _nextRobotStatusCheckUtc = nowUtc + RobotStatusCheckInterval;

            string result = _robot.ReadRobotState(0, out int[] state);
            if (result != "OK" || state == null || state.Length < 11)
            {
                string readErrorSignature = $"READ_ERROR:{result}";
                if (force || _lastRobotStatusSignature != readErrorSignature)
                {
                    AddRobotHistory(
                        $"[WARNING][ROBOT STATUS] Không đọc được trạng thái robot: {result}.");
                }

                _lastRobotReadyStatus = null;
                _lastRobotStatusSignature = readErrorSignature;
                return;
            }

            // Đồng bộ trang Manual bằng trạng thái robot thực tế, không dựa vào
            // việc người dùng đã nhấn nút nào trong ứng dụng.
            bool poweredOn = state[9] == 1;
            bool servoEnabled = state[1] == 1;
            bool controllerInitialized = servoEnabled;
            if (poweredOn && !controllerInitialized)
            {
                string controllerResult =
                    _robot.ReadControllerState(out int controllerStarted);
                controllerInitialized =
                    controllerResult == "OK" && controllerStarted == 1;
            }

            Application.Current?.Dispatcher.Invoke(() =>
            {
                _data.RobotPoweredOn = poweredOn;
                _data.OpenOn = controllerInitialized;
                _data.EnableOn = servoEnabled;
                _data.DisableOn = controllerInitialized && !servoEnabled;
                if (!servoEnabled)
                    _data.FreeDriveOn = false;
            });

            var notReadyReasons = new List<string>();

            if (state[1] == 0)
                notReadyReasons.Add("servo chưa Enable");
            if (state[2] != 0)
                notReadyReasons.Add("robot đang báo lỗi");
            if (state[3] != 0)
                notReadyReasons.Add($"mã lỗi={state[3]}");
            if (state[4] != 0)
                notReadyReasons.Add($"trục lỗi={state[4]}");
            if (state[7] != 0)
                notReadyReasons.Add("Emergency Stop đang tác động");
            if (state[9] == 0)
                notReadyReasons.Add("controller chưa Electrify");
            if (state[10] == 0)
                notReadyReasons.Add("chưa kết nối control box");

            bool isReady = notReadyReasons.Count == 0;
            string statusSignature = isReady
                ? "READY"
                : string.Join("|", notReadyReasons);

            // Chỉ ghi khi trạng thái thay đổi để không làm đầy Robot History.
            if (!force &&
                _lastRobotReadyStatus == isReady &&
                _lastRobotStatusSignature == statusSignature)
            {
                return;
            }

            _lastRobotReadyStatus = isReady;
            _lastRobotStatusSignature = statusSignature;

            if (isReady)
            {
                AddRobotHistory(
                    $"[ROBOT STATUS] READY - Moving={state[0]}, Enable={state[1]}, " +
                    $"Error={state[2]}, ErrorCode={state[3]}, Emergency={state[7]}, " +
                    $"Electrify={state[9]}, ControlBox={state[10]}.");
                return;
            }

            AddRobotHistory(
                $"[WARNING][ROBOT STATUS] NOT READY - {string.Join("; ", notReadyReasons)}. " +
                $"Raw: Moving={state[0]}, Enable={state[1]}, Error={state[2]}, " +
                $"ErrorCode={state[3]}, ErrorAxis={state[4]}, Emergency={state[7]}, " +
                $"Electrify={state[9]}, ControlBox={state[10]}.");
        }

        // IDLE: chờ Start / Home => coi như trạng thái STOP
        private void HandleIdle()
        {
            UpdateRobotStatusHistory();
            UpdateRobotHomeStatus();

            if (_data.StartRequested)
            {
                _data.StartRequested = false;
                TurnOffBlowAirOutputs();
                LoadCalibAffines();

                if (!TryPrepareRobotForMotion("START", out string robotPrepareError))
                {
                    ReportRecoverableInterlock(
                        $"[START] Không thể khởi tạo robot: {robotPrepareError}");
                    return;
                }

                if (!TryValidateStartInterlocks(out string startInterlockError))
                {
                    ReportRecoverableInterlock(
                        $"[START] Chưa thể chạy máy: {startInterlockError} " +
                        "Khi tín hiệu sẵn sàng, nhấn Start lại; không cần Reset.");
                    return;
                }

                // Xóa thông báo liên động cũ ngay khi tất cả điều kiện Start đã đạt.
                ClearErrorStatus();
                _pausedByDoorInterlock = false;

                // Mỗi lần nhấn Start, xóa và tạo lại hai quỹ đạo thả/quay về
                // từ các giá trị J1..J6 mới nhất đã lưu trong database.
                if (!TryCreateDropMovePaths(out string movePathError))
                {
                    RaiseError($"[START] Không tạo được quỹ đạo robot: {movePathError}");
                    return;
                }

                AddMachineLog("[STATE] Start requested -> RUNNING");

                ResetReadyCycle();
                var holdingToolsAtStart = new List<string>();
                for (int tool = 1; tool <= 3; tool++)
                {
                    bool isHolding = IsToolHolding(tool);
                    _readyToolHolding[tool] = isHolding;
                    if (isHolding)
                        holdingToolsAtStart.Add(GetToolName(tool));
                }

                if (holdingToolsAtStart.Count > 0)
                {
                    // Robot đã ở Home và các liên động Start đều OK. Ưu tiên
                    // thả sản phẩm còn trên đầu hút, quay về rồi tiếp tục chu trình Basket.
                    _productLoaded = true;
                    _stopAfterCycle = true;
                    _startupRecoveryDrop = true;
                    ResetDropToolSubTree();
                    _readyState = ReadySubState.DropPickedProducts;
                    AddMachineLog(
                        $"[START] Phát hiện {string.Join(", ", holdingToolsAtStart)} đang giữ sản phẩm tại HomePose. " +
                        "Ưu tiên đi thả sản phẩm, sau đó tiếp tục chụp ảnh và chạy chu trình.");
                }
                else
                {
                    _readyState = ReadySubState.CheckStatus;
                    _stopAfterCycle = false;
                    _productLoaded = false;
                    _startupRecoveryDrop = false;
                }

                _state = AppState.Running;
                StartCycleStatistics();

                // ⭐ Nếu trước đó có nhấn STOP → reset lại index
                if (_stopPressedBeforeStart)
                {
                    index = 0;
                    _stopPressedBeforeStart = false;
                    AddMachineLog("[STATE] Start after Stop -> Reset index = 0");
                }
                return;
            }

            // ✅ CHỈ TRONG IDLE / STOP MỚI CHO VỀ HOME
            if (_data.HomeRequested)
            {
                _data.HomeRequested = false;

                if (!TryPrepareRobotForMotion("HOME", out string robotPrepareError))
                {
                    ReportRecoverableInterlock(
                        $"[HOME] Không thể khởi tạo robot: {robotPrepareError}");
                    return;
                }

                AddMachineLog("[STATE] Home requested from IDLE -> HOMING");
                _state = AppState.Homing;
                _readyState = ReadySubState.CheckStatus;
                return;
            }

            // Stop/Pause trong Idle thì không ý nghĩa, clear luôn
            if (_data.StopRequested)
            {
                _data.StopRequested = false;
                _stopAfterCycle = false;
                _productLoaded = false;
                _stopPressedBeforeStart = true;   // ⭐ ghi nhớ rằng đã nhấn Stop
              
                //index = 0;
            }

            if (_data.PauseRequested)
            {
                _data.PauseRequested = false;
            }

            // ✅ Cho phép Reset ở trạng thái IDLE
            if (_data.ResetRequested)
            {
                _data.ResetRequested = false;
                AddMachineLog("[STATE] Reset requested in IDLE.");
                index = 0;
                TurnOffAllOutputs();

                try
                {
                    if (!TryResetRobotError(out string resetError))
                    {
                        AddMachineLog($"[ERROR] Reset robot thất bại: {resetError}");
                        AddRobotHistory($"[ERROR][ROBOT STATUS] {resetError}");
                        return;
                    }

                    AddMachineLog("[STATE] Reset robot OK trong IDLE.");
                    AddRobotHistory("[RESET][ROBOT STATUS] Đã xóa lỗi robot thành công.");

                    // Clear cờ lỗi trên phần mềm (nếu đang còn)
                    _hasError = false;
                    _lastError = "";

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        _data.HasError = false;
                        _data.ErrorMessage = "";
                    });
                }
                finally
                {
                    _data.IsResetProcessing = false;
                }

                // Tắt đèn đỏ nếu đang bật
             

                // Có thể reset thêm các cờ khác nếu bạn muốn
                _stopAfterCycle = false;
                _productLoaded = false;

                // Ở Idle rồi thì không đổi state
            }



        }

        // RUNNING: chạy chu trình 10 điểm
        private void HandleRunning()
        {
            // Cửa mở khi đang chạy: Pause tại trạng thái hiện tại, không tự ý
            // chạy về Home. Người vận hành đóng cửa và nhấn Start để Resume.
            if (!TryValidateDoorsClosed(out string runningDoorError, logSuccess: false))
            {
                _cycleActiveTime.Stop();
                _machineRunTime.Stop();
                _pausedByDoorInterlock = true;
                _state = AppState.Paused;
                ReportRecoverableInterlock(
                    $"[PAUSE] {runningDoorError} Robot đã tạm dừng tại chỗ. " +
                    "Đóng cửa và nhấn Start để tiếp tục.");
                return;
            }

            // Giám sát liên tục các liên động PLC trong toàn bộ thời gian RUNNING,
            // không chỉ lúc nhấn Start hoặc ngay trước khi gắp. Đọc lại 3 lần để
            // tránh dừng máy bởi một mẫu Modbus nhiễu ngắn.
            if (!ConfirmPlcReadyBeforePick(out string runningInterlockError))
            {
                StopAndHomeForRunningInterlock(runningInterlockError);
                return;
            }

            // ❌ Không cho Reset khi RUNNING
            if (_data.ResetRequested)
            {
                _data.ResetRequested = false;
                _data.IsResetProcessing = false;
                AddMachineLog("[STATE] Reset bị IGNORE vì robot đang RUNNING.");
                // Không làm gì thêm
                index = 0;
            }

            // ✅ Stop trong RUNNING phụ thuộc đã kẹp sản phẩm chưa
            if (_data.StopRequested)
            {
                
                _data.StopRequested = false;
                _cycleActiveTime.Stop();
                _machineRunTime.Stop();
                index = 0;

                if (_readyState == ReadySubState.PickByTools ||
                    _readyState == ReadySubState.LiftSafeAfterPick)
                {
                    _stopPendingPickResult = true;
                    AddMachineLog(
                        "[STATE] Stop requested while picking -> chờ hoàn tất hút, nâng xi lanh và kiểm tra sản phẩm.");
                    return;
                }

                if (HasAnyHoldingTool())
                {
                    // ĐÃ kẹp sản phẩm (sau CompleteSP):
                    // -> phải chạy hết chu trình rồi về Home
                    _productLoaded = true;
                    _stopAfterCycle = true;
                    AddMachineLog("[STATE] Stop requested AFTER product clamped -> sẽ chạy hết chu trình rồi về HOME");
                    // vẫn giữ _state = Running để tiếp tục HandleReady()
                }
                else
                {
                    // CHƯA kẹp sản phẩm: dừng ngay, SAU ĐÓ VỀ HOME
                    AddMachineLog("[STATE] Stop requested BEFORE product clamped -> dừng ngay, về HOME");

                    // TODO: gửi lệnh dừng chu trình cho robot
                    // _robot.StopCycle();

                    _stopAfterCycle = false;
                    _productLoaded = false;
                    _readyState = ReadySubState.CheckStatus;

                    _state = AppState.Homing;
                    _cycleActiveTime.Stop();
                    return;
                }

            }

            if (_data.PauseRequested)
            {
                _data.PauseRequested = false;
                AddMachineLog("[STATE] Pause requested -> PAUSED");
                // TODO: gửi lệnh tạm dừng robot
                // _robot.Pause();
                _cycleActiveTime.Stop();
                _machineRunTime.Stop();
                _pausedByDoorInterlock = false;
                _state = AppState.Paused;
                return;
            }

            // ❌ KHÔNG CHO HOME KHI ĐANG RUNNING
            if (_data.HomeRequested)
            {
                _data.HomeRequested = false;
                AddMachineLog("[STATE] Home requested while RUNNING -> IGNORE (chỉ cho phép khi STOP/IDLE)");
            }

            HandleReady(); // vẫn chạy chu trình 10 điểm
        }

        private void StopAndHomeForRunningInterlock(string interlockError)
        {
            _data.StopRequested = false;
            _data.PauseRequested = false;
            _data.HomeRequested = false;
            _cycleActiveTime.Stop();
            _machineRunTime.Stop();

            AddMachineLog(
                $"[SAFETY] Điều kiện chạy không đạt: {interlockError}. " +
                "Dừng chu trình và đưa robot về HomePose.");

            // Giữ nguyên trạng thái các đầu hút trong lúc về Home để tránh làm
            // rơi sản phẩm nếu lỗi xuất hiện sau khi robot đã gắp.
            bool homeOk = MoveNamedPose("HomePose");

            triggerRun = false;
            _readyCameraPending = false;
            _readyCameraResultReady = false;
            _stopAfterCycle = false;
            _stopPendingPickResult = false;
            _readyState = ReadySubState.CheckStatus;

            string homeResult = homeOk
                ? "Robot đã về HomePose."
                : "Robot không thể về HomePose an toàn; kiểm tra cảm biến xi lanh và robot.";

            if (homeOk)
            {
                _state = AppState.Idle;
                _productLoaded = HasAnyHoldingTool();
                ReportRecoverableInterlock(
                    $"Điều kiện chạy máy không đạt: {interlockError}. Máy đã dừng. " +
                    $"{homeResult} Khi tín hiệu sẵn sàng, nhấn Start lại; không cần Reset.");
            }
            else
            {
                // Không về Home được là lỗi chuyển động/an toàn thật sự nên vẫn
                // yêu cầu người vận hành kiểm tra và Reset.
                RaiseError(
                    $"Điều kiện chạy máy không đạt: {interlockError}. " +
                    $"Máy đã dừng. {homeResult}");
            }
        }

        private void HandlePaused()
        {
            // Stop trong PAUSED cũng giống Running:
            if (_data.StopRequested)
            {
                _data.StopRequested = false;
                _cycleActiveTime.Stop();
                _machineRunTime.Stop();

                if (HasAnyHoldingTool())
                {
                    _productLoaded = true;
                    _stopAfterCycle = true;
                    AddMachineLog("[STATE] Stop while PAUSED AFTER product clamped -> sẽ chạy hết chu trình rồi về HOME khi Resume");
                }
                else
                {
                    AddMachineLog("[STATE] Stop while PAUSED BEFORE product clamped -> về HOME");

                    // TODO: gửi lệnh dừng/thoát chu trình cho robot
                    // _robot.StopCycle();

                    _stopAfterCycle = false;
                    _productLoaded = false;
                    _readyState = ReadySubState.CheckStatus;

                    _state = AppState.Homing;
                    _cycleActiveTime.Stop();
                }
                return;
            }

            if (_data.StartRequested)
            {
                _data.StartRequested = false;
                TurnOffBlowAirOutputs();

                if (_pausedByDoorInterlock &&
                    !TryValidateResumeInterlocks(out string resumeInterlockError))
                {
                    ReportRecoverableInterlock(
                        $"[PAUSE] Chưa thể tiếp tục: {resumeInterlockError} " +
                        "Đóng cửa/khôi phục tín hiệu rồi nhấn Start lại; không cần Reset.");
                    return;
                }

                ClearErrorStatus();
                _pausedByDoorInterlock = false;
                AddMachineLog("[STATE] Resume from paused -> RUNNING");

                // TODO: gửi lệnh Resume cho robot
                // _robot.Resume();

                _cycleActiveTime.Start();
                _machineRunTime.Start();
                _state = AppState.Running;
                return;
            }

            // ❌ KHÔNG CHO HOME KHI PAUSED
            if (_data.HomeRequested)
            {
                _data.HomeRequested = false;
                AddMachineLog("[STATE] Home while PAUSED -> IGNORE (chỉ cho phép khi STOP/IDLE)");
            }
        }

        private void HandleHoming()
        {
            try
            {
                AddMachineLog("[HOMING] Moving to home (demo).");

                if (!WaitForAllPickCylinderSensors(HomeCylinderConfirmTimeout, out string cylinderSensorStatus))
                {
                    RaiseError($"[HOMING] Không cho phép về Home: quá 500 ms cảm biến xi lanh chưa đủ ({cylinderSensorStatus}). Yêu cầu DI0=1, DI2=1, DI4=1.");
                    return;
                }

                AddRobotHistory("[HOMING] Xác nhận an toàn trước khi về Home: DI0=1, DI2=1, DI4=1.");

                int setHomeTcpResult = _robot.SetTCPByNameHans(0, "TCP1");
                if (setHomeTcpResult != 0)
                {
                    RaiseError($"[HOMING] Không thể chọn TCP1 trước khi về Home. Mã lỗi: {setHomeTcpResult}.");
                    return;
                }

                AddRobotHistory("[HOMING] Đã chọn TCP1 trước khi về Home.");

                // TODO: gửi lệnh MoveHome & check hoàn thành thật:
                // bool ok = _robot.MoveHome();
                bool ok = false; // demo
                {
                    var pose = _data.RobotTrajectories
               .FirstOrDefault(t => t.NamePoses == "HomePose");

                    if (pose != null)
                    {
                        moveLHome.X = pose.X;
                        moveLHome.Y = pose.Y;
                        moveLHome.Z = pose.Z;
                        moveLHome.RX = pose.Rx;
                        moveLHome.RY = pose.Ry;
                        moveLHome.RZ = pose.Rz;
                        // Vùng an toàn là khối lồi 3D tạo bởi XYZ của WorkP1..WorkP10.
                        PosMoveL movel2 = new PosMoveL();
                        string er = _robot.ReadActualPosMoveL(0, out movel2);
                        if (er == "OK")
                        {
                            if (!TryLoadWorkspaceBoundary(out List<PosMoveL> workspaceBoundary, out string workspaceError))
                            {
                                AddMachineLog($"[HOMING] {workspaceError} Không cho phép Move Home.");
                            }
                            else if (IsRobotInsideWorkspace(workspaceBoundary, movel2))
                            {
                                AddMachineLog("Robot hiện tại nằm TRONG vùng an toàn WorkP1..WorkP10 → ĐƯỢC phép Move Home");
                                // gửi lệnh Move Home
                                er = _robot.SetOverride(0, 0.03);
                                if (er == "OK")
                                {
                                 //   er = _robot.SetUCSByName(0, tablesp);
                                    if (er == "OK")
                                    {

                                        ////////////////
                                        {
                                            istep = 11;
                                            PosMoveL moveL = new PosMoveL();
                                            moveL.X = _data.RobotTrajectories[istep].X;
                                            moveL.Y = _data.RobotTrajectories[istep].Y;
                                            moveL.Z = _data.RobotTrajectories[istep].Z;
                                            moveL.RX = _data.RobotTrajectories[istep].Rx;
                                            moveL.RY = _data.RobotTrajectories[istep].Ry;
                                            moveL.RZ = _data.RobotTrajectories[istep].Rz;
                                            _robot.SetOverride(0, 0.03);
                                          //er = _robot.SetUCSByName(0, tablesp);
                                            if (er == "OK")
                                            {
                                                if (IsAlmostEqual(moveLHome, movel2, 10))
                                                {
                                                    AddMachineLog("Điểm hiên tại gần bằng điểm cũ Home!");
                                                }
                                                else
                                                {
                                                    AddMachineLog("Điểm hiện tạ lệch so  hôm Home!");
                                                    er = _robot.MoveL(0, moveL, 0);
                                                    if (er == "OK")
                                                    {
                                                        Thread.Sleep(1000);
                                                        AddRobotHistory($"[READY {istep + 1}] Move to waite Move home -> X: {_data.RobotTrajectories[istep].X}, Y: {_data.RobotTrajectories[istep].Y}, Z: {_data.RobotTrajectories[istep].Z}, RX: {_data.RobotTrajectories[istep].Rx}, RY: {_data.RobotTrajectories[istep].Ry}, RZ: {_data.RobotTrajectories[istep].Rz} ");

                                                    }
                                                    else
                                                    {
                                                        AddMachineLog($"[READY {istep + 1} ] Error to waite Move home ");
                                                        Thread.Sleep(500);
                                                    
                                                        //  _data.StopRequested = true;
                                                    }
                                                }


                                            }
                                            else
                                            {
                                                AddMachineLog($"[READY  {istep + 1}]  Error SetUCSByName ");
                                               
                                             
                                            }

                                        }
                                        ///////////////

                                        er = _robot.MoveL(0, moveLHome, 0);
                                        if (er == "OK")
                                        {
                                            AddRobotHistory($"[READY] Move to về  Home Thành Công -> X: {pose.X}, Y: {pose.Y}, Z: {pose.Z}, RX: {pose.Rx}, RY: {pose.Ry}, RZ: {pose.Rz} ");

                                            ok = true;
                                        }
                                        else
                                        {
                                            AddMachineLog($"[READY] Erorr Move to  Home {er}");
                                            _data.StopRequested = true;
                                        }

                                    }
                                    else
                                    {
                                        AddMachineLog($"[READY] Erorr SetUCSByName {er}");
                                    }

                                }
                                else
                                {
                                    AddMachineLog($"[READY] Erorr SetOverride {er}");
                                }
                            }
                            else
                            {
                                AddMachineLog("Robot hiện tại KHÔNG nằm trong vùng an toàn WorkP1..WorkP10 → KHÔNG Move Home. Hãy di chuyển robot vào vùng an toàn.");
                             
                            }
                        }
                        else
                        {
                            AddMachineLog("Error đọc vị trí robot");
                           
                        }


                    }
                    else
                    {
                        // Không tìm thấy NamePoses tương ứng
                        AddMachineLog($"[READY] Không tìm thấy  Home ");
                        
                    }
                }
                if (ok==false)
                {
                    RaiseError("Robot về Home thất bại.");
                    return;
                }

                // Sau khi về home xong => quay lại Idle (STOP)
                _state = AppState.Idle;
                _productLoaded = false;
                _stopAfterCycle = false;

                AddMachineLog("[HOMING] Completed, back to IDLE.");
            }
            catch (Exception ex)
            {
                RaiseError("Exception khi Homing: " + ex.Message);
            }
        }

        private void HandleShutdown()
        {
            if (_data.ShutdownReq)
            {
                _data.ShutdownReq = false;

                // Chỉ cho phép tắt hệ thống khi máy đã dừng hoàn toàn (STOP/IDLE).
                if (_state != AppState.Idle)
                {
                    AddMachineLog(
                        $"[SYSTEM][BLOCKED] Từ chối Shutdown vì máy đang ở trạng thái {_state}. " +
                        "Chỉ cho phép khi STOP/IDLE.");
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        VietnameseConfirmationDialog.ShowWarning(
                            "Không thể tắt hệ thống",
                            "Chỉ được phép tắt hệ thống khi máy đang ở chế độ DỪNG (STOP).");
                    });
                    return;
                }

                // Người dùng đã xác nhận bằng tiếng Việt tại HomeViewModel.
                {
                    // 3. Chạy luồng ngầm để không làm đơ giao diện 15 giây
                    Task.Run(() =>
                    {
                        try
                        {
                            AddMachineLog("[SYSTEM] Bắt đầu quy trình SHUTDOWN hệ thống...");
                            _robot.GrpPowerOff(0);
                            Thread.Sleep(500);
                            _robot.CloseMaster();
                            Thread.Sleep(500);
                            _robot.OSCmd();
                            AddMachineLog("[SYSTEM] Đã gửi lệnh tắt OS Robot. PC sẽ tắt sau 15 giây...");

                            Thread.Sleep(15000);
                            Process.Start("shutdown", "/s /t 0");
                        }
                        catch (Exception ex)
                        {
                            AddMachineLog($"[SYSTEM] Lỗi khi Shutdown: {ex.Message}");
                        }
                    });
                }
            }
        }

        private void HandleRestart()
        {
            if (_data.RestartReq)
            {
                _data.RestartReq = false;

                if (_state != AppState.Idle)
                {
                    AddMachineLog(
                        $"[SYSTEM][BLOCKED] Từ chối Restart vì máy đang ở trạng thái {_state}. " +
                        "Chỉ cho phép khi STOP/IDLE.");
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        VietnameseConfirmationDialog.ShowWarning(
                            "Không thể khởi động lại",
                            "Chỉ được phép khởi động lại hệ thống khi máy đang ở chế độ DỪNG (STOP).");
                    });
                    return;
                }

                // Người dùng đã xác nhận bằng tiếng Việt tại HomeViewModel.
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            AddMachineLog("[SYSTEM] Bắt đầu quy trình RESTART hệ thống...");
                            _robot.GrpPowerOff(0);
                            Thread.Sleep(500);
                            _robot.CloseMaster();
                            Thread.Sleep(500);
                            _robot.OSCmd();
                            AddMachineLog("[SYSTEM] Đã gửi lệnh tắt OS Robot. PC sẽ Restart sau 15 giây...");

                            Thread.Sleep(15000);
                            Process.Start("shutdown", "/r /t 0");
                        }
                        catch (Exception ex)
                        {
                            AddMachineLog($"[SYSTEM] Lỗi khi Restart: {ex.Message}");
                        }
                    });
                }
            }
        }

        // Chu trình 10 điểm
        int index = 0;
        int indexp = 0;
        int istep = 0;
        string tablesp = "Plane_table";
        int icounter = 0;
        int ikep = 0;
        PosMoveL moveLHome = new PosMoveL();
        PosMoveL moveLPickProduct = new PosMoveL();
        PosMoveL moveLPrePick = new PosMoveL();
        TriggerPosItem[] listRobot;
        bool IsAlmostEqual(PosMoveL p1, PosMoveL p2, double tolerance)
        {
            bool sameX = Math.Abs(p1.X - p2.X) <= tolerance;
            bool sameY = Math.Abs(p1.Y - p2.Y) <= tolerance;
            bool sameZ = Math.Abs(p1.Z - p2.Z) <= tolerance;

            return sameX && sameY && sameZ;
        }
        int ivan = 0;

        private readonly List<int> _readyBasketQueue = new();
        private int _readyCurrentBasket = 0;
        private bool _readyCameraPending = false;
        private bool _readyCameraResultReady = false;
        private int _readyCameraResultCount = 0;
        private DateTime _readyCameraTriggeredAtUtc = DateTime.MinValue;
        private TimeSpan _readyCameraTimeout = TimeSpan.FromSeconds(7);
        private int _readyCameraTimeoutCount = 0;
        private int _readyEmptyConfirmCount = 0;
        // Dùng cho chế độ Both: chỉ kết thúc khi hai Basket khác nhau được xác nhận
        // rỗng liên tiếp. Nếu Basket kế tiếp còn sản phẩm thì chuỗi xác nhận bị xóa.
        // Sau khi xử lý hết hai Basket lần đầu, kiểm tra luân phiên
        // Basket1 -> Basket2. Chỉ kết thúc khi đủ 3 vòng liên tiếp không có sản phẩm.
        private int _readyEmptyBasketMask = 0;
        private int _readyEmptyVerificationRounds = 0;
        private bool _readyEmptyVerificationStarted = false;
        private int _readyProductIndex = 0;
        private readonly bool[] _readyToolHolding = new bool[4];
        private readonly bool[] _readyToolSuspended = new bool[4];
        private readonly int[] _readyToolMissCount = new int[4];
        private const int MaxPickAttemptsPerToolPerImage = 3;
        private const int MinimumFailedCapturePickCycles = 5;
        private const int EmptyConfirmShotsPerBasket = 5;
        private const int RequiredEmptyBasketVerificationRounds = 3;
        private int _readyFailedCapturePickCycles = 0;
        private readonly int[] _pickAttemptsPerTool = new int[4];
        private PickToolSubState _pickToolState = PickToolSubState.Idle;
        private readonly List<int> _pickActiveTools = new();
        private int _pickToolListIndex = 0;
        private int _pickCurrentTool = 0;
        private string _pickCurrentToolName = "";
        private double _pickRobotX = 0;
        private double _pickRobotY = 0;
        private bool _pickCurrentOk = false;
        private DateTime _pickCylinderConfirmStartedAtUtc = DateTime.MinValue;
        private static readonly TimeSpan PickCylinderConfirmTimeout = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan HomeCylinderConfirmTimeout = TimeSpan.FromMilliseconds(500);
        private DropToolSubState _dropToolState = DropToolSubState.Idle;
        private int _dropForwardPoseIndex = 1;
        private int _dropReturnPoseIndex = 1;
        private int _fullWorkConsecutiveDropCount = 0;
        private bool _fullWorkLampOn = false;
        private DateTime _fullWorkNextLampToggleUtc = DateTime.MinValue;

        private void ResetReadyCycle()
        {
            _readyBasketQueue.Clear();
            _readyCurrentBasket = 0;
            _readyCameraPending = false;
            _readyCameraResultReady = false;
            _readyCameraResultCount = 0;
            _readyCameraTriggeredAtUtc = DateTime.MinValue;
            _readyCameraTimeoutCount = 0;
            _readyEmptyConfirmCount = 0;
            _readyEmptyBasketMask = 0;
            _readyEmptyVerificationRounds = 0;
            _readyEmptyVerificationStarted = false;
            _readyProductIndex = 0;
            Array.Clear(_readyToolHolding, 0, _readyToolHolding.Length);
            Array.Clear(_readyToolSuspended, 0, _readyToolSuspended.Length);
            Array.Clear(_readyToolMissCount, 0, _readyToolMissCount.Length);
            _readyFailedCapturePickCycles = 0;
            _stopPendingPickResult = false;
            _startupRecoveryDrop = false;
            _fullWorkConsecutiveDropCount = 0;
            SetFullWorkLamp(false);
            ResetPickToolSubTree();
            ResetDropToolSubTree();
        }

        private void ResetPickToolSubTree()
        {
            _pickToolState = PickToolSubState.Idle;
            _pickActiveTools.Clear();
            _pickToolListIndex = 0;
            _pickCurrentTool = 0;
            _pickCurrentToolName = "";
            _pickRobotX = 0;
            _pickRobotY = 0;
            _pickCurrentOk = false;
            _pickCylinderConfirmStartedAtUtc = DateTime.MinValue;
            Array.Clear(_pickAttemptsPerTool, 0, _pickAttemptsPerTool.Length);
        }

        private void ResetDropToolSubTree()
        {
            _dropToolState = DropToolSubState.Idle;
            _dropForwardPoseIndex = 1;
            _dropReturnPoseIndex = 1;
        }

        private bool TryCreateDropMovePaths(out string error)
        {
            var totalStopwatch = Stopwatch.StartNew();
            var forwardPoints = new List<RobotTrajectory>(6);
            var returnPoints = new List<RobotTrajectory>(6);
            _forwardPose1Joint = null;
            _forwardPose6Joint = null;
            _returnPose6Joint = null;

            for (int i = 1; i <= 6; i++)
            {
                RobotTrajectory forwardPoint = _db.GetRobotTrajectoryByNamePoses($"ForwardPose{i}");
                RobotTrajectory returnPoint = _db.GetRobotTrajectoryByNamePoses($"ReturnPose{i}");

                if (forwardPoint == null)
                {
                    error = $"không tìm thấy ForwardPose{i} trong database.";
                    return false;
                }

                if (returnPoint == null)
                {
                    error = $"không tìm thấy ReturnPose{i} trong database.";
                    return false;
                }

                forwardPoints.Add(forwardPoint);
                returnPoints.Add(returnPoint);
            }

            if (!TryCreateJointMovePath(
                    DropForwardPathName,
                    forwardPoints,
                    _data.SpeedMoveBetweenDrops,
                    out error))
            {
                return false;
            }

            // Khi bắt đầu ABGOBACK, robot đang ở ForwardPose6. Thêm chính điểm này
            // làm điểm đầu để vị trí khớp hiện tại khớp với điểm đầu của PathJ.
            var returnPathPoints = new List<RobotTrajectory>(7) { forwardPoints[5] };
            returnPathPoints.AddRange(returnPoints);

            if (!TryCreateJointMovePath(
                    DropReturnPathName,
                    returnPathPoints,
                    _data.SpeedReturnAfterDrop,
                    out error))
            {
                return false;
            }

            _forwardPose1Joint = ToJointPosition(forwardPoints[0]);
            _forwardPose6Joint = ToJointPosition(forwardPoints[5]);
            _returnPose6Joint = ToJointPosition(returnPoints[5]);

            totalStopwatch.Stop();

            AddRobotHistory(
                $"[START] Đã tạo quỹ đạo {DropForwardPathName}: ForwardPose1..6 và " +
                $"{DropReturnPathName}: ForwardPose6 -> ReturnPose1..6. " +
                $"Tốc độ PathJ: đi={_data.SpeedMoveBetweenDrops:0.00}, " +
                $"về={_data.SpeedReturnAfterDrop:0.00}. " +
                $"Tổng thời gian tạo MovePathJ: {totalStopwatch.ElapsedMilliseconds} ms.");
            error = string.Empty;
            return true;
        }

        private static PosMoveJ ToJointPosition(RobotTrajectory point)
        {
            return new PosMoveJ
            {
                J1 = point.J1,
                J2 = point.J2,
                J3 = point.J3,
                J4 = point.J4,
                J5 = point.J5,
                J6 = point.J6
            };
        }

        private bool TryCreateJointMovePath(
            string pathName,
            IReadOnlyList<RobotTrajectory> points,
            double velocity,
            out string error)
        {
            var stopwatch = Stopwatch.StartNew();
            bool created = false;

            try
            {
            if (velocity < 0.01 || velocity > 1)
            {
                error =
                    $"tốc độ PathJ {pathName} không hợp lệ: {velocity:0.##}. " +
                    "Giá trị phải nằm trong khoảng 0.01 đến 1.00.";
                return false;
            }

            // V6.3.3: InitPath tự xóa quỹ đạo cũ nếu trùng tên.
            string result = _robot.InitPathJ(0, pathName, velocity, DropPathBlendRadius);
            if (result != "OK")
            {
                error = $"InitPath {pathName} lỗi: {result}";
                return false;
            }

            for (int i = 0; i < points.Count; i++)
            {
                PosMoveJ jointPosition = ToJointPosition(points[i]);

                result = _robot.PushPathPointJ(0, pathName, jointPosition);
                if (result != "OK")
                {
                    error = $"PushPathPoints {pathName}, điểm {i + 1} lỗi: {result}";
                    return false;
                }
            }

            result = _robot.EndPushPathPoints(0, pathName);
            if (result != "OK")
            {
                error = $"EndPushPathPoints {pathName} lỗi: {result}";
                return false;
            }

            result = _robot.WaitPathJReady(0, pathName, 1000, out int pathState);
            if (result != "OK")
            {
                error = $"ReadPathState {pathName} lỗi: {result}; stateJ={pathState}";
                return false;
            }

            error = string.Empty;
            created = true;
            return true;
            }
            finally
            {
                stopwatch.Stop();
                AddRobotHistory(
                    $"[PATHJ] Tạo quỹ đạo {pathName} {(created ? "hoàn tất" : "thất bại")}: " +
                    $"{points.Count} điểm, tốc độ={velocity:0.00}, " +
                    $"thời gian={stopwatch.ElapsedMilliseconds} ms.");
            }
        }

        private bool TryRunForwardDropMovePath(out string error)
        {
            int moveStatus = _robot.CheckStatusMove(0, 100);
            if (moveStatus != 0)
            {
                error = $"robot chưa sẵn sàng chạy quỹ đạo. CheckStatusMove={moveStatus}.";
                return false;
            }

            if (_forwardPose6Joint == null)
            {
                error = "ForwardPose6 chưa được nạp khi nhấn Start.";
                return false;
            }

            string result = _robot.WaitPathJReady(
                0,
                DropForwardPathName,
                20,
                out int pathState);
            if (result != "OK")
            {
                error = $"ReadPathState {DropForwardPathName} lỗi: {result}; stateJ={pathState}";
                return false;
            }

            result = _robot.MovePathJ(0, DropForwardPathName, _forwardPose6Joint);
            if (result != "OK")
            {
                error = result == "1"
                    ? $"robot không hoàn thành vị trí cuối ForwardPose6 của {DropForwardPathName}."
                    : $"MovePath {DropForwardPathName} lỗi: {result}";
                return false;
            }

            AddRobotHistory(
                $"[READY] MovePath {DropForwardPathName} OK: ForwardPose1 -> ForwardPose6.");
            error = string.Empty;
            return true;
        }

        private bool MoveToForwardPathStart()
        {
            if (_forwardPose1Joint == null)
            {
                AddMachineLog("[READY] ForwardPose1 J1..J6 chưa được nạp khi nhấn Start.");
                return false;
            }

            string result = _robot.MoveJ(0, _forwardPose1Joint);
            if (result != "OK")
            {
                AddMachineLog($"[READY] MoveJ tới điểm đầu ForwardPose1 lỗi: {result}");
                return false;
            }

            AddRobotHistory(
                "[READY] MoveJ ForwardPose1 OK; vị trí khớp đã khớp điểm đầu ABGO.");
            return true;
        }

        private bool TryRunReturnDropMovePath(out string error)
        {
            int moveStatus = _robot.CheckStatusMove(0, 100);
            if (moveStatus != 0)
            {
                error = $"robot chưa sẵn sàng chạy quỹ đạo quay về. CheckStatusMove={moveStatus}.";
                return false;
            }

            if (_returnPose6Joint == null)
            {
                error = "ReturnPose6 chưa được nạp khi nhấn Start.";
                return false;
            }

            string result = _robot.WaitPathJReady(
                0,
                DropReturnPathName,
                20,
                out int pathState);
            if (result != "OK")
            {
                error = $"ReadPathState {DropReturnPathName} lỗi: {result}; stateJ={pathState}";
                return false;
            }

            result = _robot.MovePathJ(0, DropReturnPathName, _returnPose6Joint);
            if (result != "OK")
            {
                error = result == "1"
                    ? $"robot không hoàn thành vị trí cuối ReturnPose6 của {DropReturnPathName}."
                    : $"MovePath {DropReturnPathName} lỗi: {result}";
                return false;
            }

            AddRobotHistory(
                $"[READY] MovePath {DropReturnPathName} OK: ReturnPose1 -> ReturnPose6.");
            error = string.Empty;
            return true;
        }

        private void BuildBasketQueue()
        {
            _readyBasketQueue.Clear();
            string mode = _data.SelectedBasketMode ?? "Both";

            if (string.Equals(mode, "Basket1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "Both", StringComparison.OrdinalIgnoreCase))
            {
                _readyBasketQueue.Add(1);
            }

            if (string.Equals(mode, "Basket2", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "Both", StringComparison.OrdinalIgnoreCase))
            {
                _readyBasketQueue.Add(2);
            }
        }

        private bool IsBothBasketMode()
        {
            return string.Equals(_data.SelectedBasketMode, "Both", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsBasketSelected(int basket)
        {
            string mode = _data.SelectedBasketMode ?? "Both";
            return string.Equals(mode, "Both", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, $"Basket{basket}", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsSelectedBasketReady()
        {
            return (!IsBasketSelected(1) || _toolSensorRtu.IsBasket1Ready) &&
                   (!IsBasketSelected(2) || _toolSensorRtu.IsBasket2Ready);
        }

        private bool IsSelectedFullWorkSensorActive()
        {
            return string.Equals(
                    _data.SelectedFullWorkSensor,
                    "Máy2",
                    StringComparison.OrdinalIgnoreCase)
                ? _toolSensorRtu.IsMachine2Full
                : _toolSensorRtu.IsMachine1Full;
        }

        private string GetSelectedFullWorkSensorDescription()
        {
            return string.Equals(
                    _data.SelectedFullWorkSensor,
                    "Máy2",
                    StringComparison.OrdinalIgnoreCase)
                ? "Máy2 (X1/20481)"
                : "Máy1 (X0/20480)";
        }

        private void SetFullWorkLamp(bool on)
        {
            if (_fullWorkLampOn == on)
                return;

            string result = _robot.SetBoxCO(2, on ? 1 : 0);
            if (result != "OK")
            {
                AddMachineLog(
                    $"[FULL WORK][OUTPUT] Không thể {(on ? "bật" : "tắt")} đèn xanh CO2: {result}");
                return;
            }

            _fullWorkLampOn = on;
        }

        private void SuspendCycleTimingForFullWork()
        {
            if (_cycleTimingSuspendedByFullWork)
                return;

            _cycleTimingSuspendedByFullWork = true;
            _cycleActiveTime.Stop();
            _machineRunTime.Stop();
            AddMachineLog(
                "[FULL WORK] Tạm dừng tính cycle time trong thời gian chờ lấy sản phẩm.");
        }

        private void ResumeCycleTimingAfterFullWork()
        {
            if (!_cycleTimingSuspendedByFullWork)
                return;

            _cycleTimingSuspendedByFullWork = false;
            if (_state == AppState.Running)
            {
                _cycleActiveTime.Start();
                _machineRunTime.Start();
            }

            AddMachineLog(
                "[FULL WORK] Tiếp tục tính cycle time sau khi cảm biến đầy đã về 0.");
        }

        private bool EnterFullWorkWaitIfRequired()
        {
            if (IsSelectedFullWorkSensorActive())
                _fullWorkConsecutiveDropCount++;
            else
                _fullWorkConsecutiveDropCount = 0;

            AddMachineLog(
                $"[FULL WORK] Sau lần thả: {GetSelectedFullWorkSensorDescription()}=" +
                $"{(IsSelectedFullWorkSensorActive() ? 1 : 0)}, " +
                $"liên tiếp {_fullWorkConsecutiveDropCount}/2.");

            if (_fullWorkConsecutiveDropCount < 2)
                return false;

            _readyCameraPending = false;
            _readyCameraResultReady = false;
            _readyCameraTriggeredAtUtc = DateTime.MinValue;

            if (!MoveNamedPose("HomePose"))
            {
                FailReadyCycle(
                    $"[FULL WORK] {GetSelectedFullWorkSensorDescription()} báo đầy nhưng robot không về được HomePose.");
                return true;
            }

            _fullWorkLampOn = false;
            SetFullWorkLamp(true);
            _fullWorkNextLampToggleUtc = DateTime.UtcNow.AddSeconds(1);
            AddMachineLog(
                $"[FULL WORK] {GetSelectedFullWorkSensorDescription()} ở mức 1 trong 2 lần thả liên tiếp. " +
                "Robot đã về HomePose và chờ cảm biến về 0; đèn xanh CO2 nháy mỗi 1 giây.");
            SuspendCycleTimingForFullWork();
            _readyState = ReadySubState.WaitFullWorkClear;
            return true;
        }

        private void ContinueAfterCompletedDrop(bool captureFreshImage)
        {
            if (_startupRecoveryDrop)
            {
                _readyCameraPending = false;
                _readyCameraResultReady = false;
                _readyCameraTriggeredAtUtc = DateTime.MinValue;
                _startupRecoveryDrop = false;
                _stopAfterCycle = false;
                AddMachineLog(
                    "[START] Đã thả sản phẩm tồn và robot đã quay về. " +
                    "Tiếp tục chu trình Basket: chuẩn bị chụp ảnh.");
                _readyState = ReadySubState.CheckStatus;
                return;
            }

            if (_stopAfterCycle)
            {
                _readyCameraPending = false;
                _readyCameraResultReady = false;
                _readyCameraTriggeredAtUtc = DateTime.MinValue;
                AddMachineLog(
                    "[STATE] Đã chạy hết chu trình gắp/thả sau yêu cầu Stop -> về Home và dừng.");
                _readyState = ReadySubState.FinishAllBaskets;
                return;
            }

            _readyState = captureFreshImage
                ? ReadySubState.MoveClearCamera
                : ReadySubState.WaitBasketCamera;
        }

        private void QueueOtherBasket()
        {
            int nextBasket = _readyCurrentBasket == 1 ? 2 : 1;
            _readyBasketQueue.Clear();
            _readyBasketQueue.Add(nextBasket);
        }

        private List<int> GetEnabledTools()
        {
            var tools = new List<int>();
            if (_data.RunTool1 && !_readyToolSuspended[1]) tools.Add(1);
            if (_data.RunTool2 && !_readyToolSuspended[2]) tools.Add(2);
            if (_data.RunTool3 && !_readyToolSuspended[3]) tools.Add(3);
            return tools;
        }

        private string GetBasketCameraName(int basket)
        {
            return basket == 2 ? "Camera2" : "Camera1";
        }

        private string GetToolName(int tool)
        {
            return $"Tool{tool}";
        }

        private bool MoveNamedPose(string poseName)
        {
            if (string.Equals(poseName, "HomePose", StringComparison.OrdinalIgnoreCase))
            {
                if (!WaitForAllPickCylinderSensors(HomeCylinderConfirmTimeout, out string cylinderSensorStatus))
                {
                    AddMachineLog($"[READY] Không cho phép Move Home: quá 500 ms cảm biến xi lanh chưa đủ ({cylinderSensorStatus}). Yêu cầu DI0=1, DI2=1, DI4=1.");
                    return false;
                }

                AddRobotHistory("[READY] Xác nhận an toàn trước khi Move Home: DI0=1, DI2=1, DI4=1.");

                int setHomeTcpResult = _robot.SetTCPByNameHans(0, "TCP1");
                if (setHomeTcpResult != 0)
                {
                    AddMachineLog($"[READY] Không thể chọn TCP1 trước khi Move Home. Mã lỗi: {setHomeTcpResult}.");
                    return false;
                }

                AddRobotHistory("[READY] Đã chọn TCP1 trước khi Move Home.");

                const double homeSpeed = 0.05;
                string setHomeSpeedResult = _robot.SetOverride(0, homeSpeed);
                if (setHomeSpeedResult != "OK")
                {
                    AddMachineLog(
                        $"[READY] Không cài được tốc độ mặc định {homeSpeed:0.00} trước khi Move Home. " +
                        $"Lỗi: {setHomeSpeedResult}");
                    return false;
                }

                AddRobotHistory(
                    $"[READY] Tốc độ về Home cố định: {homeSpeed:0.00}.");
            }

            RobotTrajectory traj = _db.GetRobotTrajectoryByNamePoses(poseName);
            if (traj == null)
            {
                AddMachineLog($"[READY] Không tìm thấy pose {poseName}.");
                return false;
            }

            var pos = new PosMoveL
            {
                X = traj.X,
                Y = traj.Y,
                Z = traj.Z,
                RX = traj.Rx,
                RY = traj.Ry,
                RZ = traj.Rz
            };

            string er = _robot.MoveL(0, pos, 0);
            if (er != "OK")
            {
                AddMachineLog($"[READY] Move {poseName} lỗi: {er}");
                return false;
            }

            AddRobotHistory($"[READY] Move {poseName} OK -> X:{pos.X}, Y:{pos.Y}, Z:{pos.Z}");
            return true;
        }

        private bool MoveLoadedPose(string poseName, PosMoveL pos)
        {
            string er = _robot.MoveL(0, pos, 0);
            if (er != "OK")
            {
                AddMachineLog($"[READY] Move {poseName} lỗi: {er}");
                return false;
            }

            AddRobotHistory($"[READY] Move {poseName} OK -> X:{pos.X}, Y:{pos.Y}, Z:{pos.Z}");
            return true;
        }

        private bool MovePickPoint(double x, double y, double z, double rz)
        {
            var pos = new PosMoveL
            {
                X = x,
                Y = y,
                Z = z,
                RX = moveLPickProduct.RX,
                RY = moveLPickProduct.RY,
                RZ = rz
            };

            string er = _robot.MoveL(0, pos, 0);
            if (er != "OK")
            {
                AddMachineLog($"[READY] Move gắp lỗi: {er}");
                return false;
            }

            AddRobotHistory($"[READY] Move gắp -> X:{x}, Y:{y}, Z:{z}, RZ:{rz}");
            return true;
        }

        private bool MoveSafeZ(int tool, double robotX, double robotY)
        {
            //if (!TrySetReadySpeed(
            //        _data.SpeedSuction,
            //        "nâng SafeH sau khi nhặt sản phẩm"))
            //{
            //    return false;
            //}

            double safeRz = moveLPickProduct.RZ;
            if (robotX > moveLPickProduct.X)
            {
                safeRz += _readyCurrentBasket == 1 ? 90 : -90;
            }

            double pickZ = moveLPickProduct.Z - GetPickHeightOffset(tool);
            var safePoint = new PosMoveL
            {
                X = robotX,
                Y = robotY,
                Z = pickZ + _data.SafeH,
                RX = moveLPickProduct.RX,
                RY = moveLPickProduct.RY,
                RZ = safeRz
            };

            string er = _robot.MoveL(0, safePoint, 0);
            if (er != "OK")
            {
                AddMachineLog($"[READY] Nâng H lỗi: {er}");
                return false;
            }

            AddRobotHistory(
                $"[READY] Nâng từ điểm gắp lên SafeH -> X:{safePoint.X}, Y:{safePoint.Y}, " +
                $"Z:{safePoint.Z}, RZ:{safePoint.RZ}");
            return true;
        }

        private void SetToolVacuum(int tool, bool on)
        {
           int DO = 3;
            if (tool == 1) { DO = 3; }
            else if (tool == 2) { DO = 4; }
            else if (tool == 3) { DO = 5; }
            _robot.SetSerialDO(DO, on ? 1 : 0);
        }

        private bool SetPickCylinderDownForTool(int tool)
        {
            int doBit = tool - 1;
            string er = _robot.SetSerialDO(doBit, 1);
            if (er != "OK")
            {
                AddMachineLog($"[READY] Không hạ được xi lanh {tool} cho {GetToolName(tool)} bằng DO{doBit}: {er}");
                return false;
            }

            AddRobotHistory($"[READY] Hạ xi lanh {tool} cho {GetToolName(tool)}: DO{doBit}=1.");
            return true;
        }

        private bool SetPickCylinderUpForTool(int tool)
        {
           
            int doBit = tool - 1;
            string er = _robot.SetSerialDO(doBit, 0);
            if (er != "OK")
            {
                AddMachineLog($"[READY] Không nâng được xi lanh {tool} cho {GetToolName(tool)} bằng DO{doBit}: {er}");
                return false;
            }

            AddRobotHistory($"[READY] Nâng xi lanh {tool} cho {GetToolName(tool)}: DO{doBit}=0.");
            return true;
        }

        private bool IsToolHolding(int tool)
        {
            return _toolSensorRtu.IsToolHolding(tool);
        }

        private bool WaitForToolHolding(int tool, int timeoutMs = 500)
        {
            if (_data.SetSensor)
            {
                Thread.Sleep(timeoutMs);
                AddMachineLog(
                    $"[READY] SetSensor đang bật: bỏ qua cảm biến hút {GetToolName(tool)}, " +
                    $"delay {timeoutMs} ms và coi như hút thành công.");
                return true;
            }

            var stopwatch = Stopwatch.StartNew();

            do
            {
                if (IsToolHolding(tool))
                    return true;

                if (stopwatch.ElapsedMilliseconds < timeoutMs)
                    Thread.Sleep(10);
            }
            while (stopwatch.ElapsedMilliseconds < timeoutMs);

            // Kiểm tra lần cuối tại đúng thời điểm hết timeout.
            return IsToolHolding(tool);
        }

        private bool TryPickWithTool(int tool, double robotX, double robotY)
        {
            double heightOffset = GetPickHeightOffset(tool);
            double pickZ = moveLPickProduct.Z - heightOffset;
            double pickRz = moveLPickProduct.RZ;

            if (robotX > moveLPickProduct.X)
            {
                pickRz += _readyCurrentBasket == 1 ? 90 : -90;
            }

            if (!MovePickPoint(robotX, robotY, pickZ, pickRz))
            {
                FailReadyCycle($"[READY] Robot không di chuyển được tới điểm gắp cho {GetToolName(tool)}. Dừng máy, cần Reset lỗi.");
                return false;
            }
            // Bật đầu hút và kiểm tra liên tục trong tối đa 500 ms.
            // Nếu cảm biến lên sớm thì tiếp tục ngay, không chờ hết timeout.
            SetToolVacuum(tool, true);
            if (!SetPickCylinderDownForTool(tool))
            {
                FailReadyCycle($"[READY] Robot không điều khiển được xi lanh gắp cho {GetToolName(tool)}. Dừng máy, cần Reset lỗi.");
                return false;
            }
            if (WaitForToolHolding(tool))
                return true;

            AddMachineLog($"[READY] {GetToolName(tool)} hút lần 1 trượt, hạ RetryZ={_data.RetryZ}.");
            if (!MovePickPoint(robotX, robotY, pickZ - _data.RetryZ, pickRz))
            {
                FailReadyCycle($"[READY] Robot không hạ được RetryZ cho {GetToolName(tool)}. Dừng máy, cần Reset lỗi.");
                return false;
            }

            SetToolVacuum(tool, true);
            if (WaitForToolHolding(tool))
            {
                if (!MoveSafeZ(_pickCurrentTool, _pickRobotX, _pickRobotY))
                {
                    FailReadyCycle("[READY] Robot không nâng được lên độ cao an toàn H sau khi hút trượt. Dừng máy, cần Reset lỗi.");
                    _pickToolState = PickToolSubState.Complete;
                    return false;
                }
                return true;
            }
               

            SetToolVacuum(tool, false);
            if (!MoveSafeZ(_pickCurrentTool, _pickRobotX, _pickRobotY))
            {
                FailReadyCycle("[READY] Robot không nâng được lên độ cao an toàn H sau khi hút trượt. Dừng máy, cần Reset lỗi.");
                _pickToolState = PickToolSubState.Complete;
                return false;
            }
            AddMachineLog($"[READY] {GetToolName(tool)} hút lần 2 trượt, đã tắt đầu hút.");
            return false;
        }

        private bool TryReadPickCylinderSensors(out int di0, out int di2, out int di4)
        {
            di0 = 0;
            di2 = 0;
            di4 = 0;

            string result = _robot.ReadBoxDI_01234567(out int[] di);
            if (result != "OK" || di == null || di.Length < 5)
            {
                return false;
            }

            di0 = di[0]; // Xi lanh 1 / Tool1
            di2 = di[2]; // Xi lanh 2 / Tool2
            di4 = di[4]; // Xi lanh 3 / Tool3
            return true;
        }

        private bool WaitForAllPickCylinderSensors(TimeSpan timeout, out string sensorStatus)
        {
            var stopwatch = Stopwatch.StartNew();
            bool readOk;
            int di0;
            int di2;
            int di4;

            do
            {
                readOk = TryReadPickCylinderSensors(out di0, out di2, out di4);
                if (readOk && di0 == 1 && di2 == 1 && di4 == 1)
                {
                    sensorStatus = "DI0=1, DI2=1, DI4=1";
                    return true;
                }

                if (stopwatch.Elapsed < timeout)
                    Thread.Sleep(50);
            }
            while (stopwatch.Elapsed < timeout);

            sensorStatus = readOk
                ? $"DI0={di0}, DI2={di2}, DI4={di4}"
                : "không đọc được DI0/DI2/DI4";
            return false;
        }

        private bool TryPrepareRobotForMotion(string context, out string error)
        {
            string readResult = _robot.ReadRobotState(0, out int[] state);
            if (readResult != "OK" || state == null || state.Length < 11)
            {
                error = $"không đọc được trạng thái robot ({readResult}).";
                return false;
            }

            if (state[7] != 0 || state[2] != 0 || state[3] != 0 || state[4] != 0)
            {
                string errorDescription = state[3] != 0
                    ? new Error_Robot().Ss_Error(state[3])
                    : "Robot đang ở trạng thái lỗi";

                error =
                    $"Robot ERROR - ErrorCode={state[3]} ({errorDescription}), " +
                    $"ErrorAxis={state[4]}, Emergency={state[7]}. " +
                    "Không gửi lệnh khởi tạo. Hãy nhả Emergency Stop nếu đang tác động, sau đó nhấn nút Reset.";
                AddRobotHistory($"[ERROR][ROBOT STATUS] {error}");
                return false;
            }

            if (state[10] == 0)
            {
                error = "robot chưa kết nối control box.";
                return false;
            }

            // Bước 1: cấp điện nếu robot chưa Powered on.
            if (state[9] == 0)
            {
                AddRobotHistory($"[{context}][ROBOT INIT] Bước 1/3: Electrify...");
                int electrifyResult = _robot.Electrify();

                if (electrifyResult == 0)
                    Thread.Sleep(5000);
                else
                    Thread.Sleep(500);

                DateTime electrifyDeadlineUtc = DateTime.UtcNow.AddSeconds(8);
                do
                {
                    readResult = _robot.ReadRobotState(0, out state);
                    if (readResult == "OK" && state != null && state.Length >= 11 && state[9] == 1)
                        break;

                    Thread.Sleep(250);
                }
                while (DateTime.UtcNow < electrifyDeadlineUtc);

                if (readResult != "OK" || state == null || state.Length < 11 || state[9] != 1)
                {
                    error =
                        $"Electrify chưa thành công (phản hồi {electrifyResult}); " +
                        "robot vẫn chưa chuyển sang Powered on.";
                    return false;
                }

                AddRobotHistory($"[{context}][ROBOT INIT] Bước 1/3: Electrify OK.");
            }
            else
            {
                AddRobotHistory(
                    $"[{context}][ROBOT INIT] Bước 1/3: Robot đã Electrify, bỏ qua.");
            }

            // Bước 2: V6 cung cấp trạng thái riêng cho Controller initialized.
            // Không suy luận trạng thái này từ Electrify hoặc PowerState.
            string controllerReadResult = _robot.ReadControllerState(out int controllerStarted);
            if (controllerReadResult != "OK")
            {
                error = $"không đọc được trạng thái Controller initialized ({controllerReadResult}).";
                return false;
            }

            int lastMasterResult = 0;
            if (controllerStarted == 0)
            {
                AddRobotHistory(
                    $"[{context}][ROBOT INIT] Bước 2/3: Controller chưa initialized, gửi StartMaster...");

                // StartMaster có thể trả lỗi khi controller đang chuyển trạng thái.
                // Thử lại có giới hạn và chỉ thành công khi ReadControllerState=1.
                for (int attempt = 1; attempt <= 3 && controllerStarted == 0; attempt++)
                {
                    lastMasterResult = _robot.StartMaster(0);
                    AddRobotHistory(
                        $"[{context}][ROBOT INIT] StartMaster lần {attempt}, phản hồi {lastMasterResult}; " +
                        "đang chờ Controller initialized...");

                    DateTime controllerDeadlineUtc = DateTime.UtcNow.AddSeconds(20);
                    do
                    {
                        Thread.Sleep(500);
                        controllerReadResult = _robot.ReadControllerState(out controllerStarted);
                        if (controllerReadResult == "OK" && controllerStarted == 1)
                            break;
                    }
                    while (DateTime.UtcNow < controllerDeadlineUtc);
                }

                if (controllerStarted != 1)
                {
                    error =
                        $"không Initialize được controller (StartMaster={lastMasterResult}, " +
                        $"ReadControllerState={controllerReadResult}, Started={controllerStarted}). " +
                        "Không gửi lệnh Enable.";
                    return false;
                }

                AddRobotHistory(
                    $"[{context}][ROBOT INIT] Bước 2/3: Controller initialized OK.");

                // ReadControllerState=1 xuất hiện trước khi toàn bộ axis group vào
                // trạng thái Disable ổn định. Cho controller thêm thời gian hoàn tất
                // startup trước khi gửi lệnh Servo.
                AddRobotHistory(
                    $"[{context}][ROBOT INIT] Chờ axis group vào trạng thái Disable ổn định (5 giây)...");
                Thread.Sleep(5000);
            }
            else
            {
                AddRobotHistory(
                    $"[{context}][ROBOT INIT] Bước 2/3: Controller đã initialized, bỏ qua StartMaster.");
            }

            // Bước 3: bộ điều khiển này dùng GrpPowerOn để bật Servo.
            // Chỉ gửi sau khi ReadControllerState xác nhận initialized.
            readResult = _robot.ReadRobotState(0, out state);
            if (readResult != "OK" || state == null || state.Length < 11)
            {
                error = $"không đọc được trạng thái robot trước khi Enable ({readResult}).";
                return false;
            }

            int enableResult = 0;
            if (state[1] == 0)
            {
                // Lệnh có thể được robot thực thi dù TCP trả -1002, nên mỗi lần
                // gửi đều xác nhận bằng PowerState và chỉ thử lại khi vẫn Disable.
                for (int enableAttempt = 1; enableAttempt <= 3 && state[1] == 0; enableAttempt++)
                {
                    AddRobotHistory(
                        $"[{context}][ROBOT INIT] Bước 3/3: Enable Servo " +
                        $"(GrpPowerOn lần {enableAttempt}/3)...");
                    enableResult = _robot.GrpPowerOn(0);

                    DateTime enableDeadlineUtc = DateTime.UtcNow.AddSeconds(6);
                    do
                    {
                        Thread.Sleep(500);
                        readResult = _robot.ReadRobotState(0, out int[] polledState);
                        if (readResult == "OK"
                            && polledState != null
                            && polledState.Length >= 11)
                        {
                            state = polledState;
                            if (state[1] == 1)
                                break;
                        }
                    }
                    while (DateTime.UtcNow < enableDeadlineUtc);

                    if (state[1] == 0 && enableAttempt < 3)
                    {
                        AddRobotHistory(
                            $"[{context}][ROBOT INIT] Servo vẫn Disable " +
                            $"(phản hồi {enableResult}), chờ 2 giây rồi thử lại...");
                        Thread.Sleep(2000);
                    }
                }
            }

            if (readResult != "OK" || state == null || state.Length < 11 || state[1] != 1)
            {
                error =
                    $"đã gửi lệnh Enable (phản hồi {enableResult}) nhưng Servo chưa chuyển " +
                    "sang trạng thái Enable.";
                return false;
            }

            AddRobotHistory($"[{context}][ROBOT INIT] Robot READY - Servo đã Enable.");
            _lastRobotReadyStatus = true;
            _lastRobotStatusSignature = "READY";
            error = string.Empty;
            return true;
        }

        private bool TryResetRobotError(out string error)
        {
            string readResult = _robot.ReadRobotState(0, out int[] state);
            if (readResult != "OK" || state == null || state.Length < 11)
            {
                error = $"Không đọc được trạng thái robot trước khi Reset ({readResult}).";
                return false;
            }

            if (state[7] == 0 && state[2] == 0 && state[3] == 0 && state[4] == 0)
            {
                error = string.Empty;
                return true;
            }

            AddRobotHistory(
                $"[RESET][ROBOT STATUS] Gửi GrpReset - ErrorCode={state[3]}, " +
                $"ErrorAxis={state[4]}, Emergency={state[7]}...");
            int resetResult = _robot.GrpReset(0);

            // GrpReset có thể thực thi thành công nhưng controller không trả
            // response kịp thời (-1002). Vì vậy kết luận theo trạng thái thực
            // tế, không kết luận thất bại chỉ dựa vào response của lệnh.
            DateTime resetDeadlineUtc = DateTime.UtcNow.AddSeconds(8);
            bool stateReadOk = false;
            do
            {
                Thread.Sleep(500);
                readResult = _robot.ReadRobotState(0, out state);
                stateReadOk =
                    readResult == "OK" &&
                    state != null &&
                    state.Length >= 11;

                if (stateReadOk &&
                    state[7] == 0 &&
                    state[2] == 0 &&
                    state[3] == 0 &&
                    state[4] == 0)
                {
                    _lastRobotReadyStatus = null;
                    _lastRobotStatusSignature = "";
                    error = string.Empty;
                    return true;
                }
            }
            while (DateTime.UtcNow < resetDeadlineUtc);

            if (!stateReadOk)
            {
                error =
                    $"Đã gửi GrpReset nhưng không đọc được trạng thái xác nhận " +
                    $"({readResult}); response lệnh={resetResult}.";
                return false;
            }

            if (state[7] != 0 || state[2] != 0 || state[3] != 0 || state[4] != 0)
            {
                error =
                    $"Robot vẫn còn lỗi sau Reset: ErrorCode={state[3]}, " +
                    $"ErrorAxis={state[4]}, Emergency={state[7]}. " +
                    $"Response lệnh={resetResult}. Nếu Emergency=1, hãy nhả nút " +
                    "Emergency Stop rồi nhấn Reset lại.";
                return false;
            }

            error = $"Không xác định được kết quả Reset; response lệnh={resetResult}.";
            return false;
        }

        private void UpdateRobotHomeStatus()
        {
            if (DateTime.UtcNow < _nextHomePositionCheckUtc)
                return;

            _nextHomePositionCheckUtc = DateTime.UtcNow.Add(HomePositionCheckInterval);

            RobotTrajectory home = _db.GetRobotTrajectoryByNamePoses("HomePose");
            if (home == null ||
                _robot.ReadActualPosMoveL(0, out PosMoveL actualPosition) != "OK")
            {
                _data.IsRobotAtHome = false;
                return;
            }

            var homePosition = new PosMoveL
            {
                X = home.X,
                Y = home.Y,
                Z = home.Z,
                RX = home.Rx,
                RY = home.Ry,
                RZ = home.Rz
            };

            const double homePositionToleranceMm = 10.0;
            _data.IsRobotAtHome =
                IsAlmostEqual(homePosition, actualPosition, homePositionToleranceMm);
        }

        private bool TryValidateStartInterlocks(out string error)
        {
            if (!TryValidateDoorsClosed(out string doorError))
            {
                error = doorError;
                return false;
            }

            if (!_toolSensorRtu.IsCommunicationHealthy)
            {
                error = "chưa có kết nối Modbus RTU với PLC.";
                return false;
            }

            var plcNotReady = new List<string>();
            if (IsBasketSelected(1) && !_toolSensorRtu.IsBasket1Ready)
                plcNotReady.Add("Basket1 chưa sẵn sàng (X2/20482 phải bằng 1)");
            if (IsBasketSelected(2) && !_toolSensorRtu.IsBasket2Ready)
                plcNotReady.Add("Basket2 chưa sẵn sàng (X3/20483 phải bằng 1)");
            if (!_toolSensorRtu.IsAirPressureReady)
                plcNotReady.Add("áp suất khí tổng chưa đủ (X4/20484 phải bằng 1)");

            if (plcNotReady.Count > 0)
            {
                error = string.Join("; ", plcNotReady) + ".";
                return false;
            }

            AddMachineLog(
                $"[START] Điều kiện PLC OK: chế độ {_data.SelectedBasketMode}, " +
                "Basket được chọn đã sẵn sàng, áp suất khí X4/20484=1.");

            int setTcpResult = _robot.SetTCPByNameHans(0, "TCP1");
            if (setTcpResult != 0)
            {
                error = $"không thể chọn TCP1. Mã lỗi: {setTcpResult}.";
                return false;
            }

            AddRobotHistory("[START] Đã chọn TCP1 trước khi kiểm tra điều kiện chạy máy.");

            RobotTrajectory home = _db.GetRobotTrajectoryByNamePoses("HomePose");
            if (home == null)
            {
                error = "không tìm thấy HomePose trong database.";
                return false;
            }

            string readPositionResult = _robot.ReadActualPosMoveL(0, out PosMoveL actualPosition);
            if (readPositionResult != "OK")
            {
                error = $"không đọc được vị trí hiện tại của robot ({readPositionResult}).";
                return false;
            }

            var homePosition = new PosMoveL
            {
                X = home.X,
                Y = home.Y,
                Z = home.Z,
                RX = home.Rx,
                RY = home.Ry,
                RZ = home.Rz
            };

            const double homePositionToleranceMm = 10.0;
            if (!IsAlmostEqual(homePosition, actualPosition, homePositionToleranceMm))
            {
                error = $"robot chưa ở HomePose. Hiện tại X={actualPosition.X:F3}, Y={actualPosition.Y:F3}, Z={actualPosition.Z:F3}; " +
                        $"Home X={homePosition.X:F3}, Y={homePosition.Y:F3}, Z={homePosition.Z:F3}; sai số cho phép {homePositionToleranceMm:F0} mm.";
                return false;
            }

            if (!WaitForAllPickCylinderSensors(HomeCylinderConfirmTimeout, out string sensorStatus))
            {
                error = $"cảm biến xi lanh chưa an toàn sau 500 ms ({sensorStatus}). Yêu cầu DI0=1, DI2=1, DI4=1.";
                return false;
            }

            AddRobotHistory("[START] Điều kiện chạy máy OK: TCP1, robot ở HomePose, DI0=1, DI2=1, DI4=1.");
            error = string.Empty;
            return true;
        }

        private bool TryValidateDoorsClosed(out string error, bool logSuccess = true)
        {
            string readResult = _robot.ReadBoxCI_01234567(out int[] ci);
            if (readResult != "OK" || ci == null || ci.Length < 4)
            {
                error =
                    $"không đọc được cảm biến cửa CI0..CI3 từ robot ({readResult}).";
                return false;
            }

            var openDoors = new List<string>();

            // Mức 1 = cửa đã đóng; mức 0 = cửa đang mở.
            if (ci[0] != 1)
                openDoors.Add("Cửa 1 (CI0=0)");

            if (ci[1] != 1)
                openDoors.Add("Cửa 2 (CI1=0)");

            if (ci[2] != 1)
                openDoors.Add("Cửa 3 (CI2=0)");

            if (ci[3] != 1)
                openDoors.Add("Cửa 4 (CI3=0)");

            if (openDoors.Count > 0)
            {
                error =
                    $"{string.Join("; ", openDoors)} đang mở. " +
                    "Vui lòng đóng tất cả cửa an toàn.";
                return false;
            }

            if (logSuccess)
            {
                AddMachineLog(
                    "[START] Cửa an toàn OK: CI0=1, CI1=1, CI2=1, CI3=1.");
            }
            error = string.Empty;
            return true;
        }

        private bool TryValidateResumeInterlocks(out string error)
        {
            if (!TryValidateDoorsClosed(out string doorError, logSuccess: false))
            {
                error = doorError;
                return false;
            }

            if (!ConfirmPlcReadyBeforePick(out string plcError))
            {
                error = plcError;
                return false;
            }

            error = string.Empty;
            return true;
        }

        private bool ConfirmPlcReadyBeforePick(out string error)
        {
            const int maxAttempts = 3;
            const int delayBetweenAttemptsMs = 100;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                bool communicationOk = _toolSensorRtu.IsCommunicationHealthy;
                bool selectedBasketReady = IsSelectedBasketReady();
                bool airPressureReady = _toolSensorRtu.IsAirPressureReady;

                if (communicationOk &&
                    selectedBasketReady &&
                    airPressureReady)
                {
                    error = string.Empty;
                    return true;
                }

                if (attempt < maxAttempts)
                    Thread.Sleep(delayBetweenAttemptsMs);
            }

            var notReady = new List<string>();
            if (!_toolSensorRtu.IsCommunicationHealthy)
                notReady.Add("mất kết nối Modbus RTU với PLC");
            else
            {
                if (IsBasketSelected(1) && !_toolSensorRtu.IsBasket1Ready)
                    notReady.Add("Basket1 chưa sẵn sàng (X2/20482=0)");
                if (IsBasketSelected(2) && !_toolSensorRtu.IsBasket2Ready)
                    notReady.Add("Basket2 chưa sẵn sàng (X3/20483=0)");
                if (!_toolSensorRtu.IsAirPressureReady)
                    notReady.Add("áp suất khí tổng chưa đủ (X4/20484=0)");
            }

            error = string.Join("; ", notReady);
            return false;
        }

        private double GetPickHeightOffset(int tool)
        {
            return tool switch
            {
                1 => _data.JobH1,
                2 => _data.JobH2,
                3 => _data.JobH3,
                _ => 0
            };
        }

        private bool TriggerCurrentBasketCamera()
        {
            string cameraName = GetBasketCameraName(_readyCurrentBasket);
            string flowName = GetTriggerFlowName(cameraName);
            var pro = VmSolution.Instance[flowName] as VmProcedure;
            if (pro == null)
            {
                AddMachineLog($"[READY] Không tìm thấy {flowName} cho Basket{_readyCurrentBasket}.");
                return false;
            }

            // Keep VmRenderControl synchronized with the flow that is about to run.
            // ModuleSource is bound to a WPF control, so update it on the UI thread.
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _data.ModuleSource = pro;
            });

            _activeTriggerCamera = cameraName;
            _readyCameraPending = true;
            _readyCameraResultReady = false;
            _readyCameraResultCount = 0;
            xpixel = Array.Empty<float>();
            ypixel = Array.Empty<float>();
            triggerRun = false;
            _readyCameraTriggeredAtUtc = DateTime.UtcNow;
            int basketNumber = _readyCurrentBasket;
            // VmProcedure.Run() là lệnh đồng bộ và có thể chạy lâu. Chỉ gửi lệnh
            // sang worker rồi trả quyền ngay cho state machine; kết quả camera vẫn
            // được nhận qua VmSolution_OnWorkStatusEvent như trước.
            _ = Task.Run(() =>
            {
                var swRun = Stopwatch.StartNew();
                try
                {
                    pro.Run();
                    swRun.Stop();
                    AddMachineLog($"[READY] pro.Run() Basket{basketNumber} ({cameraName}) hoàn tất sau {swRun.ElapsedMilliseconds} ms.");
                }
                catch (Exception ex)
                {
                    _readyCameraPending = false;
                    AddMachineLog($"[READY] Lỗi pro.Run() Basket{basketNumber} ({cameraName}): {ex.Message}");
                }
                finally
                {
                    swRun.Stop();
                }
            });

            AddMachineLog($"[READY] Đã gửi lệnh trigger Basket{basketNumber} bằng {cameraName}.");
            return true;
        }

        private bool HasAnyHoldingTool()
        {
           
            return IsToolHolding(1) || IsToolHolding(2) || IsToolHolding(3);
        }

        private void StopBecauseNoProductPicked()
        {
            AddMachineLog("Không hút được sản phẩm. Vui lòng kiểm tra nguồn khí, áp suất hút, giác hút, dây tín hiệu và cảm biến sản phẩm.");
            if (!MoveNamedPose("HomePose"))
            {
                FailReadyCycle("[READY] Robot không về được HomePose sau khi không hút được sản phẩm. Dừng máy, cần Reset lỗi.");
                return;
            }

            _data.StopRequested = false;
            _cycleActiveTime.Stop();
            _machineRunTime.Stop();
            _state = AppState.Idle;
            _productLoaded = false;
            _stopAfterCycle = false;
            _stopPendingPickResult = false;
            _readyState = ReadySubState.CheckStatus;
            AddMachineLog("[READY] Không đầu hút nào có sản phẩm, robot đã về Home và máy đã dừng.");
        }

        private void RetryCaptureAfterFailedPickCycle()
        {
            _readyFailedCapturePickCycles++;
            AddMachineLog(
                $"[READY] Lượt chụp-hút không lấy được sản phẩm " +
                $"{_readyFailedCapturePickCycles}/{MinimumFailedCapturePickCycles}.");

            if (_readyFailedCapturePickCycles >= MinimumFailedCapturePickCycles)
            {
                AddMachineLog(
                    $"[READY] Đã đủ {MinimumFailedCapturePickCycles} lượt chụp-hút liên tiếp thất bại, robot sẽ về Home.");
                StopBecauseNoProductPicked();
                return;
            }

            _productLoaded = false;
            ResetPickToolSubTree();
            _readyState = ReadySubState.MoveClearCamera;
        }

        private void FailReadyCycle(string message, bool releaseVacuum = true)
        {
            if (releaseVacuum)
            {
                SetToolVacuum(1, false);
                SetToolVacuum(2, false);
                SetToolVacuum(3, false);
            }
            else
            {
                AddMachineLog(
                    "[SAFETY] Robot chưa hoàn thành quỹ đạo tới điểm thả; " +
                    "giữ nguyên van hút để không làm rơi sản phẩm.");
            }

            triggerRun = false;
            _readyCameraPending = false;
            _readyCameraResultReady = false;

            // Khi vẫn đang giữ sản phẩm, không xóa trạng thái Tool đang hút.
            // Trạng thái này chỉ được reset sau khi người vận hành xử lý và nhấn Reset.
            if (releaseVacuum)
                ResetReadyCycle();

            _readyState = ReadySubState.CheckStatus;
            _stopPendingPickResult = false;
            RaiseError(message);
        }

        private bool HandlePickToolSubTree()
        {
            switch (_pickToolState)
            {
                // Cây con bước 1: Chuẩn bị danh sách Tool đang bật cho chu kỳ hiện tại.
                case PickToolSubState.Idle:
                    _pickToolState = PickToolSubState.PrepareToolList;
                    return false;

                case PickToolSubState.PrepareToolList:
                    _pickActiveTools.Clear();
                    _pickActiveTools.AddRange(GetEnabledTools());
                    _pickToolListIndex = 0;

                    if (_pickActiveTools.Count == 0)
                    {
                        AddMachineLog("[READY] Không còn Tool nào được bật hoặc Tool đều đã trượt quá số lần cho phép.");
                        _pickToolState = PickToolSubState.Complete;
                        return false;
                    }

                    _pickToolState = PickToolSubState.SelectTool;
                    return false;

                // Cây con bước 2: Chọn Tool tiếp theo. Mỗi Tool chỉ xử lý một sản phẩm trong chu kỳ.
                case PickToolSubState.SelectTool:
                    if (xpixel == null ||
                        ypixel == null ||
                        _readyProductIndex >= xpixel.Length ||
                        _readyProductIndex >= ypixel.Length ||
                        _pickToolListIndex >= _pickActiveTools.Count)
                    {
                        _pickToolState = PickToolSubState.Complete;
                        return false;
                    }

                    _pickCurrentTool = _pickActiveTools[_pickToolListIndex];
                    _pickToolListIndex++;
                    _pickCurrentToolName = GetToolName(_pickCurrentTool);

                    string tcp = $"TCP{_pickCurrentTool}";
                    int setTcpResult = _robot.SetTCPByNameHans(0, tcp);
                    if (setTcpResult != 0)
                    {
                        FailReadyCycle($"[READY] Không thể chọn {tcp} cho {_pickCurrentToolName}. Mã lỗi: {setTcpResult}.");
                        _pickToolState = PickToolSubState.Complete;
                        return true;
                    }

                    AddRobotHistory($"[READY] Đã chọn {tcp} cho {_pickCurrentToolName}.");
                    _pickToolState = PickToolSubState.LoadCalibration;
                    return false;

                // Cây con bước 3: Lấy calibration đúng theo Tool + Camera rồi đổi pixel sang robot.
                case PickToolSubState.LoadCalibration:
                    {
                        string cameraName = GetBasketCameraName(_readyCurrentBasket);
                     //   var affine = GetCameraAffine(cameraName: cameraName, toolName: _pickCurrentToolName);

                        var affine = GetCameraAffine(cameraName: cameraName, "Tool1");
                        if (affine == null)
                        {
                            AddMachineLog($"[READY] Chưa load calibration cho {_data.GetCalibName(_pickCurrentToolName, cameraName)}.");
                            _readyToolSuspended[_pickCurrentTool] = true;
                            _pickToolState = PickToolSubState.SelectTool;
                            return false;
                        }

                        var (robotX, robotY) = affine.PixelToRobot(xpixel[_readyProductIndex], ypixel[_readyProductIndex]);
                        _pickRobotX = robotX;
                        _pickRobotY = robotY;
                        _pickToolState = PickToolSubState.PickProduct;
                        return false;
                    }

                // Cây con bước 4: Move tới điểm gắp, bật hút, nếu trượt thì hạ RetryZ và hút lại.
                case PickToolSubState.PickProduct:
             

                    _pickCurrentOk = TryPickWithTool(_pickCurrentTool, _pickRobotX, _pickRobotY);
                    if (_state == AppState.Error)
                    {
                        _pickToolState = PickToolSubState.Complete;
                        return true;
                    }

                    _pickToolState = PickToolSubState.HandlePickResult;
                    return false;

                // Cây con bước 5: Xử lý kết quả hút OK/NG rồi chuyển Tool hoặc sản phẩm tiếp theo.
                case PickToolSubState.HandlePickResult:
                    if (!SetPickCylinderUpForTool(_pickCurrentTool))
                    {
                        FailReadyCycle($"[READY] Robot không điều khiển được xi lanh về vị trí lên cho {_pickCurrentToolName}. Dừng máy, cần Reset lỗi.");
                        _pickToolState = PickToolSubState.Complete;
                        return true;
                    }
                    //if (!MoveSafeZ(_pickCurrentTool, _pickRobotX, _pickRobotY))
                    //{
                    //    FailReadyCycle("[READY] Robot không nâng được lên độ cao an toàn H sau khi hút trượt. Dừng máy, cần Reset lỗi.");
                    //    _pickToolState = PickToolSubState.Complete;
                    //    return true;
                    //}

                    _pickCylinderConfirmStartedAtUtc = DateTime.UtcNow;
                    _pickToolState = PickToolSubState.ConfirmCylinderSensors;
                    return false;

                // Sau khi đầu hút đã nâng lên, cả ba cảm biến xi lanh phải ON.
                // Chỉ xác nhận kết quả hút và tăng bộ đếm trượt một lần tại đây.
                case PickToolSubState.ConfirmCylinderSensors:
                    bool readOk = TryReadPickCylinderSensors(out int di0, out int di2, out int di4);
                    if (readOk && di0 == 1 && di2 == 1 && di4 == 1)
                    {
                        AddRobotHistory("[READY] Xác nhận cảm biến xi lanh OK: DI0=1, DI2=1, DI4=1.");
                        _pickCylinderConfirmStartedAtUtc = DateTime.MinValue;

                        // Kiểm tra lại cảm biến hút sau khi đầu Tool đã nâng lên vị trí an toàn.
                        // SetSensor là chế độ bỏ qua cảm biến nên giữ nguyên kết quả mô phỏng trước đó.
                        bool holdingAfterLift =
                            _pickCurrentOk && (_data.SetSensor || IsToolHolding(_pickCurrentTool));

                        if (holdingAfterLift)
                        {
                            _readyToolHolding[_pickCurrentTool] = true;
                            _productLoaded = true;
                            _readyToolMissCount[_pickCurrentTool] = 0;
                            _readyFailedCapturePickCycles = 0;
                            _pickAttemptsPerTool[_pickCurrentTool] = 0;
                            AddRobotHistory($"[READY] {_pickCurrentToolName} hút OK Basket{_readyCurrentBasket} sản phẩm {_readyProductIndex + 1}.");
                            _readyProductIndex++;
                            _pickToolState = PickToolSubState.SelectTool;
                            return false;
                        }

                        // Một chu trình gắp thất bại chỉ được tính trượt đúng một lần ở đây.
                        SetToolVacuum(_pickCurrentTool, false);
                        _readyToolHolding[_pickCurrentTool] = false;
                        _readyToolMissCount[_pickCurrentTool]++;
                        _pickAttemptsPerTool[_pickCurrentTool]++;
                        AddMachineLog(
                            $"[READY] {_pickCurrentToolName} hút trượt sản phẩm {_readyProductIndex + 1}, " +
                            $"lần {_pickAttemptsPerTool[_pickCurrentTool]}/{MaxPickAttemptsPerToolPerImage} trong ảnh hiện tại.");
                        _readyProductIndex++;

                        bool canRetrySameTool =
                            _pickAttemptsPerTool[_pickCurrentTool] < MaxPickAttemptsPerToolPerImage &&
                            _readyProductIndex < xpixel.Length;

                        if (canRetrySameTool)
                        {
                            // SelectTool đã tăng index; giảm lại để lần kế tiếp vẫn chọn đúng Tool hiện tại.
                            _pickToolListIndex--;
                            _pickToolState = PickToolSubState.SelectTool;
                            return false;
                        }

                        AddMachineLog(
                            $"[READY] {_pickCurrentToolName} không hút được sau " +
                            $"{_pickAttemptsPerTool[_pickCurrentTool]} lần thử, chuyển sang Tool active kế tiếp.");

                        // Không quay lại các tọa độ đã hút trượt trong ảnh hiện tại.
                        // Tool kế tiếp tiếp tục từ sản phẩm chưa được thử tiếp theo.
                        _pickToolState = PickToolSubState.SelectTool;
                        return false;
                    }

                    if (DateTime.UtcNow - _pickCylinderConfirmStartedAtUtc >= PickCylinderConfirmTimeout)
                    {
                        string sensorStatus = readOk
                            ? $"DI0={di0}, DI2={di2}, DI4={di4}"
                            : "không đọc được DI0/DI2/DI4";
                        FailReadyCycle($"[READY] Quá 1 giây chưa xác nhận đủ cảm biến xi lanh ({sensorStatus}). Yêu cầu DI0=1, DI2=1, DI4=1. Dừng máy, cần Reset lỗi.");
                        _pickToolState = PickToolSubState.Complete;
                        return true;
                    }

                    return false;

                // Cây con kết thúc: trả quyền cho cây READY cha để nâng H, kiểm tra cảm biến và thả.
                case PickToolSubState.Complete:
                    ResetPickToolSubTree();
                    return true;

                default:
                    ResetPickToolSubTree();
                    return true;
            }
        }

        private bool HandleDropToolSubTree()
        {
            switch (_dropToolState)
            {
                // Cây con bước 1: Chuẩn bị đi tới điểm thả một lần cho tất cả Tool.
                case DropToolSubState.Idle:
                    int setDropTcpResult = _robot.SetTCPByNameHans(0, "TCP1");
                    if (setDropTcpResult != 0)
                    {
                        FailReadyCycle(
                            $"[READY] Không thể chọn TCP1 trước khi đi tới điểm thả đầu tiên. Mã lỗi: {setDropTcpResult}. Dừng máy, cần Reset lỗi.",
                            releaseVacuum: false);
                        _dropToolState = DropToolSubState.Complete;
                        return true;
                    }

                    AddRobotHistory("[READY] Đã chọn TCP1 trước khi đi tới ForwardPose1.");
                    _dropForwardPoseIndex = 1;
                    _dropReturnPoseIndex = 1;
                    _dropToolState = DropToolSubState.MoveForwardPose;
                    return false;

                // Cây con bước 2:
                // - MoveL riêng tới ForwardPose1 để chụp camera đúng tại điểm 1.
                // - Sau đó kiểm tra robot và chạy ABGO tới ForwardPose6.
                // Không chạy MoveL riêng cho ForwardPose2..ForwardPose6.
                case DropToolSubState.MoveForwardPose:
                    if (_dropForwardPoseIndex == 1)
                    {
                        const string poseName = "ForwardPose1";
                        if (!TrySetReadySpeed(
                                _data.SpeedMoveToDrop1,
                                "đi tới vị trí thả 1"))
                        {
                            _dropToolState = DropToolSubState.Complete;
                            return true;
                        }

                        if (!MoveToForwardPathStart())
                        {
                            FailReadyCycle(
                                $"[READY] Robot không di chuyển được tới {poseName}. Dừng máy, cần Reset lỗi.",
                                releaseVacuum: false);
                            _dropToolState = DropToolSubState.Complete;
                            return true;
                        }

                        AddRobotHistory($"[READY] Robot đi qua {poseName} trước khi thả các sản phẩm.");

                        // Chụp trước Basket khi robot đã tới vị trí thả 1, trừ khi đã nhận Stop.
                        // Khi Stop sau chu trình, robot chỉ hoàn tất gắp/thả rồi về Home.
                        if (_dropForwardPoseIndex == 1)
                        {
                            if (_stopAfterCycle)
                            {
                                AddMachineLog($"[STATE] Đã nhận Stop -> bỏ qua chụp Basket{_readyCurrentBasket} tại {poseName}.");
                            }
                            else if (!TriggerCurrentBasketCamera())
                            {
                                FailReadyCycle(
                                    $"[READY] Không trigger được camera tại {poseName} cho Basket{_readyCurrentBasket}. Dừng máy, cần Reset lỗi.",
                                    releaseVacuum: false);
                                _dropToolState = DropToolSubState.Complete;
                                return true;
                            }
                            else
                            {
                                AddMachineLog($"[READY] Đã chụp trước Basket{_readyCurrentBasket} tại {poseName}; chờ robot quay về mới dùng kết quả.");
                            }
                        }

                        _dropForwardPoseIndex = 2;
                        return false;
                    }

                    if (_dropForwardPoseIndex == 2)
                    {
                        if (!TryRunForwardDropMovePath(out string movePathError))
                        {
                            FailReadyCycle(
                                $"[READY] Robot không chạy được quỹ đạo {DropForwardPathName}: " +
                                $"{movePathError} Dừng máy, cần Reset lỗi.",
                                releaseVacuum: false);
                            _dropToolState = DropToolSubState.Complete;
                            return true;
                        }

                        _dropForwardPoseIndex = 6;
                        return false;
                    }

                    _dropToolState = DropToolSubState.ReleaseAllTools;
                    return false;

                // Cây con bước 3: Tại điểm thả, tắt hút đồng thời tất cả Tool đang giữ sản phẩm.
                case DropToolSubState.ReleaseAllTools:
                    var releasedTools = new List<string>();
                    for (int tool = 1; tool <= 3; tool++)
                    {
                        SetToolVacuum(tool, false);

                        if (!_readyToolHolding[tool])
                            continue;

                        _readyToolHolding[tool] = false;
                        releasedTools.Add($"Tool{tool}");
                    }

                    if (!SetBlowAirOutputs(true, "[READY][DROP]"))
                    {
                        // Có thể chỉ một phần output đã ON, nên luôn thử OFF cả ba
                        // trước khi dừng chu trình.
                        SetBlowAirOutputs(false, "[READY][DROP][SAFE]");
                        FailReadyCycle(
                            "[READY] Không bật được đầy đủ CO4, CO5, CO6 để thả sản phẩm. Dừng máy, cần Reset lỗi.");
                        _dropToolState = DropToolSubState.Complete;
                        return true;
                    }

                    Thread.Sleep(50);
                    if (!_startupRecoveryDrop)
                        RecordReleasedProducts(releasedTools.Count);
                    AddRobotHistory($"[READY] Thả đồng thời sản phẩm của {string.Join(", ", releasedTools)} tại điểm thả.");
                    _dropReturnPoseIndex = 1;
                    _dropToolState = DropToolSubState.MoveReturnPose;
                    return false;

                // Cây con bước 4: Sau khi thả tất cả sản phẩm, chạy một lần quỹ đạo
                // ABGOBACK tới ReturnPose6. Không chạy MoveL riêng ReturnPose1..ReturnPose6.
                case DropToolSubState.MoveReturnPose:
                    if (_dropReturnPoseIndex == 1)
                    {
                        if (!TryRunReturnDropMovePath(out string movePathError))
                        {
                            // OFF dự phòng, kể cả khi xung thổi tại điểm thả đã tắt thành công.
                            SetBlowAirOutputs(false, "[READY][DROP][SAFE]");
                            FailReadyCycle(
                                $"[READY] Robot không chạy được quỹ đạo {DropReturnPathName}: " +
                                $"{movePathError} Dừng máy, cần Reset lỗi.");
                            _dropToolState = DropToolSubState.Complete;
                            return true;
                        }

                        // Chỉ tắt khí thổi sau khi robot đã hoàn thành ABGOBACK.
                        if (!SetBlowAirOutputs(false, "[READY][DROP]"))
                        {
                            FailReadyCycle(
                                $"[READY] Robot đã hoàn thành {DropReturnPathName} nhưng không tắt được đầy đủ " +
                                "CO4, CO5, CO6. Dừng máy, cần Reset lỗi.");
                            _dropToolState = DropToolSubState.Complete;
                            return true;
                        }

                        _dropReturnPoseIndex = 6;
                        return false;
                    }

                    _dropToolState = DropToolSubState.Complete;
                    return false;

                // Cây con kết thúc: trả quyền cho READY cha để chụp/gắp tiếp hoặc về Home.
                case DropToolSubState.Complete:
                    ResetDropToolSubTree();
                    return true;

                default:
                    ResetDropToolSubTree();
                    return true;
            }
        }

        private bool TrySetReadySpeed(double speed, string stepName)
        {
            if (speed <= 0 || speed > 1)
            {
                FailReadyCycle(
                    $"[READY] Tốc độ bước {stepName} không hợp lệ: {speed:0.##}. " +
                    "Giá trị phải lớn hơn 0 và không vượt quá 1.");
                return false;
            }

            string result = _robot.SetOverride(0, speed);
            if (result != "OK")
            {
                FailReadyCycle(
                    $"[READY] Không cài được tốc độ {speed:0.##} cho bước {stepName}. " +
                    $"Dừng máy, lỗi: {result}");
                return false;
            }

            AddRobotHistory(
                $"[READY] Tốc độ bước {stepName}: {speed:0.##}.");
            return true;
        }

        private void HandleReady()
         {
            try
            {
                switch (_readyState)
                {
                    // Bước 0: Khởi tạo chu trình chạy theo cấu hình Settings.
                    // Tạo hàng đợi Basket: Basket1, Basket2 hoặc Basket1 -> Basket2 nếu chọn Both.
                    case ReadySubState.CheckStatus:

                        ResetReadyCycle();
                        BuildBasketQueue();
                        AddRobotHistory("[READY] CheckStatus -> Init Basket cycle");
                        if (_readyBasketQueue.Count == 0)
                        {
                            AddMachineLog("[READY] Chưa chọn Basket nào để chạy.");
                            FailReadyCycle("[READY] Chưa chọn Basket nào để chạy.");
                            break;
                        }
                       // _robot.SetOverride(0, 0.03);
                        _readyState = ReadySubState.InitBasketCycle;

                        break;

                  

                    // Bước 1: Nạp thông tin nền cho chu trình.
                    // PickProductPose dùng Z/RX/RY/RZ tham chiếu cho điểm gắp.
                    case ReadySubState.InitBasketCycle:
                        RobotTrajectory home = _db.GetRobotTrajectoryByNamePoses("HomePose");
                        RobotTrajectory prePick = _db.GetRobotTrajectoryByNamePoses("PrePickPose");
                        RobotTrajectory pickProduct = _db.GetRobotTrajectoryByNamePoses("PickProductPose");

                        if (home == null)
                        {
                            FailReadyCycle("[READY] Không tìm thấy HomePose trong database. Dừng máy, cần lưu HomePose trước khi Start.");
                            break;
                        }

                        if (prePick == null)
                        {
                            FailReadyCycle("[READY] Không tìm thấy PrePickPose trong database. Dừng máy, cần lưu PrePickPose trước khi Start.");
                            break;
                        }

                        if (pickProduct == null)
                        {
                            FailReadyCycle("[READY] Không tìm thấy PickProductPose trong database. Dừng máy, cần lưu PickProductPose trước khi Start.");
                            break;
                        }

                        if (home != null)
                        {
                            moveLHome.X = home.X;
                            moveLHome.Y = home.Y;
                            moveLHome.Z = home.Z;
                            moveLHome.RX = home.Rx;
                            moveLHome.RY = home.Ry;
                            moveLHome.RZ = home.Rz;
                        }

                        moveLPickProduct.X = pickProduct.X;
                        moveLPickProduct.Y = pickProduct.Y;
                        moveLPickProduct.Z = pickProduct.Z;
                        moveLPickProduct.RX = pickProduct.Rx;
                        moveLPickProduct.RY = pickProduct.Ry;
                        moveLPickProduct.RZ = pickProduct.Rz;

                        moveLPrePick.X = prePick.X;
                        moveLPrePick.Y = prePick.Y;
                        moveLPrePick.Z = prePick.Z;
                        moveLPrePick.RX = prePick.Rx;
                        moveLPrePick.RY = prePick.Ry;
                        moveLPrePick.RZ = prePick.Rz;

                        AddMachineLog($"[READY] Đã load HomePose, PrePickPose và PickProductPose từ database.");
                        _readyState = ReadySubState.SelectNextBasket;
                        break;

                    // Bước 2: Chọn Basket tiếp theo.
                    // Nếu chạy Both thì luôn xử lý hết Basket1 trước, sau đó mới chuyển Basket2.
                    case ReadySubState.SelectNextBasket:
                        if (_readyBasketQueue.Count == 0)
                        {
                            _readyState = ReadySubState.FinishAllBaskets;
                            break;
                        }
                        _readyCurrentBasket = _readyBasketQueue[0];
                        _readyBasketQueue.RemoveAt(0);
                        _readyCameraTimeoutCount = 0;
                        _readyEmptyConfirmCount = 0;
                        _readyProductIndex = 0;
                        Array.Clear(_readyToolSuspended, 0, _readyToolSuspended.Length);
                        Array.Clear(_readyToolMissCount, 0, _readyToolMissCount.Length);
                        ResetPickToolSubTree();
                        AddMachineLog($"[READY] Bắt đầu Basket{_readyCurrentBasket} ({GetBasketCameraName(_readyCurrentBasket)}).");
                        _readyState = ReadySubState.MoveClearCamera;
                        break;

                    // Bước 3: Robot đi đến vị trí không che camera.
                    // Hiện dùng PrePickPose làm vị trí đứng ngoài vùng nhìn camera.
                    case ReadySubState.MoveClearCamera:
                        if (!TrySetReadySpeed(
                                _data.SpeedCapture,
                                "đi tới vị trí chụp ảnh"))
                        {
                            break;
                        }

                        if (!MoveLoadedPose("PrePickPose", moveLPrePick))
                        {
                            FailReadyCycle("[READY] Robot không di chuyển được tới PrePickPose. Dừng máy, cần Reset lỗi.");
                            break;
                        }
                        _readyState = ReadySubState.TriggerBasketCamera;
                        break;

                    // Bước 4: Trigger camera tương ứng với Basket hiện tại.
                    // Basket1 -> Camera1/Flow1, Basket2 -> Camera2/Flow2.
                    case ReadySubState.TriggerBasketCamera:
                        if (!TriggerCurrentBasketCamera())
                        {
                            FailReadyCycle($"[READY] Không trigger được camera cho Basket{_readyCurrentBasket}. Dừng máy, cần Reset lỗi.");
                            break;
                        }
                        _readyState = ReadySubState.WaitBasketCamera;
                        break;

                    // Bước 5: Chờ callback VisionMaster trả danh sách pixel sản phẩm.
                    // Quá thời gian cấu hình chưa có callback thì chụp lại. Sau đủ số lần cấu hình,
                    // bỏ qua Basket hiện tại và chuyển sang Basket tiếp theo.
                    // Nếu không có sản phẩm thì chuyển sang bước chụp xác nhận Basket rỗng.
                    case ReadySubState.WaitBasketCamera:
                        if (!_readyCameraResultReady)
                        {
                            if (_readyCameraTriggeredAtUtc != DateTime.MinValue &&
                                DateTime.UtcNow - _readyCameraTriggeredAtUtc >= _readyCameraTimeout)
                            {
                                _readyCameraPending = false;
                                _readyCameraTriggeredAtUtc = DateTime.MinValue;
                                _readyCameraTimeoutCount++;
                                int maxTimeouts = Math.Max(MinimumFailedCapturePickCycles, _data.EmptyConfirmShots);

                                if (_readyCameraTimeoutCount >= maxTimeouts)
                                {
                                    if (_readyBasketQueue.Count > 0)
                                    {
                                        AddMachineLog($"[READY] Basket{_readyCurrentBasket} không trả kết quả sau {_readyCameraTimeoutCount}/{maxTimeouts} lần, chuyển Basket tiếp theo.");
                                        _readyState = ReadySubState.SelectNextBasket;
                                    }
                                    else
                                    {
                                        AddMachineLog($"[READY] Basket{_readyCurrentBasket} không trả kết quả sau {_readyCameraTimeoutCount}/{maxTimeouts} lần. Đã hết Basket, kết thúc và về Home.");
                                        _readyState = ReadySubState.FinishAllBaskets;
                                    }
                                }
                                else
                                {
                                    AddMachineLog(
                                        $"[READY] Basket{_readyCurrentBasket} không trả kết quả sau " +
                                        $"{_readyCameraTimeout.TotalSeconds:0.###} giây " +
                                        $"({_readyCameraTimeoutCount}/{maxTimeouts}), chụp lại.");
                                    _readyState = ReadySubState.TriggerBasketCamera;
                                }
                            }
                            break;
                        }

                        _readyCameraResultReady = false;
                        _readyCameraTriggeredAtUtc = DateTime.MinValue;
                        _readyCameraTimeoutCount = 0;
                        if (_readyCameraResultCount <= 0 || xpixel == null || ypixel == null || xpixel.Length == 0)
                        {
                            _readyState = ReadySubState.ConfirmBasketEmpty;
                            break;
                        }

                        if (!TrySortProductsForPicking(out string sortError))
                        {
                            FailReadyCycle($"[READY] Không thể sắp thứ tự sản phẩm: {sortError}");
                            break;
                        }

                        AddMachineLog($"[READY] Basket{_readyCurrentBasket} có {_readyCameraResultCount} sản phẩm.");
                        // Basket hiện tại còn sản phẩm nên không thể dùng lần xác nhận rỗng
                        // của Basket trước để kết luận cả hai Basket đã hết.
                        _readyEmptyBasketMask = 0;
                        _readyEmptyVerificationRounds = 0;
                        _readyEmptyVerificationStarted = false;
                        _readyProductIndex = 0;
                        // Camera thấy sản phẩm chưa có nghĩa là đầu hút đang giữ sản phẩm.
                        // Chỉ đặt _productLoaded=true sau khi cảm biến hút xác nhận thành công.
                        _productLoaded = false;
                        ResetPickToolSubTree();
                        _readyState = ReadySubState.PickByTools;
                        break;

                    // Bước 6: Chụp đủ 5 lần để xác nhận Basket thật sự hết sản phẩm.
                    // Nếu vẫn không thấy sản phẩm thì kết luận Basket hiện tại đã hết.
                    case ReadySubState.ConfirmBasketEmpty:
                        _readyEmptyConfirmCount++;
                        int requiredEmptyConfirmShots = EmptyConfirmShotsPerBasket;
                        AddMachineLog($"[READY] Basket{_readyCurrentBasket} không thấy sản phẩm, xác nhận {_readyEmptyConfirmCount}/{requiredEmptyConfirmShots}.");
                        if (_readyEmptyConfirmCount < requiredEmptyConfirmShots)
                        {
                            _readyState = ReadySubState.MoveClearCamera;
                            break;
                        }

                        AddMachineLog($"[READY] Basket{_readyCurrentBasket} đã hết sản phẩm sau {_readyEmptyConfirmCount} lần xác nhận.");

                        if (IsBothBasketMode())
                        {
                            _readyEmptyBasketMask |= 1 << (_readyCurrentBasket - 1);

                            // Lượt Basket1 -> Basket2 đầu tiên là lượt xử lý chính.
                            // Sau khi cả hai rỗng mới bắt đầu 3 vòng kiểm tra lại.
                            if (_readyEmptyBasketMask == 0b11)
                            {
                                if (!_readyEmptyVerificationStarted)
                                {
                                    _readyEmptyVerificationStarted = true;
                                    _readyEmptyVerificationRounds = 0;
                                    AddMachineLog(
                                        $"[READY] Basket1 và Basket2 đã hết. Bắt đầu kiểm tra lại " +
                                        $"{RequiredEmptyBasketVerificationRounds} vòng, mỗi Basket {requiredEmptyConfirmShots} lần chụp.");
                                }
                                else
                                {
                                    _readyEmptyVerificationRounds++;
                                    AddMachineLog(
                                        $"[READY] Hoàn tất vòng kiểm tra rỗng " +
                                        $"{_readyEmptyVerificationRounds}/{RequiredEmptyBasketVerificationRounds}.");

                                    if (_readyEmptyVerificationRounds >= RequiredEmptyBasketVerificationRounds)
                                    {
                                        AddMachineLog(
                                            $"[READY] Basket1 và Basket2 không có sản phẩm sau " +
                                            $"{RequiredEmptyBasketVerificationRounds} vòng kiểm tra. Kết thúc chương trình.");
                                        _readyState = ReadySubState.FinishAllBaskets;
                                        break;
                                    }
                                }

                                _readyEmptyBasketMask = 0;
                                _readyBasketQueue.Clear();
                                _readyBasketQueue.Add(1);
                                AddMachineLog("[READY] Quay lại Basket1 để bắt đầu vòng kiểm tra tiếp theo.");
                                _readyState = ReadySubState.SelectNextBasket;
                                break;
                            }

                            QueueOtherBasket();
                            AddMachineLog($"[READY] Chuyển sang Basket{_readyBasketQueue[0]} để tiếp tục kiểm tra và gắp.");
                        }

                        _readyState = ReadySubState.SelectNextBasket;
                        break;

                    // Bước 7: Gắp sản phẩm bằng các Tool đang bật.
                    // Mỗi Tool chỉ hút một sản phẩm thành công trong một chu kỳ.
                    case ReadySubState.PickByTools:
                        if (_state == AppState.Error)
                            break;

                        // Chỉ kiểm tra khi bắt đầu cây gắp của chu kỳ mới.
                        // Nếu lần đầu chưa đạt, lấy lại trạng thái tối đa 3 lần
                        // trước khi thực hiện cùng luồng với nút Stop trên Home.
                        if (_pickToolState == PickToolSubState.Idle &&
                            !ConfirmPlcReadyBeforePick(out string plcReadyError))
                        {
                            _data.StopRequested = true;
                            FailReadyCycle(
                                $"[READY] Dừng trước khi ra gắp sau 3 lần kiểm tra PLC: {plcReadyError}.");
                            break;
                        }

                        if (_pickToolState == PickToolSubState.Idle &&
                            !TrySetReadySpeed(
                                _data.SpeedSuction,
                                "hút sản phẩm"))
                        {
                            break;
                        }

                        if (!HandlePickToolSubTree())
                            break;

                        if (_state == AppState.Error)
                            break;

                        _readyState = ReadySubState.LiftSafeAfterPick;
                        break;

                    // Bước 8: Sau khi chạy hết các Tool trong chu kỳ, robot nâng lên độ cao an toàn H.
                    case ReadySubState.LiftSafeAfterPick:
                        //if (!MoveSafeZ())
                        //{
                        //    FailReadyCycle("[READY] Robot không nâng được lên độ cao an toàn H. Dừng máy, cần Reset lỗi.");
                        //    break;
                        //}
                        _readyState = ReadySubState.CheckHoldingProducts;
                        break;

                    // Bước 9: Kiểm tra cảm biến của cả 3 Tool.
                    // Nếu không Tool nào giữ sản phẩm thì về Home và dừng chương trình.
                    case ReadySubState.CheckHoldingProducts:
                        if (!HasAnyHoldingTool())
                        {
                            if (_stopPendingPickResult)
                            {
                                _stopPendingPickResult = false;
                                AddMachineLog(
                                    "[STATE] Đã hoàn tất hút sau yêu cầu Stop; không Tool nào có sản phẩm -> về Home.");
                                StopBecauseNoProductPicked();
                            }
                            else
                            {
                                RetryCaptureAfterFailedPickCycle();
                            }
                            break;
                        }

                        if (_stopPendingPickResult)
                        {
                            _stopPendingPickResult = false;
                            _stopAfterCycle = true;
                            AddMachineLog(
                                "[STATE] Đã hoàn tất hút sau yêu cầu Stop; có sản phẩm -> thả xong rồi về Home.");
                        }

                        ResetDropToolSubTree();
                        _readyState = ReadySubState.DropPickedProducts;
                        break;

                    // Bước 10: Đi thả những sản phẩm đã hút được.
                    // Robot đi ForwardPose1..ForwardPose6 một lần, thả đồng thời tất cả Tool đang giữ sản phẩm,
                    // rồi đi ReturnPose1..ReturnPose6 một lần để người vận hành có thể Pause giữa các vị trí.
                    case ReadySubState.DropPickedProducts:
                        if (!HandleDropToolSubTree())
                            break;

                        if (_state == AppState.Error)
                            break;

                        _productLoaded = false;

                        if (EnterFullWorkWaitIfRequired())
                            break;

                        ContinueAfterCompletedDrop(captureFreshImage: false);
                        break;

                    // Máy nhận được hai mẫu đầy ở hai lần thả liên tiếp:
                    // robot đã về Home và chờ người vận hành lấy sản phẩm ra.
                    // Trong lúc chờ, đèn xanh CO2 đảo trạng thái mỗi một giây.
                    case ReadySubState.WaitFullWorkClear:
                        if (!IsSelectedFullWorkSensorActive())
                        {
                            SetFullWorkLamp(false);
                            _fullWorkConsecutiveDropCount = 0;
                            _fullWorkNextLampToggleUtc = DateTime.MinValue;
                            ResumeCycleTimingAfterFullWork();
                            AddMachineLog(
                                $"[FULL WORK] {GetSelectedFullWorkSensorDescription()} đã về 0. " +
                                "Tắt CO2 và tiếp tục chương trình.");
                            ContinueAfterCompletedDrop(captureFreshImage: true);
                            break;
                        }

                        if (DateTime.UtcNow >= _fullWorkNextLampToggleUtc)
                        {
                            SetFullWorkLamp(!_fullWorkLampOn);
                            _fullWorkNextLampToggleUtc = DateTime.UtcNow.AddSeconds(1);
                        }
                        break;

                    // Bước 11: Tất cả Basket được chọn đã hết, robot về Home và kết thúc.
                    case ReadySubState.FinishAllBaskets:
                        if (!MoveNamedPose("HomePose"))
                        {
                            FailReadyCycle("[READY] Robot không về được HomePose khi kết thúc. Dừng máy, cần Reset lỗi.");
                            break;
                        }
                        AddMachineLog("[READY] Đã xử lý hết Basket, robot đã về Home. Kết thúc chương trình.");
                        _data.StopRequested = false;
                        _cycleActiveTime.Stop();
                        _machineRunTime.Stop();
                        _state = AppState.Idle;
                        _productLoaded = false;
                        _stopAfterCycle = false;
                        _startupRecoveryDrop = false;
                        _readyState = ReadySubState.CheckStatus;
                        break;
                            
                }
            }
            catch (Exception ex)
            {
                // Nếu exception xảy ra sau khi bắt đầu thả, không để van thổi
                // CO4..CO6 giữ ON khi state machine đã chuyển sang Error.
                if (_readyState == ReadySubState.DropPickedProducts)
                    SetBlowAirOutputs(false, "[READY][DROP][EXCEPTION-SAFE]");

                RaiseError(
                    $"Exception trong HandleReady tại trạng thái {_readyState}: {ex}");
            }
        }
        private void HandleControlRequests()
        {
            try
            {
                // 1. Xử lý ENABLE (Bật Servo đơn lẻ)
                if (_data.EnableReq)
                {
                    _data.EnableReq = false;
                    string controllerResult = _robot.ReadControllerState(out int controllerStarted);
                    if (controllerResult != "OK" || controllerStarted != 1)
                    {
                        AddMachineLog(
                            "[MANUAL] Không thể Enable: Controller chưa initialized. Hãy nhấn OPEN trước.");
                        return;
                    }

                    int enableResult = 0;
                    int[] rbtState = null;
                    string stateResult = "";
                    for (int attempt = 1; attempt <= 3; attempt++)
                    {
                        AddMachineLog($"[MANUAL] Enable Servo lần {attempt}/3 (GrpPowerOn)...");
                        enableResult = _robot.GrpPowerOn(0);

                        DateTime deadlineUtc = DateTime.UtcNow.AddSeconds(6);
                        do
                        {
                            Thread.Sleep(500);
                            stateResult = _robot.ReadRobotState(0, out int[] currentState);
                            if (stateResult == "OK" && currentState != null && currentState.Length >= 11)
                            {
                                rbtState = currentState;
                                if (rbtState[1] == 1)
                                    break;
                            }
                        }
                        while (DateTime.UtcNow < deadlineUtc);

                        if (rbtState != null && rbtState[1] == 1)
                            break;

                        if (attempt < 3)
                            Thread.Sleep(2000);
                    }

                    bool servoEnabled = rbtState != null && rbtState.Length >= 11 && rbtState[1] == 1;
                    AddMachineLog(
                        servoEnabled
                            ? "[MANUAL] Enable thành công - robot đang Standby."
                            : $"[MANUAL] Enable không thành công (phản hồi {enableResult}, trạng thái {stateResult}).");

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        _data.OpenOn = true;
                        _data.CloseOn = false;
                        _data.EnableOn = servoEnabled;
                        _data.DisableOn = !servoEnabled;
                    });
                }

                // 2. Xử lý DISABLE (Tắt Servo đơn lẻ)
                if (_data.DisableReq)
                {
                    _data.DisableReq = false;
                    AddMachineLog("[MANUAL] Đang gửi lệnh Disable Robot");

                    int res = _robot.GrpPowerOff(0); //

                    if (res == 0 || res == 20018)
                    {
                        Thread.Sleep(1000); // Chờ nhả phanh cơ học

                        int[] rbtState;
                        string errType = _robot.ReadRobotState(0, out rbtState); //

                        // rbtState[1] == 0 là trạng thái Tắt Servo an toàn
                        if (errType == "OK" && rbtState[1] == 0)
                        {
                            AddMachineLog("[MANUAL] Disable thành công.");
                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                _data.DisableOn = true;
                                _data.EnableOn = false;
                                _data.FreeDriveOn = false; // Tắt luôn FreeDrive
                            });
                        }
                        else
                        {
                            AddMachineLog("[MANUAL] CẢNH BÁO: Không thể Disable Servo vật lý.");
                        }
                    }
                    else AddMachineLog($"[MANUAL] Disable thất bại: {res}");
                }

                // 3. OPEN: đưa robot tới trạng thái Controller initialized/Disable.
                if (_data.OpenReq)
                {
                    _data.OpenReq = false;
                    AddMachineLog("[MANUAL] OPEN: Powered on -> Controller initialized...");

                    string stateResult = _robot.ReadRobotState(0, out int[] openState);
                    if (stateResult != "OK" || openState == null || openState.Length < 11)
                    {
                        AddMachineLog($"[MANUAL] OPEN thất bại: không đọc được trạng thái robot ({stateResult}).");
                        return;
                    }

                    if (openState[7] != 0 || openState[2] != 0 || openState[3] != 0 || openState[4] != 0)
                    {
                        AddMachineLog("[MANUAL] OPEN bị chặn: robot đang lỗi/Emergency. Hãy Reset trước.");
                        return;
                    }

                    bool wasPoweredOn = openState[9] == 1;
                    if (openState[9] == 0)
                    {
                        int electrifyResult = _robot.Electrify();
                        DateTime powerDeadlineUtc = DateTime.UtcNow.AddSeconds(10);
                        do
                        {
                            Thread.Sleep(500);
                            stateResult = _robot.ReadRobotState(0, out int[] currentState);
                            if (stateResult == "OK" && currentState != null && currentState.Length >= 11)
                            {
                                openState = currentState;
                                if (openState[9] == 1)
                                    break;
                            }
                        }
                        while (DateTime.UtcNow < powerDeadlineUtc);

                        if (openState[9] != 1)
                        {
                            AddMachineLog($"[MANUAL] OPEN thất bại tại Electrify ({electrifyResult}).");
                            return;
                        }
                    }

                    // Mỗi lần nhấn chỉ thực hiện đúng một bước như giao diện HANS:
                    // POWER ON xong thì nút đổi thành INITIALIZE.
                    if (!wasPoweredOn)
                    {
                        AddMachineLog("[MANUAL] POWER ON hoàn tất. Có thể nhấn INITIALIZE.");
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            _data.RobotPoweredOn = true;
                            _data.OpenOn = false;
                            _data.CloseOn = false;
                            _data.EnableOn = false;
                            _data.DisableOn = false;
                        });
                        return;
                    }

                    string controllerResult = _robot.ReadControllerState(out int controllerStarted);
                    int masterResult = 0;
                    for (int attempt = 1; attempt <= 3 && controllerStarted == 0; attempt++)
                    {
                        masterResult = _robot.StartMaster(0);
                        AddMachineLog(
                            $"[MANUAL] Initialize lần {attempt}/3 (StartMaster={masterResult})...");

                        DateTime controllerDeadlineUtc = DateTime.UtcNow.AddSeconds(20);
                        do
                        {
                            Thread.Sleep(500);
                            controllerResult = _robot.ReadControllerState(out controllerStarted);
                            if (controllerResult == "OK" && controllerStarted == 1)
                                break;
                        }
                        while (DateTime.UtcNow < controllerDeadlineUtc);
                    }

                    if (controllerStarted != 1)
                    {
                        AddMachineLog(
                            $"[MANUAL] OPEN thất bại: Controller chưa initialized " +
                            $"(StartMaster={masterResult}, ReadControllerState={controllerResult}).");
                        return;
                    }

                    AddMachineLog(
                        "[MANUAL] Controller initialized. Chờ axis group vào Disable (5 giây)...");
                    Thread.Sleep(5000);
                    stateResult = _robot.ReadRobotState(0, out openState);
                    bool alreadyEnabled =
                        stateResult == "OK" && openState != null && openState.Length >= 11 && openState[1] == 1;

                    AddMachineLog(
                        alreadyEnabled
                            ? "[MANUAL] OPEN hoàn tất - robot đang Standby."
                            : "[MANUAL] OPEN hoàn tất - robot đang Disable, có thể nhấn ENABLE.");
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        _data.RobotPoweredOn = true;
                        _data.OpenOn = true;
                        _data.CloseOn = false;
                        _data.EnableOn = alreadyEnabled;
                        _data.DisableOn = !alreadyEnabled;
                    });
                }

                // 4. CLOSE: Standby -> Disable -> đóng Master -> Blackout 48V.
                if (_data.CloseReq)
                {
                    _data.CloseReq = false;
                    AddMachineLog("[MANUAL] Đang thực hiện quy trình CLOSE an toàn...");

                    // BƯỚC 1: Khóa Free Drive (nếu đang bật) để tránh rơi tay máy
                    if (_data.FreeDriveOn)
                    {
                        AddMachineLog("[MANUAL] Đang khóa phanh Free Drive...");
                        _robot.GrpCloseFreeDriver(0);
                        Thread.Sleep(500); // Chờ phanh cơ học đóng lại
                    }

                    string closeStateResult = _robot.ReadRobotState(0, out int[] closeState);

                    // BƯỚC 2: Tắt Servo theo trạng thái robot thực tế.
                    if (closeStateResult == "OK"
                        && closeState != null
                        && closeState.Length >= 11
                        && closeState[1] == 1)
                    {
                        AddMachineLog("[MANUAL] CLOSE bước 1/3: Disable Servo...");
                        _robot.GrpPowerOff(0);
                        Thread.Sleep(1500);
                    }

                    // BƯỚC 3: Ngắt kết nối controller.
                    AddMachineLog("[MANUAL] CLOSE bước 2/3: CloseMaster...");
                    int closeMasterResult = _robot.CloseMaster();
                    Thread.Sleep(2500);

                    // BƯỚC 4: Cắt nguồn 48V để trở về Blackout như giao diện HANS.
                    AddMachineLog("[MANUAL] CLOSE bước 3/3: BlackOut 48V...");
                    int blackOutResult = _robot.BlackOut();
                    DateTime blackOutDeadlineUtc = DateTime.UtcNow.AddSeconds(10);
                    do
                    {
                        Thread.Sleep(500);
                        closeStateResult = _robot.ReadRobotState(0, out int[] currentState);
                        if (closeStateResult == "OK"
                            && currentState != null
                            && currentState.Length >= 11)
                        {
                            closeState = currentState;
                            if (closeState[9] == 0)
                                break;
                        }
                    }
                    while (DateTime.UtcNow < blackOutDeadlineUtc);

                    bool blackedOut =
                        closeState != null && closeState.Length >= 11 && closeState[9] == 0;
                    AddMachineLog(
                        blackedOut
                            ? "[MANUAL] CLOSE hoàn tất - robot đang Blackout 48V."
                            : $"[MANUAL] CLOSE chưa hoàn tất (CloseMaster={closeMasterResult}, " +
                              $"BlackOut={blackOutResult}, State={closeStateResult}).");

                    // Cập nhật nút theo trạng thái xác nhận được, không giả lập thành công.
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        _data.RobotPoweredOn = !blackedOut;
                        _data.OpenOn = !blackedOut;
                        _data.CloseOn = false;
                        _data.EnableOn = false;
                        _data.DisableOn = !blackedOut;
                        _data.FreeDriveOn = false;
                    });
                }
                // 5. Xử lý FREE DRIVE (Hoạt động khi Servo đang ENABLE)
                if (_data.FreeDriveReq)
                {
                    _data.FreeDriveReq = false;

                    // KIỂM TRA: Nếu Servo chưa ENABLE thì không cho mở Free Drive
                    if (!_data.EnableOn)
                    {
                        AddMachineLog("[MANUAL] Lỗi: Phải ENABLE Robot (Bật Servo) trước khi mở Free Drive!");
                        return;
                    }

                    if (!_data.FreeDriveOn)
                    {
                        AddMachineLog("[MANUAL] Đang yêu cầu MỞ Free Drive (Robot đang Enable)...");
                        int res = _robot.GrpOpenFreeDriver(0);

                        if (res == 0 || res == 20018)
                        {
                            AddMachineLog("[MANUAL] Mở Free Drive thành công. Bạn có thể kéo tay robot.");
                            Application.Current?.Dispatcher.Invoke(() => _data.FreeDriveOn = true);
                        }
                        else AddMachineLog($"[MANUAL] Lỗi Mở Free Drive: {res}");
                    }
                    else
                    {
                        AddMachineLog("[MANUAL] Đang yêu cầu KHÓA Free Drive...");
                        int res = _robot.GrpCloseFreeDriver(0);

                        if (res == 0 || res == 20018)
                        {
                            AddMachineLog("[MANUAL] Đã khóa phanh Free Drive.");
                            Application.Current?.Dispatcher.Invoke(() => _data.FreeDriveOn = false);
                        }
                        else AddMachineLog($"[MANUAL] Lỗi Khóa Free Drive: {res}");
                    }
                }
                // 6. Xử lý RESET ROBOT
                if (_data.ResetRobotReq)
                {
                    _data.ResetRobotReq = false;
                    AddMachineLog("[MANUAL] Đang thực hiện Reset Robot...");
                    TurnOffAllOutputs();
                    int res = _robot.GrpReset(0); // Gọi hàm reset từ ConmandRobot 

                    if (res == 0) AddMachineLog("[MANUAL] Reset Robot thành công.");
                    else AddMachineLog($"[MANUAL] Lỗi Reset Robot: {res}");
                }

                // 7. Xử lý STATUS ROBOT (Đọc trạng thái chi tiết)
                if (_data.StatusRobotReq)
                {
                    _data.StatusRobotReq = false;
                    AddMachineLog("[MANUAL] Đang kiểm tra trạng thái chi tiết Robot...");

                    int[] data;
                    string errType = _robot.ReadRobotState(0, out data); // Đọc mảng data 15 phần tử 

                    if (errType == "OK")
                    {
                        // Duyệt qua mảng data và ghi vào Log tương tự logic richTextBox của bạn
                        string statusInfo = "--- ROBOT STATUS ---\n";
                        for (int i = 0; i <= 12; i++)
                        {
                            switch (i)
                            {
                                case 0: statusInfo += $"0: MovingState: {(data[i] == 0 ? "No movement" : "In motion")}\n"; break;
                                case 1: statusInfo += $"1: PowerState: {(data[i] == 0 ? "De-enable" : "Enable")}\n"; break;
                                case 2: statusInfo += $"2: ErrorState: {(data[i] == 0 ? "No error" : "Error reported")}\n"; break;
                                case 3: statusInfo += $"3: ErrorCode: {data[i]}\n"; break;
                                case 7: statusInfo += $"7: Emergency: {(data[i] == 0 ? "No Stop" : "EMG STOPPED")}\n"; break;
                                case 9: statusInfo += $"9: Electrify: {(data[i] == 0 ? "No Power" : "Powered On")}\n"; break;
                                case 10: statusInfo += $"10: Connection: {(data[i] == 0 ? "Not Connected" : "Connected")}\n"; break;
                            }
                        }
                        AddMachineLog(statusInfo);
                    }
                    else
                    {
                        AddMachineLog($"[MANUAL] Lỗi đọc trạng thái: {errType}");
                    }
                }
            }
            catch (Exception ex)
            {
                AddMachineLog($"[MANUAL][CONTROL][ERROR] {ex.Message}");
            }
        }
        // === MANUAL ===
        private void HandleManual()
        {
            UpdateManualStatusIfDue();

            if (_state != AppState.Idle)
            {
                ClearPendingManualRequests();
                if (!_manualBlockedLogged)
                {
                    AddMachineLog(
                        $"[MANUAL][BLOCKED] Không cho phép điều khiển Manual Robot khi máy đang ở trạng thái {_state}. " +
                        "Hãy nhấn STOP và chờ máy về trạng thái Idle.");
                    _manualBlockedLogged = true;
                }
                return;
            }

            _manualBlockedLogged = false;

            // 1) Nếu bấm Manual Step 1
            switch (_manualState)
            {

                case ManualSubState.MoveRobot:
                    // TODO: logic manual (Jog, move,...)
                    _manualState = ManualSubState.CheckSensor;
                    break;

                case ManualSubState.CheckSensor:
                    HandleControlRequests();
                    HandleOutputRequests();    // xử lý output người dùng bấm
                                              
                    PosMoveL pos = UpdateRealtimePosition();

                    // 2. Nếu đọc thành công (pos != null) thì mới truyền vào hàm Jog
                    if (pos != null)
                    {
                        ManualRobot(pos);
                    }
                    _manualState = ManualSubState.MoveRobot;
                    break;
            }
        }

        private void UpdateManualStatusIfDue()
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc < _nextManualStatusUpdateUtc)
                return;

            _nextManualStatusUpdateUtc = nowUtc.Add(ManualStatusUpdateInterval);
            ReadSensorAndUpdateUI();
        }

        // === SETTINGS ===
        private void HandleSettings()
        {
            // ❌ Không cho chỉnh settings nếu không Idle
            if (_state != AppState.Idle)
            {
                bool hasPendingSettingsRequest =
                    _data.FUpdatePose ||
                    _data.RequestEditPose ||
                    _data.RequestMovePose;

                // Clear tất cả request để không bị “dồn lệnh” sang lúc Idle
                _data.FUpdatePose = false;
                _data.RequestEditPose = false;
                _data.RequestMovePose = false;
                _data.MovePoseName = null;

                if (hasPendingSettingsRequest)
                {
                    AutoCloseToast.ShowError(
                        "Robot chưa ở trạng thái Idle. Lệnh Move/Lưu điểm đã bị hủy.",
                        2500,
                        "Không thể thực hiện");
                }
                return;
            }
            switch (_settingsState)
            {
                case SettingsSubState.WaitUserEdit:
                    if (_data.RequestTriggerCamera)
                    {
                        _data.RequestTriggerCamera = false;
                        //  HandleTriggerCamera(); // Gọi hàm xử lý Trigger
                        _activeTriggerCamera = _data.SelectedTriggerCamera;
                        _activeCalibTool = "Tool1";

                        string flowName = GetTriggerFlowName(_activeTriggerCamera);
                        var pro = VmSolution.Instance[flowName] as VmProcedure;
                        if (pro != null)
                        {
                            _settingsTriggerCameraPending = true;
                            pro.Run();
                        }
                        else
                        {
                            _settingsTriggerCameraPending = false;
                            AddMachineLog($"[SETTING] Lỗi: Không tìm thấy {flowName} để chạy Trigger Camera.");
                        }
                    }
                    if (_data.RequestSavePositionTrigger)
                    {
                        _data.RequestSavePositionTrigger = false;
                        HandleSavePositionTrigger(_data.IndexTrigger);
                    }

                   // if (_data.FUpdatePose)
                        _settingsState = SettingsSubState.SaveChanges;

                    // ==============================================================
                    // 1. XỬ LÝ LỆNH UPDATE/EDIT VỊ TRÍ
                    // ==============================================================
                    if (_data.RequestEditPose && _data.PoseToEdit != null)
                    {
                        // Đọc trực tiếp tọa độ thực tế, KHÔNG cần SetUCSByName
                        string kq = _robot.ReadActualPos(0);
                        string[] array = kq.Split(',');

                        if (array[0] == "OK")
                        {
                            RobotTrajectory robotTrajectory = new RobotTrajectory();
                            robotTrajectory.X = double.Parse(array[1], CultureInfo.InvariantCulture);
                            robotTrajectory.Y = double.Parse(array[2], CultureInfo.InvariantCulture);
                            robotTrajectory.Z = double.Parse(array[3], CultureInfo.InvariantCulture);
                            robotTrajectory.Rx = double.Parse(array[4], CultureInfo.InvariantCulture);
                            robotTrajectory.Ry = double.Parse(array[5], CultureInfo.InvariantCulture);
                            robotTrajectory.Rz = double.Parse(array[6], CultureInfo.InvariantCulture);

                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                _data.PoseToEdit.X = robotTrajectory.X;
                                _data.PoseToEdit.Y = robotTrajectory.Y;
                                _data.PoseToEdit.Z = robotTrajectory.Z;
                                _data.PoseToEdit.Rx = robotTrajectory.Rx;
                                _data.PoseToEdit.Ry = robotTrajectory.Ry;
                                _data.PoseToEdit.Rz = robotTrajectory.Rz;
                            });
                        }
                        else
                        {
                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                MessageBox.Show("Lỗi đọc vị trí từ Robot: " + array[0]);
                            });
                        }

                        _data.RequestEditPose = false;
                        _data.PoseToEdit = null;
                    }

                    // ==============================================================
                    // 2. XỬ LÝ LỆNH MOVE (DỊCH CHUYỂN TỚI ĐIỂM ĐÃ DẠY)
                    // ==============================================================
                    if (_data.RequestMovePose && !string.IsNullOrEmpty(_data.MovePoseName))
                    {
                        string poseName = _data.MovePoseName;
                        var moveType = _data.MoveTypeToMove;

                        RobotTrajectory traj = _db.GetRobotTrajectoryByNamePoses(poseName);

                        if (traj != null)
                        {
                            AddMachineLog($"[SETTING] Đang di chuyển robot tới điểm: {poseName} ({moveType})...");
                            AutoCloseToast.ShowSuccess(
                                $"Đang di chuyển robot tới {poseName} ({moveType})...",
                                1800,
                                "Đang thực hiện");

                            string moveErr = ""; // Biến để hứng lỗi từ Robot

                            if (moveType == RobotTrajectory.MoveTypeEnum.moveL)
                            {
                                PosMoveL posMoveL = new PosMoveL();
                                double v = 0.02; // Tôi tăng tốc độ chạy thử lên 5% để chắc chắn robot không bị timeout vì quá chậm
                                _robot.SetOverride(0, v);
                                posMoveL.X = traj.X; posMoveL.Y = traj.Y; posMoveL.Z = traj.Z; posMoveL.RX = traj.Rx; posMoveL.RY = traj.Ry; posMoveL.RZ = traj.Rz;

                                // Hứng kết quả trả về
                                moveErr = _robot.MoveL(0, posMoveL, 0);
                            }
                            else
                            {
                                double v = 0.02;
                                _robot.SetOverride(0, v);
                                PosMoveJ posMoveJ = new PosMoveJ();
                                posMoveJ.J1 = traj.J1; posMoveJ.J2 = traj.J2; posMoveJ.J3 = traj.J3; posMoveJ.J4 = traj.J4; posMoveJ.J5 = traj.J5; posMoveJ.J6 = traj.J6;

                                // Hứng kết quả trả về
                                moveErr = _robot.MoveJ(0, posMoveJ);
                            }

                            // Nếu Robot trả về không phải chữ OK, in ngay lỗi ra màn hình
                            if (moveErr != "OK")
                            {
                                AddMachineLog($"[SETTING] Di chuyển tới {poseName} thất bại: {moveErr}");
                                AutoCloseToast.ShowError(
                                    $"Robot từ chối di chuyển tới {poseName}. Mã lỗi: {moveErr}",
                                    3000,
                                    "Lỗi lệnh Move");
                            }
                            else
                            {
                                AddMachineLog($"[SETTING] Di chuyển thành công tới {poseName}");
                                AutoCloseToast.ShowSuccess(
                                    $"Robot đã di chuyển tới {poseName} ✔",
                                    2200,
                                    "Hoàn tất");
                            }
                        }
                        else
                        {
                            AddMachineLog($"[SETTING] Không tìm thấy dữ liệu điểm: {poseName}");
                            AutoCloseToast.ShowError(
                                $"Chưa có dữ liệu cho điểm {poseName}. Hãy lưu điểm trước.",
                                3000,
                                "Không thể Move");
                        }

                        _data.RequestMovePose = false;
                        _data.MovePoseName = null;
                    }
                    break;

                case SettingsSubState.SaveChanges:
                    if (_data.RequestSaveAllPositionsTrigger == true)
                    {
                        _data.RequestSaveAllPositionsTrigger = false;
                        HandleSaveAllPositions();
                    }
                    if (_data.FUpdatePose)
                    {
                        string poseName = _data.NamePose;

                        // ĐỌC TRỰC TIẾP TỌA ĐỘ BỎ QUA SetUCSByName
                        string kq = _robot.ReadActualPos(0);
                        string[] array = kq.Split(',');

                        if (array[0] == "OK")
                        {
                            RobotTrajectory robotTrajectory = new RobotTrajectory();
                            robotTrajectory.X = double.Parse(array[1], CultureInfo.InvariantCulture);
                            robotTrajectory.Y = double.Parse(array[2], CultureInfo.InvariantCulture);
                            robotTrajectory.Z = double.Parse(array[3], CultureInfo.InvariantCulture);
                            robotTrajectory.Rx = double.Parse(array[4], CultureInfo.InvariantCulture);
                            robotTrajectory.Ry = double.Parse(array[5], CultureInfo.InvariantCulture);
                            robotTrajectory.Rz = double.Parse(array[6], CultureInfo.InvariantCulture);
                            robotTrajectory.J1 = double.Parse(array[7], CultureInfo.InvariantCulture);
                            robotTrajectory.J2 = double.Parse(array[8], CultureInfo.InvariantCulture);
                            robotTrajectory.J3 = double.Parse(array[9], CultureInfo.InvariantCulture);
                            robotTrajectory.J4 = double.Parse(array[10], CultureInfo.InvariantCulture);
                            robotTrajectory.J5 = double.Parse(array[11], CultureInfo.InvariantCulture);
                            robotTrajectory.J6 = double.Parse(array[12], CultureInfo.InvariantCulture);

                            robotTrajectory.NamePoses = poseName;

                            // Lưu vào Database
                            _db.UpdateTrajectory(robotTrajectory);

                            AddMachineLog($"[SETTING] Đã lưu tọa độ thành công cho: {poseName}");
                            AutoCloseToast.ShowSuccess(
                                $"Đã lưu vị trí {poseName} ✔",
                                2200,
                                "Lưu điểm thành công");
                        }
                        else
                        {
                            AddMachineLog($"[SETTING] Lưu {poseName} thất bại: {array[0]}");
                            AutoCloseToast.ShowError(
                                $"Không thể đọc vị trí robot để lưu {poseName}. Lỗi: {array[0]}",
                                3000,
                                "Lưu điểm thất bại");
                        }

                        _data.FUpdatePose = false;
                    }
                    _settingsState = SettingsSubState.WaitUserEdit;
                    break;
            }
        }

        // === ERROR STATE ===
        private void HandleError()
        {
            // Chờ người vận hành nhấn Reset trên UI
            if (_data.ResetRequested)
            {
                _data.ResetRequested = false;
                AddMachineLog("[ERROR] Người vận hành nhấn RESET, thử reset robot...");
                TurnOffAllOutputs();

                try
                {
                    if (!TryResetRobotError(out string resetError))
                    {
                        AddMachineLog($"[ERROR] Reset robot thất bại: {resetError}");
                        AddRobotHistory($"[ERROR][ROBOT STATUS] {resetError}");
                        return; // vẫn ở Error
                    }

                    AddRobotHistory("[RESET][ROBOT STATUS] Đã xóa lỗi robot thành công.");
                    ClearErrorStatus();
                    ResetReadyCycle();

                // Tắt đèn đỏ
              

                // Reset chỉ xóa lỗi và đưa máy về trạng thái Stop/Idle.
                // Robot chỉ được di chuyển khi người vận hành nhấn Home riêng.
                    _data.HomeRequested = false;
                    AddMachineLog("[ERROR] Reset OK -> chuyển sang IDLE, không di chuyển robot.");
                    _machineRunTime.Stop();
                    _state = AppState.Idle;
                    _readyState = ReadySubState.CheckStatus;
                    _productLoaded = false;
                    _stopAfterCycle = false;
                }
                finally
                {
                    _data.IsResetProcessing = false;
                }
            }
        }

        private void PollRobotControlInputs()
        {
            string result = _robot.ReadBoxCI_01234567(out int[] ci);
            if (result != "OK" || ci == null || ci.Length < 7)
            {
                // Chỉ ghi một lần trong suốt khoảng mất kết nối để không làm đầy log
                // vì vòng lặp nền đọc lại liên tục.
                if (!_robotControlCiReadFailed)
                {
                    AddMachineLog(
                        $"[CONTROL INPUT][ERROR] Không đọc được CI4/CI5/CI6: {result}.");
                    _robotControlCiReadFailed = true;
                }
                return;
            }

            if (_robotControlCiReadFailed)
            {
                AddMachineLog("[CONTROL INPUT] Đã đọc lại được CI4/CI5/CI6.");
                _robotControlCiReadFailed = false;
            }

            bool ci4Start = ci[4] == 1;
            bool ci5Stop = ci[5] == 1;
            bool ci6Reset = ci[6] == 1;

            bool startRising = ci4Start && !_lastCi4Start;
            bool stopRising = ci5Stop && !_lastCi5Stop;
            bool resetRising = ci6Reset && !_lastCi6Reset;

            // Lưu trạng thái trước khi tạo request để tín hiệu giữ ở mức 1
            // chỉ tương đương một lần nhấn nút.
            _lastCi4Start = ci4Start;
            _lastCi5Stop = ci5Stop;
            _lastCi6Reset = ci6Reset;

            // Nếu nhiều nút cùng được nhấn, ưu tiên lệnh an toàn hơn.
            if (resetRising)
            {
                _data.IsResetProcessing = true;
                _data.ResetRequested = true;
                AddMachineLog("[CONTROL INPUT] CI6 rising edge -> Reset requested.");
                return;
            }

            if (stopRising)
            {
                _data.StopRequested = true;
                AddMachineLog("[CONTROL INPUT] CI5 rising edge -> Stop requested.");
                return;
            }

            if (startRising)
            {
                _data.StartRequested = true;
                AddMachineLog("[CONTROL INPUT] CI4 rising edge -> Start requested.");
            }
        }

        private void ClearPendingManualRequests()
        {
            _data.EnableReq = false;
            _data.DisableReq = false;
            _data.OpenReq = false;
            _data.CloseReq = false;
            _data.FreeDriveReq = false;
            _data.ResetRobotReq = false;
            _data.StatusRobotReq = false;

            _data.JogXPlusReq = false;
            _data.JogXMinusReq = false;
            _data.JogYPlusReq = false;
            _data.JogYMinusReq = false;
            _data.JogZPlusReq = false;
            _data.JogZMinusReq = false;
            _data.JogRXPlusReq = false;
            _data.JogRXMinusReq = false;
            _data.JogRYPlusReq = false;
            _data.JogRYMinusReq = false;
            _data.JogRZPlusReq = false;
            _data.JogRZMinusReq = false;

            // Không giữ lệnh output Manual để tránh tự kích hoạt khi máy trở lại Idle.
            _data.Cylinder1 = false;
            _data.Cylinder2 = false;
            _data.Cylinder3 = false;
            _data.Vacuum1 = false;
            _data.Vacuum2 = false;
            _data.Vacuum3 = false;
            _data.PushAir1 = false;
            _data.PushAir2 = false;
            _data.PushAir3 = false;
            _data.TriggerCamera = false;
            _data.BuzzerOn = false;
            _data.RedLampOn = false;
            _data.YellowLampOn = false;
            _data.GreenLampOn = false;
        }

        private void TurnOffAllOutputs()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _data.PushAir1 = false;
                _data.PushAir2 = false;
                _data.PushAir3 = false;
                _data.SubPush = false;
                _data.Cylinder1 = false;
                _data.Cylinder2 = false;
                _data.Cylinder3 = false;
                _data.GreenLampOn = false;

                _data.Vacuum1 = false;
                _data.Vacuum2 = false;
                _data.Vacuum3 = false;
                _data.TriggerCamera = false;
                _data.BuzzerOn = false;
                _data.RedLampOn = false;
                _data.YellowLampOn = false;
                _data.EnableOn = false;
                _data.DisableOn = false;
                _data.OpenOn = false;
                _data.CloseOn = false;
            });

            var errors = new List<string>();
            for (int bit = 0; bit < 8; bit++)
            {
                string doResult = _robot.SetSerialDO(bit, 0);
                if (doResult != "OK")
                {
                    errors.Add($"DO{bit}: {doResult}");
                }

                string coResult = _robot.SetBoxCO(bit, 0);
                if (coResult != "OK")
                {
                    errors.Add($"CO{bit}: {coResult}");
                }
            }

            if (errors.Count == 0)
            {
                AddMachineLog("[RESET] Đã OFF toàn bộ DO0..DO7 và CO0..CO7.");
            }
            else
            {
                AddMachineLog($"[RESET][OUTPUT][ERROR] Không thể OFF một số output: {string.Join("; ", errors)}");
            }
        }

        private void TurnOffBlowAirOutputs()
        {
            SetBlowAirOutputs(false, "[START]");
        }

        private bool SetBlowAirOutputs(bool on, string logPrefix)
        {
            var errors = new List<string>();
            for (int bit = 4; bit <= 6; bit++)
            {
                string result = _robot.SetBoxCO(bit, on ? 1 : 0);
                if (result != "OK")
                {
                    errors.Add($"CO{bit}: {result}");
                }
            }

            if (errors.Count == 0)
            {
                AddMachineLog(
                    $"{logPrefix} Đã {(on ? "ON" : "OFF")} CO4, CO5, CO6 - " +
                    $"{(on ? "bật" : "tắt")} chế độ thổi khí.");
                return true;
            }

            AddMachineLog(
                $"{logPrefix}[OUTPUT][ERROR] Không thể {(on ? "bật" : "tắt")} hết " +
                $"chế độ thổi khí: {string.Join("; ", errors)}");
            return false;
        }

        // === OUTPUT REQUESTS ===
        private void HandleOutputRequests()
        {
            try
            {
                // ===== GHI DO0..DO7 =====
                _robot.SetSerialDO(0, _data.Cylinder1 ? 1 : 0);      // DO0 = XL1
                _robot.SetSerialDO(1, _data.Cylinder2 ? 1 : 0);      // DO1 = XL2
                _robot.SetSerialDO(2, _data.Cylinder3 ? 1 : 0);      // DO2 = XL3
                _robot.SetSerialDO(3, _data.Vacuum1 ? 1 : 0);        // DO3 = SC1
                _robot.SetSerialDO(4, _data.Vacuum2 ? 1 : 0);        // DO4 = SC2
                _robot.SetSerialDO(5, _data.Vacuum3 ? 1 : 0);        // DO5 = SC3
                _robot.SetSerialDO(6, _data.TriggerCamera ? 1 : 0);   // DO6 = Trigger camera

                // ===== GHI CO0..CO6 =====
                _robot.SetBoxCO(0, _data.RedLampOn ? 1 : 0);         // CO0 = Red
                _robot.SetBoxCO(1, _data.YellowLampOn ? 1 : 0);      // CO1 = Yellow
                _robot.SetBoxCO(2, _data.GreenLampOn ? 1 : 0);       // CO2 = Green
                _robot.SetBoxCO(3, _data.BuzzerOn ? 1 : 0);          // CO3 = Buzzer
                _robot.SetBoxCO(4, _data.PushAir1 ? 1 : 0);          // CO4 = Blow1
                _robot.SetBoxCO(5, _data.PushAir2 ? 1 : 0);          // CO5 = Blow2
                _robot.SetBoxCO(6, _data.PushAir3 ? 1 : 0);          // CO6 = Blow3
              //  _robot.SetBoxCO(5, _data.EnableOn ? 1 : 0);         // CO5 = Enable
              //  _robot.SetBoxCO(6, _data.DisableOn ? 1 : 0);        // CO6 = Disable
               // _robot.SetBoxCO(7, _data.OpenOn ? 1 : 0);           // CO7 = Open(1)/Close(0)

                // Không gán readback DO/CO ngược vào các biến đang bind với
                // ToggleButton. Robot có thể trả về trạng thái cũ trong một chu kỳ,
                // làm nút tự OFF rồi chu kỳ sau lại ON liên tục.
            }
            catch (Exception ex)
            {
                AddMachineLog($"[OUTPUT][WRITE][ERROR] {ex.Message}");
            }
        }

        private void ReadSensorAndUpdateUI()
        {
            // ===== ĐỌC DI0..DI7: cảm biến xi lanh =====
            int[] di = new int[8];
            string kq = _robot.ReadBoxDI_01234567(out di);
            if (kq == "OK")
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _data.Xl1Down = di[0] == 1;
                    _data.Xl1Up   = di[1] == 1;
                    _data.Xl2Down = di[2] == 1;
                    _data.Xl2Up   = di[3] == 1;
                    _data.Xl3Down = di[4] == 1;
                    _data.Xl3Up   = di[5] == 1;
                });
            }
            else
            {
                AddMachineLog($"[ERROR] Read DI robot {kq}");
            }

            // ===== ĐỌC CI0..CI7: cửa và nút nhấn =====
            int[] ci = new int[8];
            kq = _robot.ReadBoxCI_01234567(out ci);
            if (kq == "OK")
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _data.Door1 = ci[0] == 1;
                    _data.Door2 = ci[1] == 1;
                    _data.Door3 = ci[2] == 1;
                    _data.Door4 = ci[3] == 1;
                    _data.Start = ci[4] == 1;
                    _data.Stop  = ci[5] == 1;
                    _data.Reset = ci[6] == 1;
                });
            }
            else
            {
                AddMachineLog($"[ERROR] Read CI robot {kq}");
            }

            // ===== PLC X0..X7 / Modbus addresses 20480..20487 =====
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _data.MayPolishing = _toolSensorRtu.IsMachine1Full;
                _data.MaySeatFinishin = _toolSensorRtu.IsMachine2Full;
                _data.Basket1 = _toolSensorRtu.IsBasket1Ready;
                _data.Basket2 = _toolSensorRtu.IsBasket2Ready;
                _data.AirP = _toolSensorRtu.IsAirPressureReady;
                _data.SsSc1 = _toolSensorRtu.IsToolHolding(1);
                _data.SsSc2 = _toolSensorRtu.IsToolHolding(2);
                _data.SsSc3 = _toolSensorRtu.IsToolHolding(3);
                _data.LampRed = _data.RedLampOn;
                _data.LampYellow = _data.YellowLampOn;
                _data.LampGreen = _data.GreenLampOn;
                _data.Buzzer = _data.BuzzerOn;
            });
        }

        // Thêm tham số đầu vào 'currentPos'
        partial void ManualRobot(PosMoveL currentPos)
        {
            // 1. Kiểm tra xem có bất kỳ yêu cầu Jog nào không
            string axis = "";
            int direction = 0;

            if (_data.JogXPlusReq) { axis = "X"; direction = 1; _data.JogXPlusReq = false; }
            else if (_data.JogXMinusReq) { axis = "X"; direction = -1; _data.JogXMinusReq = false; }
            else if (_data.JogYPlusReq) { axis = "Y"; direction = 1; _data.JogYPlusReq = false; }
            else if (_data.JogYMinusReq) { axis = "Y"; direction = -1; _data.JogYMinusReq = false; }
            else if (_data.JogZPlusReq) { axis = "Z"; direction = 1; _data.JogZPlusReq = false; }
            else if (_data.JogZMinusReq) { axis = "Z"; direction = -1; _data.JogZMinusReq = false; }
            else if (_data.JogRXPlusReq) { axis = "RX"; direction = 1; _data.JogRXPlusReq = false; }
            else if (_data.JogRXMinusReq) { axis = "RX"; direction = -1; _data.JogRXMinusReq = false; }
            else if (_data.JogRYPlusReq) { axis = "RY"; direction = 1; _data.JogRYPlusReq = false; }
            else if (_data.JogRYMinusReq) { axis = "RY"; direction = -1; _data.JogRYMinusReq = false; }
            else if (_data.JogRZPlusReq) { axis = "RZ"; direction = 1; _data.JogRZPlusReq = false; }
            else if (_data.JogRZMinusReq) { axis = "RZ"; direction = -1; _data.JogRZMinusReq = false; }

            if (axis == "") return; // Không có nút nào được nhấn

            // === ĐOẠN NÀY ĐÃ BỊ XÓA VÌ ĐÃ CÓ currentPos TỪ THAM SỐ TRUYỀN VÀO ===
            /* PosMoveL currentPos;
            string er = _robot.ReadActualPosMoveL(0, out currentPos);
            if (er != "OK") return;
            */
            // ====================================================================

            // 2. Tính toán STEP (Sử dụng trực tiếp currentPos)
            double stepValue;
            if (axis == "X" || axis == "Y" || axis == "Z")
            {
                stepValue = _data.IsStepMode ? _data.StepMM : 0.1;
            }
            else // Các trục xoay RX, RY, RZ
            {
                stepValue = _data.IsStepMode ? _data.StepDegree : 0.1;
            }

            double delta = stepValue * direction;

            // 3. Cộng dồn vào trục tương ứng
            switch (axis)
            {
                case "X": currentPos.X += delta; break;
                case "Y": currentPos.Y += delta; break;
                case "Z": currentPos.Z += delta; break;
                case "RX": currentPos.RX += delta; break;
                case "RY": currentPos.RY += delta; break;
                case "RZ": currentPos.RZ += delta; break;
            }

            // 4. Gửi lệnh di chuyển
            _robot.SetOverride(0, 0.05);
            string er = _robot.MoveL(0, currentPos, 0);

            if (er == "OK")
            {
                AddMachineLog($"[MANUAL] Jog {axis} {direction}: Thành công (Step: {stepValue})");
            }
            else
            {
                AddMachineLog($"[MANUAL] Jog {axis} {direction}: Thất bại - {er}");
            }
        }
        // Thêm hàm cập nhật vị trí thời gian thực
        private PosMoveL UpdateRealtimePosition()
        {
            PosMoveL currentPos;
            // Đọc vị trí thực tế
            string er = _robot.ReadActualPosMoveL(0, out currentPos);

            if (er == "OK")
            {
                // Cập nhật lên UI
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _data.CurrentX = currentPos.X;
                    _data.CurrentY = currentPos.Y;
                    _data.CurrentZ = currentPos.Z;
                    _data.CurrentRx = currentPos.RX;
                    _data.CurrentRy = currentPos.RY;
                    _data.CurrentRz = currentPos.RZ;
                });

                // TRẢ VỀ GIÁ TRỊ VỪA ĐỌC ĐƯỢC
                return currentPos;
            }

            // Nếu lỗi thì trả về null
            return null;
        }

        // ============ TRIGGER CAMERA LOGIC ============

        private void HandleTriggerCamera(int count)
        {
            try
            {
                AddMachineLog("[TRIGGER] Bắt đầu gọi camera...");

                // BƯỚC 1: Gọi camera service lấy số
             //   int count = GetNumberFromCameraService();

                if (count <= 0)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        _data.NumTriggerCamera = 0;
                        _data.RobotPositionList.Clear();
                        _data.IsSaveAllSuccess = false;
                        _data.ShowTriggerPositions = false;
                    });
                    AddMachineLog("[TRIGGER] Lỗi: Camera trả về số không hợp lệ: " + count);
                    AutoCloseToast.ShowError("Camera Error: Invalid number", 1000);
                    return;
                }

                AddMachineLog($"[TRIGGER] Camera trả về: {count} vị trí");
                listRobot = new TriggerPosItem[count];
                for(int i=0; i < count; i++)
                {
                    listRobot[i] = new TriggerPosItem();

                }
                // ✅ Ensure all UI-bound changes happen on UI thread in one block
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    // update numeric label
                    _data.NumTriggerCamera = count;
                    // tạo mảng mới với đúng số lượng phần tử
                    // update ObservableCollection that ItemsControl binds to
                    // clear + add items so CollectionChanged fires on UI thread

                    _data.RobotPositionList.Clear();
                    for (int i = 1; i <= count; i++)
                    {
                        _data.RobotPositionList.Add(new RobotPositionItem
                        {
                            PositionId = i,
                            PositionName = $"Position {i}",
                            IsStatus = false
                        });
                    }

                    _data.IsSaveAllSuccess = false;
                    _data.ShowTriggerPositions = true;
                    AddMachineLog($"[TRIGGER] (UI) Created {_data.RobotPositionList.Count} Save buttons");
                });
                AutoCloseToast.ShowSuccess($"Trigger Success: {count} positions ✔", 1000);
            }
            catch (Exception ex)
            {
                AddMachineLog($"[TRIGGER][ERROR] {ex.Message}");
                AutoCloseToast.ShowError($"Trigger Error: {ex.Message}", 1000);
            }
        }

        // Hàm lấy số từ camera service
        private int GetNumberFromCameraService()
        {
            try
            {
                // TODO: Thay bằng logic gọi camera thực tế
                // Ví dụ:
                // - Gọi API camera
                // - Gọi DLL/SDK camera
                // - Gọi COM port giao tiếp camera
                
                // Demo: random số từ 3-10 để test
                Random rand = new Random();
                int count = rand.Next(3, 10);
                
                AddMachineLog($"[CAMERA] Lấy được số: {count}");
                return count;
            }
            catch (Exception ex)
            {
                AddMachineLog($"[CAMERA][ERROR] Lỗi gọi camera: {ex.Message}");
                return -1;
            }
        }

        // Hàm lưu vị trí được trigger
        private void HandleSavePositionTrigger(int positionId)
        {
            try
            {
                AddMachineLog($"[TRIGGER] Đang lưu vị trí {positionId}...");

                // BƯỚC 1: Đọc tọa độ thực tế từ robot
                string kq = _robot.ReadActualPos(0);
                string[] array = kq.Split(',');

                if (array[0] != "OK")
                {
                    AddMachineLog($"[TRIGGER] Lỗi đọc vị trí: {array[0]}");
                    AutoCloseToast.ShowError("Error reading robot position", 1000);
                    return;
                }

                // BƯỚC 2: Tạo object RobotTrajectory với tên đặc biệt
                RobotTrajectory trajectory = new RobotTrajectory();
                trajectory.X = double.Parse(array[1], CultureInfo.InvariantCulture);
                trajectory.Y = double.Parse(array[2], CultureInfo.InvariantCulture);
                trajectory.Z = double.Parse(array[3], CultureInfo.InvariantCulture);
                trajectory.Rx = double.Parse(array[4], CultureInfo.InvariantCulture);
                trajectory.Ry = double.Parse(array[5], CultureInfo.InvariantCulture);
                trajectory.Rz = double.Parse(array[6], CultureInfo.InvariantCulture);
                trajectory.J1 = double.Parse(array[7], CultureInfo.InvariantCulture);
                trajectory.J2 = double.Parse(array[8], CultureInfo.InvariantCulture);
                trajectory.J3 = double.Parse(array[9], CultureInfo.InvariantCulture);
                trajectory.J4 = double.Parse(array[10], CultureInfo.InvariantCulture);
                trajectory.J5 = double.Parse(array[11], CultureInfo.InvariantCulture);
                trajectory.J6 = double.Parse(array[12], CultureInfo.InvariantCulture);

                // Đặt tên vị trí theo pattern "TriggerPos_1", "TriggerPos_2", ...
                trajectory.NamePoses = $"TriggerPos_{positionId}";
                listRobot[positionId-1]= new TriggerPosItem
                {
                    Id = positionId,
                    PosMoveL = new PosMoveL
                    {
                        X = trajectory.X,
                        Y = trajectory.Y,
                        Z = trajectory.Z,
                        RX = trajectory.Rx,
                        RY = trajectory.Ry,
                        RZ = trajectory.Rz
                    },
                    IsStatus = true // đã save
                };

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var uiItem = _data.RobotPositionList.FirstOrDefault(x => x.PositionId == positionId);
                    if (uiItem != null)
                        uiItem.IsStatus = true;
                });

                //// BƯỚC 3: Lưu vào Database
                //_db.UpdateTrajectory(trajectory);

                AddMachineLog($"[TRIGGER] Đã lưu vị trí {positionId} thành công: {trajectory.NamePoses}");
                AutoCloseToast.ShowSuccess($"Saved {trajectory.NamePoses} ✔", 2000);
            }
            catch (Exception ex)
            {
                AddMachineLog($"[TRIGGER][SAVE][ERROR] {ex.Message}");
                AutoCloseToast.ShowError($"Save Error: {ex.Message}", 1000);
            }
        }
        RobotPointCalib[] robotPointCalib;
        private void HandleSaveAllPositions()
        {
            if (!TryCheckAllStatus(listRobot, out int badIdx))
            {
                AddMachineLog($"[CALIB] Position lỗi IsStatus=false tại index={badIdx} (ID={(badIdx >= 0 && badIdx < listRobot.Length ? listRobot[badIdx].Id : -1)})");
                AutoCloseToast.ShowError("Cannot save all: Not all positions are ready", 2000);
                _data.RequestSaveAllPositionsTrigger = false;
                return;
            }

            // ✅ OK hết -> lưu vào DB
            robotPointCalib = new RobotPointCalib[listRobot.Length];
            // Lưu tuần tự từng position
            for(int i=0; i < listRobot.Length; i++)
            {
                robotPointCalib[i] = new RobotPointCalib();
                robotPointCalib[i].Angle = 0;
                robotPointCalib[i].RobotX = listRobot[i].PosMoveL.X;
                robotPointCalib[i].RobotY = listRobot[i].PosMoveL.Y;
                robotPointCalib[i].ImageX = xpixel[i];
                robotPointCalib[i].ImageY = ypixel[i];
            }

            // ✅ Lưu riêng theo Tool + Camera đang chọn trên UI, ví dụ Tool1_Camera1 hoặc Tool2_Camera2.
            string selectedTool = "Tool1";
            string selectedCamera = _data.SelectedTriggerCamera;
            string selectedCalibName = _data.GetCalibName(selectedTool, selectedCamera);

            foreach (var point in robotPointCalib)
                point.NameCalib = selectedCalibName;

            _db.SaveCalibPointsToDb(robotPointCalib, selectedCalibName);
            _data.RequestSaveAllPositionsTrigger = false;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var targetPoints = string.Equals(selectedCamera, "Camera2", StringComparison.OrdinalIgnoreCase)
                    ? _data.CalibPointsCamera2
                    : _data.CalibPointsCamera1;

                targetPoints.Clear();
                foreach (var p in robotPointCalib)
                    targetPoints.Add(p);

                foreach (var item in _data.RobotPositionList)
                    item.IsStatus = true;

                Affine2D? affine = targetPoints.Count >= 3
                    ? Affine2D.FitFromCalibPoints(targetPoints)
                    : null;
                _data.SetCalibAffine(selectedTool, selectedCamera, affine);

                if (string.Equals(selectedCamera, "Camera2", StringComparison.OrdinalIgnoreCase))
                {
                    _data.AffineCamera2 = affine;
                    _data._affine2 = affine;
                }
                else
                {
                    _data.AffineCamera1 = affine;
                    _data._affine1 = affine;
                }

                _data.IsSaveAllSuccess = true;
            });
            AddMachineLog($"[CALIB] Đã lưu tất cả {listRobot.Length} điểm calibration vào '{selectedCalibName}' thành công");
            AutoCloseToast.ShowSuccess($"Saved all {listRobot.Length} calibration points to {selectedCalibName} ✔", 2000);
        }
        private bool TryCheckAllStatus(TriggerPosItem[] listRobot, out int badIndex)
        {
            badIndex = -1;
            if (listRobot == null || listRobot.Length == 0)
            {
                badIndex = 0;
                return false;
            }

            for (int i = 0; i < listRobot.Length; i++)
            {
                if (!listRobot[i].IsStatus)
                {
                    badIndex = i;          // index 0-based
                    return false;
                }
            }

            return true;
        }
    }


}
