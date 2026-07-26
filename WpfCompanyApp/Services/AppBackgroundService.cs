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

        // ✅ đã kẹp sản phẩm sau bước CompleteSP hay chưa
        private bool _productLoaded = false;

        // ✅ có yêu cầu dừng sau khi chạy hết chu trình hiện tại không
        private bool _stopAfterCycle = false;

        // Cycle time is measured only while the machine is actually Running.
        // One sample represents one successfully released product.
        private readonly Stopwatch _cycleActiveTime = new Stopwatch();
        private readonly Stopwatch _machineRunTime = new Stopwatch();
        private readonly Queue<double> _recentProductCycleSeconds = new Queue<double>();
        private double _activeSecondsAtLastRelease;
        private int _completedProductCount;
        private int _displayCompletedProductCount;

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

        bool IsRobotInsideWorkspace(IReadOnlyList<PosMoveL> boundary, PosMoveL robotPos, double heightOffset)
        {
            if (boundary == null || boundary.Count != 10)
                return false;

            // ---- 1. Lấy Z_MIN từ mặt đáy ----
            double zMin = boundary.Min(p => p.Z) - 100;
            double zMax = zMin + heightOffset;

            // ---- 2. Kiểm tra theo Z (trục đứng) ----
            if (robotPos.Z < zMin || robotPos.Z > zMax)
                return false;

            // ---- 3. Kiểm tra theo mặt XY (đa giác WorkP1..WorkP10) ----
            return IsPointInPolygonXY(boundary, robotPos);
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
        float[] xpixel;
        float[] ypixel;
        bool triggerRun = false;
        private string _activeTriggerCamera = "Camera1";
        private string _activeCalibTool = "Tool1";
        private bool _settingsTriggerCameraPending = false;

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
                                            //cycletime 	string vmResult = vmProcedure.ModuResult.GetOutputString("time").astStringVal[0].strValue;
                                            string vmResult = vmProcedure.ModuResult.GetOutputString("ketqua").astStringVal[0].strValue;
                                            xpixel = vmProcedure.ModuResult.GetOutputFloat("outX").pFloatVal;
                                            ypixel = vmProcedure.ModuResult.GetOutputFloat("outY").pFloatVal;
                                            HandleVisionTriggerResult(xpixel.Length);
                                            try
                                            {
                                                AddMachineLog(vmResult);
                                            }
                                            catch (Exception ex)
                                            {

                                            }
                                        }
                                        catch
                                        {

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
<<<<<<< HEAD
                                        //int monthNumber = DateTime.Now.Month;
                                        //string monthAbbreviation = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedMonthNames[monthNumber - 1];
                                        //string path = DCSInfo.pathimage + "\\Logs\\" + monthAbbreviation + DateTime.Now.Day + DateTime.Now.Year+"\\" + NameMode + "\\"  + NameMode + "_" + monthAbbreviation + DateTime.Now.Day + DateTime.Now.Year + ".csv";
                                        //if (!File.Exists(path))
                                        //{
                                        //	indexTotal = 1;
                                        //	//try
                                        //	//{
                                        //	//	GlobalVariableModuleTool tool = new GlobalVariableModuleTool();
                                        //	//	tool.SetGlobalVar("serial", "1");
                                        //	//	tool.SetGlobalVar("namesolution", NameMode);
                                        //	//}
                                        //	//catch
                                        //	//{

                                        //	//}
                                        //}
=======
                                      

>>>>>>> 04688cd (Refactor application workflows and update UI components)
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
                                            //cycletime 	string vmResult = vmProcedure.ModuResult.GetOutputString("time").astStringVal[0].strValue;
                                            string vmResult = vmProcedure.ModuResult.GetOutputString("ketqua").astStringVal[0].strValue;
                                            xpixel = vmProcedure.ModuResult.GetOutputFloat("outX").pFloatVal;
                                            ypixel = vmProcedure.ModuResult.GetOutputFloat("outY").pFloatVal;
                                            HandleVisionTriggerResult(xpixel.Length);
                                            try
                                            {
                                                AddMachineLog(vmResult);
                                            }
                                            catch (Exception ex)
                                            {

                                            }
                                        }
                                        catch
                                        {

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
            _cycleActiveTime.Restart();
            _machineRunTime.Restart();
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

        private void UpdateProductionDisplay()
        {
            if (_data.ClearCycleRequested)
            {
                _data.ClearCycleRequested = false;

                _cycleActiveTime.Reset();
                if (_state == AppState.Running)
                    _cycleActiveTime.Start();

                _machineRunTime.Reset();
                if (_state == AppState.Running || _state == AppState.Error)
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
                _data.CycleCount = _displayCompletedProductCount;
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
                        LoadJob();
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
        private void LoadJob()
        {
            try
            {
                if (_data.LoadJob)
                {
                    _data.LoadJob = false;
                    VmSolutionInfo vmSolutionInfo = new VmSolutionInfo();
                    string path111 = AppDomain.CurrentDomain.BaseDirectory + "Solution\\" + _data.JobName + ".sol";
                    vmSolutionInfo.vmSolutionPath = path111;
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        // VisionMaster configuration controls contain WPF/Win32 UI.
                        // Keep the complete solution lifecycle on the UI thread so
                        // native parameter dialogs (for example Camera) can open.
                        if (VmSolution.Instance.SolutionPath != null)
                        {
                            VmSolution.Save();
                            VmSolution.Instance.CloseSolution();
                        }

                        VmSolution.Load(vmSolutionInfo.vmSolutionPath, "196370");
                        vmProcessInfoList = VmSolution.Instance.GetAllProcedureList();
                        vmProcedure = VmSolution.Instance[
                            vmProcessInfoList.astProcessInfo[0].strProcessName] as VmProcedure;
                        _data.ModuleSource = vmProcedure;
                    });
                    AutoCloseToast.ShowSuccess("Load Solution successfulg ✔", 1000);
                }
                
           
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

                _robotConnectAttemptLogged = false;
                _robotConnectFailureLogged = false;
                _data.HomeData = $"Đã load config: IP={_ipRobot}, Port={_portRobot}, TO={_readTimeout}";
                AddMachineLog(
                    $"[ROBOT TCP] Đã tải cấu hình từ [RobotTCP]: IP={_ipRobot}, " +
                    $"Port={_portRobot}, TimeOut={_readTimeout} ms.");
                
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

        // IDLE: chờ Start / Home => coi như trạng thái STOP
        private void HandleIdle()
        {
            if (_data.StartRequested)
            {
                _data.StartRequested = false;

                if (!TryValidateStartInterlocks(out string startInterlockError))
                {
                    RaiseError($"[START] Không cho phép chạy máy: {startInterlockError}");
                    return;
                }

                AddMachineLog("[STATE] Start requested -> RUNNING");
           
                _readyState = ReadySubState.CheckStatus;
                _state = AppState.Running;
                StartCycleStatistics();

                // reset cờ
                _stopAfterCycle = false;
                _productLoaded = false;
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
                // Nếu bạn có hàm reset lỗi robot:
                // bool resetOk = _robot.ResetError();
                // if (!resetOk)
                // {
                //     AddMachineLog("[ERROR] Reset robot thất bại trong IDLE.");
                // }
                // else
                // {
                //     AddMachineLog("[STATE] Reset robot OK trong IDLE.");
                // }

                // Clear cờ lỗi trên phần mềm (nếu đang còn)
                _hasError = false;
                _lastError = "";

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _data.HasError = false;
                    _data.ErrorMessage = "";
                });

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
            // ❌ Không cho Reset khi RUNNING
            if (_data.ResetRequested)
            {
                _data.ResetRequested = false;
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
                if (_productLoaded)
                {
                    // ĐÃ kẹp sản phẩm (sau CompleteSP):
                    // -> phải chạy hết chu trình rồi về Home
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

        private void HandlePaused()
        {
            // Stop trong PAUSED cũng giống Running:
            if (_data.StopRequested)
            {
                _data.StopRequested = false;
                _cycleActiveTime.Stop();
                _machineRunTime.Stop();

                if (_productLoaded)
                {
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
                        // Vùng an toàn là lăng trụ tạo bởi đa giác WorkP1..WorkP10.
                        double heightOffset = 500;
                        PosMoveL movel2 = new PosMoveL();
                        string er = _robot.ReadActualPosMoveL(0, out movel2);
                        if (er == "OK")
                        {
                            if (!TryLoadWorkspaceBoundary(out List<PosMoveL> workspaceBoundary, out string workspaceError))
                            {
                                AddMachineLog($"[HOMING] {workspaceError} Không cho phép Move Home.");
                            }
                            else if (IsRobotInsideWorkspace(workspaceBoundary, movel2, heightOffset))
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

                // 1. Kiểm tra an toàn: Đang chạy thì không cho tắt
                if (_state == AppState.Running || _state == AppState.Homing)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("Máy đang chạy! Không được nhấn ShutDown.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                    return;
                }

                // 2. Hỏi xác nhận người dùng
                bool isConfirm = false;
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var result = MessageBox.Show("Bạn có chắc chắn muốn TẮT toàn bộ hệ thống (Robot & PC) không?",
                                                 "Xác nhận Shutdown", MessageBoxButton.YesNo, MessageBoxImage.Error);
                    isConfirm = (result == MessageBoxResult.Yes);
                });

                if (isConfirm)
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

                if (_state == AppState.Running || _state == AppState.Homing)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("Máy đang chạy! Vui lòng ấn STOP trước khi nhấn Restart.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                    return;
                }

                bool isConfirm = false;
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var result = MessageBox.Show("Bạn có chắc chắn muốn KHỞI ĐỘNG LẠI máy tính không?",
                                                 "Xác nhận Restart", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    isConfirm = (result == MessageBoxResult.Yes);
                });

                if (isConfirm)
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
        private static readonly TimeSpan ReadyCameraTimeout = TimeSpan.FromSeconds(5);
        private int _readyCameraTimeoutCount = 0;
        private int _readyEmptyConfirmCount = 0;
        // Dùng cho chế độ Both: chỉ kết thúc khi hai Basket khác nhau được xác nhận
        // rỗng liên tiếp. Nếu Basket kế tiếp còn sản phẩm thì chuỗi xác nhận bị xóa.
        private int _readyLastEmptyBasket = 0;
        private int _readyProductIndex = 0;
        private readonly bool[] _readyToolHolding = new bool[4];
        private readonly bool[] _readyToolSuspended = new bool[4];
        private readonly int[] _readyToolMissCount = new int[4];
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
            _readyLastEmptyBasket = 0;
            _readyProductIndex = 0;
            Array.Clear(_readyToolHolding, 0, _readyToolHolding.Length);
            Array.Clear(_readyToolSuspended, 0, _readyToolSuspended.Length);
            Array.Clear(_readyToolMissCount, 0, _readyToolMissCount.Length);
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
        }

        private void ResetDropToolSubTree()
        {
            _dropToolState = DropToolSubState.Idle;
            _dropForwardPoseIndex = 1;
            _dropReturnPoseIndex = 1;
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

        private bool MoveSafeZ()
        {
            PosMoveL current;
            string er = _robot.ReadActualPosMoveL(0, out current);
            if (er != "OK")
            {
                AddMachineLog($"[READY] Không đọc được vị trí để nâng H: {er}");
                return false;
            }

            current.Z += _data.SafeH;
            er = _robot.MoveL(0, current, 0);
            if (er != "OK")
            {
                AddMachineLog($"[READY] Nâng H lỗi: {er}");
                return false;
            }

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
            var stopwatch = Stopwatch.StartNew();

            do
            {
                if (IsToolHolding(tool))
                    return true;

                if (stopwatch.ElapsedMilliseconds < timeoutMs)
                    Thread.Sleep(20);
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
            return WaitForToolHolding(tool);
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

        private bool TryValidateStartInterlocks(out string error)
        {
            if (!_toolSensorRtu.IsCommunicationHealthy)
            {
                error = "chưa có kết nối Modbus RTU với PLC.";
                return false;
            }

            var plcNotReady = new List<string>();
            if (!_toolSensorRtu.IsBasket1Ready)
                plcNotReady.Add("Basket1 chưa sẵn sàng (X2/20482 phải bằng 1)");
            if (!_toolSensorRtu.IsBasket2Ready)
                plcNotReady.Add("Basket2 chưa sẵn sàng (X3/20483 phải bằng 1)");
            if (!_toolSensorRtu.IsAirPressureReady)
                plcNotReady.Add("áp suất khí tổng chưa đủ (X4/20484 phải bằng 1)");

            if (plcNotReady.Count > 0)
            {
                error = string.Join("; ", plcNotReady) + ".";
                return false;
            }

            AddMachineLog(
                "[START] Điều kiện PLC OK: Basket1 X2/20482=1, " +
                "Basket2 X3/20483=1, áp suất khí X4/20484=1.");

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

        private bool ConfirmPlcReadyBeforePick(out string error)
        {
            const int maxAttempts = 3;
            const int delayBetweenAttemptsMs = 100;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                bool communicationOk = _toolSensorRtu.IsCommunicationHealthy;
                bool basket1Ready = _toolSensorRtu.IsBasket1Ready;
                bool basket2Ready = _toolSensorRtu.IsBasket2Ready;
                bool airPressureReady = _toolSensorRtu.IsAirPressureReady;

                if (communicationOk &&
                    basket1Ready &&
                    basket2Ready &&
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
                if (!_toolSensorRtu.IsBasket1Ready)
                    notReady.Add("Basket1 chưa sẵn sàng (X2/20482=0)");
                if (!_toolSensorRtu.IsBasket2Ready)
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
            _data.StopRequested = true;
            _readyState = ReadySubState.CheckStatus;
        }

        private void FailReadyCycle(string message)
        {
            SetToolVacuum(1, false);
            SetToolVacuum(2, false);
            SetToolVacuum(3, false);
            triggerRun = false;
            _readyCameraPending = false;
            _readyCameraResultReady = false;
            ResetReadyCycle();
            _readyState = ReadySubState.CheckStatus;
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
                    if (_readyProductIndex >= xpixel.Length || _pickToolListIndex >= _pickActiveTools.Count)
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
                    if (!MoveSafeZ())
                    {
                        FailReadyCycle("[READY] Robot không nâng được lên độ cao an toàn H sau khi hút trượt. Dừng máy, cần Reset lỗi.");
                        _pickToolState = PickToolSubState.Complete;
                        return true;
                    }

                    _pickCylinderConfirmStartedAtUtc = DateTime.UtcNow;
                    _pickToolState = PickToolSubState.ConfirmCylinderSensors;
                    return false;

                // Sau MoveSafeZ, cả ba cảm biến xi lanh phải ON trước khi xử lý kết quả hút.
                case PickToolSubState.ConfirmCylinderSensors:
                    bool readOk = TryReadPickCylinderSensors(out int di0, out int di2, out int di4);
                    if (readOk && di0 == 1 && di2 == 1 && di4 == 1)
                    {
                        AddRobotHistory("[READY] Xác nhận cảm biến xi lanh OK: DI0=1, DI2=1, DI4=1.");
                        _pickCylinderConfirmStartedAtUtc = DateTime.MinValue;

                        if (_pickCurrentOk)
                        {
                            _readyToolHolding[_pickCurrentTool] = true;
                            _readyToolMissCount[_pickCurrentTool] = 0;
                            AddRobotHistory($"[READY] {_pickCurrentToolName} hút OK Basket{_readyCurrentBasket} sản phẩm {_readyProductIndex + 1}.");
                            _readyProductIndex++;
                            _pickToolState = PickToolSubState.SelectTool;
                            return false;
                        }

                        _readyToolMissCount[_pickCurrentTool]++;
                        AddMachineLog($"[READY] {_pickCurrentToolName} hút trượt {_readyToolMissCount[_pickCurrentTool]}/{_data.MaxToolMissCount}.");
                        if (_readyToolMissCount[_pickCurrentTool] >= _data.MaxToolMissCount)
                        {
                            _readyToolSuspended[_pickCurrentTool] = true;
                            AddMachineLog($"[READY] Tạm ngừng {_pickCurrentToolName} do trượt liên tiếp.");
                        }

                        _readyProductIndex++;
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
                        FailReadyCycle($"[READY] Không thể chọn TCP1 trước khi đi tới điểm thả đầu tiên. Mã lỗi: {setDropTcpResult}. Dừng máy, cần Reset lỗi.");
                        _dropToolState = DropToolSubState.Complete;
                        return true;
                    }

                    AddRobotHistory("[READY] Đã chọn TCP1 trước khi đi tới ForwardPose1.");
                    _dropForwardPoseIndex = 1;
                    _dropReturnPoseIndex = 1;
                    _dropToolState = DropToolSubState.MoveForwardPose;
                    return false;

                // Cây con bước 2: Robot đi tuần tự ForwardPose1..ForwardPose5 một lần.
                // Mỗi lần move xong trả quyền về HandleRunning để nút Pause có thể dừng trước vị trí tiếp theo.
                case DropToolSubState.MoveForwardPose:
                    if (_dropForwardPoseIndex <= 5)
                    {
                        string poseName = $"ForwardPose{_dropForwardPoseIndex}";
                        if (!MoveNamedPose(poseName))
                        {
                            FailReadyCycle($"[READY] Robot không di chuyển được tới {poseName}. Dừng máy, cần Reset lỗi.");
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
                                FailReadyCycle($"[READY] Không trigger được camera tại {poseName} cho Basket{_readyCurrentBasket}. Dừng máy, cần Reset lỗi.");
                                _dropToolState = DropToolSubState.Complete;
                                return true;
                            }
                            else
                            {
                                AddMachineLog($"[READY] Đã chụp trước Basket{_readyCurrentBasket} tại {poseName}; chờ robot quay về mới dùng kết quả.");
                            }
                        }

                        _dropForwardPoseIndex++;
                        return false;
                    }

                    _dropToolState = DropToolSubState.ReleaseAllTools;
                    return false;

                // Cây con bước 3: Tại điểm thả, tắt hút đồng thời tất cả Tool đang giữ sản phẩm.
                case DropToolSubState.ReleaseAllTools:
                    var releasedTools = new List<string>();
                    for (int tool = 1; tool <= 3; tool++)
                    {
                        if (!_readyToolHolding[tool])
                            continue;

                        SetToolVacuum(tool, false);
                        _readyToolHolding[tool] = false;
                        releasedTools.Add($"Tool{tool}");
                    }

                    Thread.Sleep(200);
                    RecordReleasedProducts(releasedTools.Count);
                    AddRobotHistory($"[READY] Thả đồng thời sản phẩm của {string.Join(", ", releasedTools)} tại điểm thả.");
                    _dropReturnPoseIndex = 1;
                    _dropToolState = DropToolSubState.MoveReturnPose;
                    return false;

                // Cây con bước 4: Sau khi thả tất cả sản phẩm, robot đi đường về ReturnPose1..ReturnPose5 một lần.
                // Mỗi lần move xong cũng trả quyền để Pause có thể dừng tại từng điểm ReturnPose.
                case DropToolSubState.MoveReturnPose:
                    if (_dropReturnPoseIndex <= 5)
                    {
                        string poseName = $"ReturnPose{_dropReturnPoseIndex}";
                        if (!MoveNamedPose(poseName))
                        {
                            FailReadyCycle($"[READY] Robot không di chuyển được tới {poseName}. Dừng máy, cần Reset lỗi.");
                            _dropToolState = DropToolSubState.Complete;
                            return true;
                        }

                        AddRobotHistory($"[READY] Robot đi qua {poseName} sau khi thả tất cả sản phẩm.");
                        _dropReturnPoseIndex++;
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

                    case ReadySubState.MoveHome:
                        _readyState = ReadySubState.CheckCNC0;
                    
                        break;
                    case ReadySubState.CheckCNC0:
                        if(triggerRun == true && xpixel.Length>0)
                        {
                            string cameraName = _activeTriggerCamera;
                            string toolName = _activeCalibTool;
                            var affine = GetCameraAffine(cameraName: cameraName, toolName: toolName);
                            if (affine == null)
                            {
                                AddMachineLog($"[READY] Chưa load calibration cho {_data.GetCalibName(toolName, cameraName)}.");
                                triggerRun = false;
                                break;
                            }
                            //X, Y là tọa độ robot gắp
                            var (X, Y) = affine.PixelToRobot(xpixel[ivan], ypixel[ivan]);

                            AddRobotHistory($"[READY] Check CNC -> Pixel: ({xpixel[ivan]}, {ypixel[ivan]}) -> Robot: ({X}, {Y})");
                            ivan++;
                            if(ivan >= xpixel.Length)
                            {
                                ivan = 0;
                                triggerRun = false;
                            }
                        }
                        else
                        {

                        }
                 
                        break;
                    case ReadySubState.CompleteHome:
                    

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
                      string err=  _robot.SetOverride(0, 0.03);
                        if (err != "OK")
                        {
                            FailReadyCycle($"[READY] Không Lưu đươc tốc độ xuống robot . Dừng máy, Error: {err}");
                            break;
                        }
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
                    // Quá 5 giây chưa có callback thì chụp lại. Sau đủ số lần cấu hình,
                    // bỏ qua Basket hiện tại và chuyển sang Basket tiếp theo.
                    // Nếu không có sản phẩm thì chuyển sang bước chụp xác nhận Basket rỗng.
                    case ReadySubState.WaitBasketCamera:
                        if (!_readyCameraResultReady)
                        {
                            if (_readyCameraTriggeredAtUtc != DateTime.MinValue &&
                                DateTime.UtcNow - _readyCameraTriggeredAtUtc >= ReadyCameraTimeout)
                            {
                                _readyCameraPending = false;
                                _readyCameraTriggeredAtUtc = DateTime.MinValue;
                                _readyCameraTimeoutCount++;
                                int maxTimeouts = Math.Max(1, _data.EmptyConfirmShots);

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
                                    AddMachineLog($"[READY] Basket{_readyCurrentBasket} không trả kết quả sau 5 giây ({_readyCameraTimeoutCount}/{maxTimeouts}), chụp lại.");
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

                        AddMachineLog($"[READY] Basket{_readyCurrentBasket} có {_readyCameraResultCount} sản phẩm.");
                        // Basket hiện tại còn sản phẩm nên không thể dùng lần xác nhận rỗng
                        // của Basket trước để kết luận cả hai Basket đã hết.
                        _readyLastEmptyBasket = 0;
                        _readyProductIndex = 0;
                        // Từ thời điểm này robot đã bắt đầu chu trình gắp. Nếu người vận hành
                        // nhấn Stop thì phải hoàn tất gắp và thả trước khi về Home.
                        _productLoaded = true;
                        ResetPickToolSubTree();
                        _readyState = ReadySubState.PickByTools;
                        break;

                    // Bước 6: Chụp lại 2-3 lần để xác nhận Basket thật sự hết sản phẩm.
                    // Nếu vẫn không thấy sản phẩm thì kết luận Basket hiện tại đã hết.
                    case ReadySubState.ConfirmBasketEmpty:
                        _readyEmptyConfirmCount++;
                        AddMachineLog($"[READY] Basket{_readyCurrentBasket} không thấy sản phẩm, xác nhận {_readyEmptyConfirmCount}/{_data.EmptyConfirmShots}.");
                        if (_readyEmptyConfirmCount < _data.EmptyConfirmShots)
                        {
                            _readyState = ReadySubState.MoveClearCamera;
                            break;
                        }

                        AddMachineLog($"[READY] Basket{_readyCurrentBasket} đã hết sản phẩm sau {_readyEmptyConfirmCount} lần xác nhận.");

                        if (IsBothBasketMode())
                        {
                            // Chỉ dừng khi Basket còn lại cũng vừa được xác nhận rỗng mà
                            // giữa hai lần xác nhận không hề phát hiện thêm sản phẩm.
                            if (_readyLastEmptyBasket != 0 &&
                                _readyLastEmptyBasket != _readyCurrentBasket)
                            {
                                AddMachineLog($"[READY] Basket1 và Basket2 đều rỗng liên tiếp. Kết thúc chương trình.");
                                _readyState = ReadySubState.FinishAllBaskets;
                                break;
                            }

                            _readyLastEmptyBasket = _readyCurrentBasket;
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

                        if (!HandlePickToolSubTree())
                            break;

                        if (_state == AppState.Error)
                            break;

                        _readyState = ReadySubState.LiftSafeAfterPick;
                        break;

                    // Bước 8: Sau khi chạy hết các Tool trong chu kỳ, robot nâng lên độ cao an toàn H.
                    case ReadySubState.LiftSafeAfterPick:
                        if (!MoveSafeZ())
                        {
                            FailReadyCycle("[READY] Robot không nâng được lên độ cao an toàn H. Dừng máy, cần Reset lỗi.");
                            break;
                        }
                        _readyState = ReadySubState.CheckHoldingProducts;
                        break;

                    // Bước 9: Kiểm tra cảm biến của cả 3 Tool.
                    // Nếu không Tool nào giữ sản phẩm thì về Home và dừng chương trình.
                    case ReadySubState.CheckHoldingProducts:
                        if (!HasAnyHoldingTool())
                        {
                            StopBecauseNoProductPicked();
                            break;
                        }
                        ResetDropToolSubTree();
                        _readyState = ReadySubState.DropPickedProducts;
                        break;

                    // Bước 10: Đi thả những sản phẩm đã hút được.
                    // Robot đi ForwardPose1..ForwardPose5 một lần, thả đồng thời tất cả Tool đang giữ sản phẩm,
                    // rồi đi ReturnPose1..ReturnPose5 một lần để người vận hành có thể Pause giữa các vị trí.
                    case ReadySubState.DropPickedProducts:
                        if (!HandleDropToolSubTree())
                            break;

                        if (_state == AppState.Error)
                            break;

                        _productLoaded = false;

                        if (_stopAfterCycle)
                        {
                            // Đã hoàn tất toàn bộ đường gắp/thả và quay về. Bỏ kết quả camera
                            // chụp trước trong lúc thả, sau đó về Home và dừng hoàn toàn.
                            _readyCameraPending = false;
                            _readyCameraResultReady = false;
                            _readyCameraTriggeredAtUtc = DateTime.MinValue;
                            AddMachineLog("[STATE] Đã chạy hết chu trình gắp/thả sau yêu cầu Stop -> về Home và dừng.");
                            _readyState = ReadySubState.FinishAllBaskets;
                            break;
                        }

                        // Camera đã được trigger tại ForwardPose1. Đến đây robot đã hoàn tất đường
                        // quay về nên mới đọc kết quả; nếu callback chưa tới thì WaitBasketCamera
                        // tiếp tục chờ trong giới hạn timeout hiện có.
                        _readyState = ReadySubState.WaitBasketCamera;
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
                        _readyState = ReadySubState.CheckStatus;
                        break;
                            
                }
            }
            catch (Exception ex)
            {
                RaiseError("Exception trong HandleReady: " + ex.Message);
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
                    AddMachineLog("[MANUAL] Đang gửi lệnh Enable Robot (GrpPowerOn)...");

                    int res = _robot.GrpPowerOn(0); //

                    // 0 = Đã nhận lệnh. 20018 = Đã bật sẵn.
                    if (res == 0 || res == 20018)
                    {
                        AddMachineLog("[MANUAL] Lệnh đã gửi. Đang chờ Servo vật lý đóng phanh (2s)...");
                        Thread.Sleep(2000); // Thời gian bắt buộc để động cơ nạp dòng

                        // XÁC MINH TRẠNG THÁI THỰC TẾ
                        int[] rbtState;
                        string errType = _robot.ReadRobotState(0, out rbtState); //

                        // rbtState[1] là PowerState. Nếu = 1 tức là Servo đã thực sự ON
                        if (errType == "OK" && rbtState[1] == 1)
                        {
                            AddMachineLog("[MANUAL] Enable thành công.");
                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                _data.EnableOn = true;
                                _data.DisableOn = false;
                            });
                        }
                        else
                        {
                            // Robot từ chối bật do có lỗi ngầm
                            AddMachineLog("[MANUAL] Hệ thống đang khởi động");
                        }
                    }
                    else AddMachineLog($"[MANUAL] Enable thất bại (TCP Error): {res}");
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

                // 3. Xử lý OPEN (Quy trình chuẩn: Electrify -> StartMaster -> Enable Servo)
                if (_data.OpenReq)
                {
                    _data.OpenReq = false;
                    AddMachineLog("[MANUAL] Đang thực hiện quy trình OPEN ...");

                    bool electrifyOk = false;

                    // --- BƯỚC 1: CẤP ĐIỆN (ELECTRIFY) ---
                    int res1 = _robot.Electrify();

                    if (res1 == 0)
                    {
                        AddMachineLog("[MANUAL] Electrify OK. Đang chờ nạp tụ (5s)...");
                        electrifyOk = true;
                        Thread.Sleep(5000); // Giữ nguyên 5s như code của bạn để nạp tụ an toàn
                    }
                    else if (res1 == 20018)
                    {
                        AddMachineLog("[MANUAL] Robot đã có điện sẵn (20018). Chờ 1s...");
                        electrifyOk = true;
                        Thread.Sleep(1000); // Code của bạn chờ 1s khi đã có điện
                    }
                    else
                    {
                        AddMachineLog($"[MANUAL] Electrify thất bại: {res1}");
                    }

                    if (electrifyOk)
                    {
                        // --- BƯỚC 2: START MASTER (Thử tối đa 2 lần như code của bạn) ---
                        bool masterOk = false;
                        for (int attempt = 1; attempt <= 2; attempt++)
                        {
                            int res2 = _robot.StartMaster(0);

                            if (res2 == 0 || res2 == 20016)
                            {
                                if (res2 == 20016)
                                    AddMachineLog("[MANUAL] Master đã chạy sẵn (20016).");
                                else
                                    AddMachineLog("[MANUAL] StartMaster OK.");

                                masterOk = true;
                                break;
                            }
                            else
                            {
                                AddMachineLog($"[MANUAL] StartMaster lần {attempt} thất bại: {res2}");
                                if (attempt < 2)
                                    Thread.Sleep(1000); // Chờ 1s trước khi thử lại
                            }
                        }

                        if (masterOk)
                        {                       

                            AddMachineLog("[MANUAL] Quy trình OPEN hoàn tất. Chờ lệnh ENABLE Servo.");
                            Thread.Sleep(4000);
                            // Chỉ cập nhật trạng thái UI để nút chuyển sang chữ "ENABLE"
                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                _data.OpenOn = true;
                                _data.CloseOn = false;

                                // QUAN TRỌNG: Đặt EnableOn = false để nút đa năng hiểu là Servo chưa bật
                                _data.EnableOn = false;
                                _data.DisableOn = true;
                            });
                        }
                        else
                        {
                            AddMachineLog("[MANUAL] StartMaster thất bại sau 2 lần thử.");
                        }
                    }
                }

                // 4. Xử lý CLOSE (Quy trình tắt an toàn 3 bước)
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

                    // BƯỚC 2: Tắt Servo (Disable) nếu đang mở
                    if (_data.EnableOn)
                    {
                        AddMachineLog("[MANUAL] Đang ngắt Servo (Power Off)...");
                        _robot.GrpPowerOff(0);
                        Thread.Sleep(500); // Chờ ngắt dòng điện động cơ
                    }

                    // BƯỚC 3: Đóng Master
                    int res = _robot.CloseMaster();

                    if (res >= 0 || res == 20018)
                    {
                        AddMachineLog("[MANUAL] CloseMaster thành công. Hệ thống đã nghỉ.");
                    }
                    else
                    {
                        AddMachineLog($"[MANUAL] CloseMaster trả về mã: {res}");
                    }

                    // BƯỚC 4: Dọn dẹp giao diện (Đảm bảo dập tắt mọi đèn báo dù có lỗi kết nối)
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        _data.OpenOn = false;
                        _data.CloseOn = false;
                        _data.EnableOn = false;
                        _data.DisableOn = false;
                        _data.FreeDriveOn = false; // Triệt để tắt đèn Free Drive
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
            // 1) Nếu bấm Manual Step 1
            switch (_manualState)
            {

                case ManualSubState.MoveRobot:
                    // TODO: logic manual (Jog, move,...)
                    _manualState = ManualSubState.CheckSensor;
                    break;

                case ManualSubState.CheckSensor:
                    ReadSensorAndUpdateUI();   // đọc input (CI, DI)
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

        // === SETTINGS ===
        private void HandleSettings()
        {
            // ❌ Không cho chỉnh settings nếu không Idle
            if (_state != AppState.Idle)
            {
                // Clear tất cả request để không bị “dồn lệnh” sang lúc Idle
                _data.FUpdatePose = false;
                _data.RequestEditPose = false;
                _data.RequestMovePose = false;
                _data.MovePoseName = null;
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
                        _activeCalibTool = _data.SelectedCalibTool;

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
                                Application.Current?.Dispatcher.Invoke(() =>
                                {
                                    MessageBox.Show($"Robot từ chối di chuyển!\nMã lỗi trả về: {moveErr}", "Lỗi Lệnh Move", MessageBoxButton.OK, MessageBoxImage.Error);
                                });
                            }
                            else
                            {
                                AddMachineLog($"[SETTING] Di chuyển thành công tới {poseName}");
                            }
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
                        }
                        else
                        {
                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                MessageBox.Show("Lỗi đọc vị trí từ Robot: " + array[0], "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                            });
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

                // TODO: Gửi lệnh reset lỗi robot
                // bool resetOk = _robot.ResetError();
                bool resetOk = true; // demo

                if (!resetOk)
                {
                    AddMachineLog("[ERROR] Reset robot thất bại.");
                    return; // vẫn ở Error
                }

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
        }

        // === OUTPUT REQUESTS ===
        private void HandleOutputRequests()
        {
            try
            {
                // ===== GHI DO0..DO7: PushAir1,2,3 / SubPush / Cylinder1,2,3 / GreenLamp =====
                _robot.SetSerialDO(0, _data.PushAir1 ? 1 : 0);
                _robot.SetSerialDO(1, _data.PushAir2 ? 1 : 0);
                _robot.SetSerialDO(2, _data.PushAir3 ? 1 : 0);
                _robot.SetSerialDO(3, _data.SubPush ? 1 : 0);
                _robot.SetSerialDO(4, _data.Cylinder1 ? 1 : 0);
                _robot.SetSerialDO(5, _data.Cylinder2 ? 1 : 0);
                _robot.SetSerialDO(6, _data.Cylinder3 ? 1 : 0);
                _robot.SetSerialDO(7, _data.GreenLampOn ? 1 : 0);   // DO7 = GreenLamp

                // ===== GHI CO0..CO7: Vacuum1,2,3 / RedLamp / YellowLamp / Enable / Disable / Open+Close =====
                _robot.SetBoxCO(0, _data.Vacuum1 ? 1 : 0);
                _robot.SetBoxCO(1, _data.Vacuum2 ? 1 : 0);
                _robot.SetBoxCO(2, _data.Vacuum3 ? 1 : 0);
                _robot.SetBoxCO(3, _data.RedLampOn ? 1 : 0);        // CO3 = RedLamp
                _robot.SetBoxCO(4, _data.YellowLampOn ? 1 : 0);     // CO4 = YellowLamp
              //  _robot.SetBoxCO(5, _data.EnableOn ? 1 : 0);         // CO5 = Enable
              //  _robot.SetBoxCO(6, _data.DisableOn ? 1 : 0);        // CO6 = Disable
               // _robot.SetBoxCO(7, _data.OpenOn ? 1 : 0);           // CO7 = Open(1)/Close(0)

                // ===== ĐỌC LẠI DO TỪ ROBOT → CẬP NHẬT UI =====
                int[] doi = new int[8];
                string kp = _robot.ReadBoxDO_01234567(out doi);
                if (kp == "OK")
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        _data.PushAir1 = doi[0] == 1;
                        _data.PushAir2 = doi[1] == 1;
                        _data.PushAir3 = doi[2] == 1;
                        _data.SubPush = doi[3] == 1;
                        _data.Cylinder1 = doi[4] == 1;
                        _data.Cylinder2 = doi[5] == 1;
                        _data.Cylinder3 = doi[6] == 1;
                        _data.GreenLampOn = doi[7] == 1;  // DO7
                    });
                }
                else
                {
                    AddMachineLog($"[MANUAL] Error Read DO: {kp}");
                }

                // ===== ĐỌC LẠI CO TỪ ROBOT → CẬP NHẬT UI =====
                int[] coi = new int[8];
                kp = _robot.ReadBoxCO_01234567(out coi);
                if (kp == "OK")
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        _data.Vacuum1 = coi[0] == 1;
                        _data.Vacuum2 = coi[1] == 1;
                        _data.Vacuum3 = coi[2] == 1;
                        _data.RedLampOn = coi[3] == 1;     // CO3
                        _data.YellowLampOn = coi[4] == 1;  // CO4
                        // EnableOn/DisableOn/OpenOn/CloseOn được quản lý bởi HandleControlRequests()
                        // Không readback từ CO để tránh ghi đè trạng thái
                        // _data.EnableOn = coi[5] == 1;   // CO5
                        // _data.DisableOn = coi[6] == 1;  // CO6
                      //  _data.OpenOn = coi[7] == 1;        // CO7 = Open
                     //  _data.CloseOn = coi[7] == 0;       // CO7 = Close (ngược lại Open)
                    });
                }
                else
                {
                    AddMachineLog($"[MANUAL] Error Read CO: {kp}");
                }
            }
            catch (Exception ex)
            {
                AddMachineLog($"[OUTPUT][READBACK][ERROR] {ex.Message}");
            }
        }

        private void ReadSensorAndUpdateUI()
        {
            // ===== ĐỌC CI0..CI7 =====
            int[] ci = new int[8];
            string kq = _robot.ReadBoxCI_01234567(out ci);
            if (kq == "OK")
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _data.Xl1Down  = ci[0] == 1;  // CI0
                    _data.Xl1Up    = ci[1] == 1;  // CI1
                    _data.Xl2Down  = ci[2] == 1;  // CI2
                    _data.Xl2Up    = ci[3] == 1;  // CI3
                    _data.Xl3Down  = ci[4] == 1;  // CI4
                    _data.Xl3Up    = ci[5] == 1;  // CI5
                    _data.SsSc1    = ci[6] == 1;  // CI6
                    _data.SsSc2    = ci[7] == 1;  // CI7
                });
            }
            else
            {
                AddMachineLog($"[ERROR] Read CI robot {kq}");
            }

            // ===== ĐỌC DI0..DI7 =====
            int[] di = new int[8];
            kq = _robot.ReadBoxDI_01234567(out di);
            if (kq == "OK")
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _data.SsSc3      = di[0] == 1;  // DI0
                    _data.FrontDoor  = di[1] == 1;  // DI1
                    _data.BackDoor   = di[2] == 1;  // DI2
                    _data.Buzzer     = di[3] == 1;  // DI3
                    _data.LampRed    = di[4] == 1;  // DI4
                    _data.LampYellow = di[5] == 1;  // DI5
                    _data.LampGreen  = di[6] == 1;  // DI6
                    _data.Basket1    = di[7] == 1;  // DI7
                });
            }
            else
            {
                AddMachineLog($"[ERROR] Read DI robot {kq}");
            }

            // TODO: Basket2, MayPolishing, MaySeatFinishin, Stop, Reset, Start, AirP
            // cần thêm kênh IO (ví dụ ReadSerialDI hoặc mở rộng CI/DI) để mapping
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
                AddRobotHistory($"[MANUAL] Jog {axis} {direction}: Thành công (Step: {stepValue})");
            }
            else
            {
                AddRobotHistory($"[MANUAL] Jog {axis} {direction}: Thất bại - {er}");
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
            string selectedTool = _data.SelectedCalibTool;
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
