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
        CheckCNCdi2,
        OpenGripper1,
        MoveWait,
        CompleteMoveWait,
        CheckPhoi,
        Move1H,
        OpenGripper2,
        CompleteMove1H,
        MoveSP,
        CompleteSP,
        CheckGripperClose1,
        Move1HR,
        CompleteMove1HR,

        Move1,
        CompleteMove1,
        OpenClockCNC,
        WaitClockCNC,
        OpenDoorCNC,
        WaitOpenDoorCNC,
        Move2,
        CompleteMove2,
        Move3,
        CompleteMove3,
        Move4,
        CompleteMove4,
        Move5,
        CompleteMove5,
        CheckGripperOpen1,
        Move6,
        CompleteMove6,
        Move7,
        CompleteMove7,
        Move8,
        CompleteMove8,
        Move9,
        CompleteMove9,
        Move10,
        CompleteMove10,
        MoveHomev,
        CompleteHomev,
        CloseDoor1,
        CheckIO1,
        CheckIO2,
        CheckIO3,
        CheckIO4,
        CheckIO5,
        CheckIO6,
        LockCNCthayphoi,
        homeManual,
        CheckIO7,
        CheckIO8,
        CheckIO9,
        CheckIO10,
        CompleteIO,
        MoveGo1,
        CompleteGo1,
        MoveGo2,
        CompleteGo2,
        MoveGo3,
        CompleteGo3,
        CheckGripperClose2,
        OpenClaw1,
        CompleteOpenClaw1,
        MoveRe1,
        CompleteRe1,
        MoveRe2,
        CompleteRe2,
        MoveRe3,//ve home
        CompleteRe3, //kiem tra ra tha o nao
        MoveWaitTha,
        CompleteWaitTha,
        MovethaTH,
        CompletethaTH,
        MovethaT,
        CompletethaT,
        CheckGripperOpen2,
        MovethaTHV,
        CompletethaTHV,
        MovethaT_PH,
        CompletethaT_PH,
        MovethaT_P,
        CompletethaT_P,
        CheckGripperOpen3,
        MovethaT_PV,
        CompletethaT_PV,
        MoveWaitThaV,
        CompleteWaitThaV,
        MoveEnd,
        Complete,
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

    public partial class AppBackgroundService
    {
        private readonly AppDataService _data;
        private readonly CancellationTokenSource _cts = new();
        private readonly DatabaseRobot _db = new();
        private readonly INIFile _ini;
        private readonly FileLogger _logger;
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
        private string _ipRobot = "192.168.0.10";
        private int _portRobot = 10003;
        private int _readTimeout = 500;
        bool _manualStep1CycleActive = false;
        bool _manualStep2CycleActive = false;
        bool _manualStep3CycleActive = false;
        // Robot đã kết nối chưa
        private bool _isRobotConnected = false;

        // ✅ đã kẹp sản phẩm sau bước CompleteSP hay chưa
        private bool _productLoaded = false;

        // ✅ có yêu cầu dừng sau khi chạy hết chu trình hiện tại không
        private bool _stopAfterCycle = false;

        // ✅ cờ lỗi chung
        private bool _hasError = false;
        private string _lastError = "";

        private readonly string _logFolder;
   
        bool IsPointInPolygonXY(PosMoveL[] poly, PosMoveL p)
        {
            int n = poly.Length;
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
        bool IsRobotInsideHexPrism(PosMoveL[] bottom, PosMoveL robotPos, double heightOffset)
        {
            if (bottom.Length != 6)
                throw new Exception("Cần đúng 6 điểm đáy (Bottom) để tạo khối!");

            // ---- 1. Lấy Z_MIN từ mặt đáy ----
            double zMin = bottom.Min(p => p.Z)-100;
            double zMax = zMin + heightOffset;

            // ---- 2. Kiểm tra theo Z (trục đứng) ----
            if (robotPos.Z < zMin || robotPos.Z > zMax)
                return false;

            // ---- 3. Kiểm tra theo mặt XY (đa giác 6 cạnh) ----
            return IsPointInPolygonXY(bottom, robotPos);
        }
        partial void ManualRobot(PosMoveL currentPos);
        public AppBackgroundService(AppDataService data, INIFile ini, FileLogger logger)
        {
            VmSolution.OnWorkStatusEvent += VmSolution_OnWorkStatusEvent;
            _data = data;
            _ini = ini;
            _logger = logger;

            _robot = new ConmandRobot();

            _logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(_logFolder);
        }
        public ProcessInfoList vmProcessInfoList = new ProcessInfoList();
        VmProcedure vmProcedure;
        float[] xpixel;
        float[] ypixel;
        bool triggerRun = false;
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
                        {
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
                                    var pro = VmSolution.Instance["Flow1"] as VmProcedure;
                                    if (pro != null)
                                    {

                                        try
                                        {
                                            //cycletime 	string vmResult = vmProcedure.ModuResult.GetOutputString("time").astStringVal[0].strValue;
                                            string vmResult = vmProcedure.ModuResult.GetOutputString("ketqua").astStringVal[0].strValue;
                                             xpixel = vmProcedure.ModuResult.GetOutputFloat("outX").pFloatVal;
                                            ypixel = vmProcedure.ModuResult.GetOutputFloat("outY").pFloatVal;
                                            if(_state == AppState.Running)
                                            {
                                                triggerRun = true;
                                            }
                                            else
                                            {
                                                triggerRun = false;
                                            }
                                                HandleTriggerCamera(xpixel.Length);
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

            _state = AppState.Error;
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
                    try
                    {
                        //   if(nameSolution !=  nameSolutionClear) 
                        {
                            Task task = Task.Run(() =>
                            {
                                if (VmSolution.Instance.SolutionPath != null)
                                {
                                    VmSolution.Save();
                                    VmSolution.Instance.CloseSolution();
                                }
                            });
                            task.Wait();  // Chờ task hoàn thành trước khi tiếp tục
                        }
                    }
                    catch
                    {

                    }
                    vmSolutionInfo.vmSolutionPath = path111;
                    VmSolution.Load(vmSolutionInfo.vmSolutionPath, "196370");
                    vmProcessInfoList = VmSolution.Instance.GetAllProcedureList();//Obtain all processes in the solution
                    vmProcedure = VmSolution.Instance[vmProcessInfoList.astProcessInfo[0].strProcessName] as VmProcedure;
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
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

                var portStr = _ini.Read("Port", "PLCTCP");
                if (int.TryParse(portStr, out int port))
                    _portRobot = port;

                var timeoutStr = _ini.Read("TimeOut", "PLCTCP");
                if (int.TryParse(timeoutStr, out int timeout))
                    _readTimeout = timeout;

                for (int i = 0; i < 6; i++)
                {
                    bottom[i] = new PosMoveL();
                    bottom[i].X = _data.RobotTrajectories[i + 12].X;
                    bottom[i].Y = _data.RobotTrajectories[i + 12].Y;
                    bottom[i].Z = _data.RobotTrajectories[i + 12].Z;
                }

                    _data.HomeData = $"Đã load config: IP={_ipRobot}, Port={_portRobot}, TO={_readTimeout}";
                AddMachineLog($"[INIT] Load config: IP={_ipRobot}, Port={_portRobot}, TO={_readTimeout}");
                
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
                AddMachineLog($"[CONNECT] Try connect robot: IP={_ipRobot}, Port={_portRobot}, TO={_readTimeout}");

                bool ok = _robot.tcpConnect(_ipRobot, _portRobot, _readTimeout);
                //  bool ok = true;
                if (ok)
                {
                    _isRobotConnected = true;
                    AddMachineLog("[CONNECT] Robot connected OK");
                    AddRobotHistory("[CONNECT] Connected OK");

                    _state = AppState.Idle;
                    _readyState = ReadySubState.CheckStatus;
                }
                else
                {
                    _isRobotConnected = false;
                    AddMachineLog("[CONNECT][ERROR] Cannot connect to robot, will retry...");
                    Thread.Sleep(500);
                }
            }
            catch (Exception ex)
            {
                _isRobotConnected = false;
                AddMachineLog($"[CONNECT][EXCEPTION] {ex.Message}");
                RaiseError("Lỗi kết nối robot: " + ex.Message);
                Thread.Sleep(500);
            }
        }

        // IDLE: chờ Start / Home => coi như trạng thái STOP
        private void HandleIdle()
        {
            if (_data.StartRequested)
            {
                _data.StartRequested = false;
                AddMachineLog("[STATE] Start requested -> RUNNING");
           
                _readyState = ReadySubState.CheckStatus;
                _state = AppState.Running;

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
                    return;
                }

            }

            if (_data.PauseRequested)
            {
                _data.PauseRequested = false;
                AddMachineLog("[STATE] Pause requested -> PAUSED");
                // TODO: gửi lệnh tạm dừng robot
                // _robot.Pause();
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
                }
                return;
            }

            if (_data.StartRequested)
            {
                _data.StartRequested = false;
                AddMachineLog("[STATE] Resume from paused -> RUNNING");

                // TODO: gửi lệnh Resume cho robot
                // _robot.Resume();

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
                        // chiều cao khối 3D
                        double heightOffset = 500;
                        PosMoveL movel2 = new PosMoveL();
                        string er = _robot.ReadActualPosMoveL(0, out movel2);
                        if (er == "OK")
                        {
                            if (IsRobotInsideHexPrism(bottom, moveLHome, heightOffset))
                            {
                                AddMachineLog("Robot nằm TRONG vùng 3D → ĐƯỢC phép Move Home");
                                // gửi lệnh Move Home
                                er = _robot.SetOverride(0, 0.05);
                                if (er == "OK")
                                {
                                    er = _robot.SetUCSByName(0, tablesp);
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
                                            _robot.SetOverride(0, 0.05);
                                            er = _robot.SetUCSByName(0, tablesp);
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
                                AddMachineLog("Robot KHÔNG nằm trong vùng 3D → KHÔNG Move Home Hay di chuyển robot vào vùng an toan");
                             
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
        PosMoveL[] bottom = new PosMoveL[6];
        TriggerPosItem[] listRobot;
        bool IsAlmostEqual(PosMoveL p1, PosMoveL p2, double tolerance)
        {
            bool sameX = Math.Abs(p1.X - p2.X) <= tolerance;
            bool sameY = Math.Abs(p1.Y - p2.Y) <= tolerance;
            bool sameZ = Math.Abs(p1.Z - p2.Z) <= tolerance;

            return sameX && sameY && sameZ;
        }
        int ivan = 0;
        private void HandleReady()
         {
            try
            {
                switch (_readyState)
                {
                    case ReadySubState.CheckStatus:

                        _readyState = ReadySubState.MoveHome;
                        AddRobotHistory($"[READY]   CheckStatus ");
                        icounter = 0;

                        break;

                    case ReadySubState.MoveHome:
                        _readyState = ReadySubState.CheckCNC0;
                        // TODO: gọi lệnh MoveHome nội bộ chu trình nếu cần
                        // if (!_robot.MoveHome()) { RaiseError(...); return; }
                        /*  {
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
                                   // chiều cao khối 3D
                                   double heightOffset = 500;
                                   PosMoveL movel2 = new PosMoveL();
                                   string er = _robot.ReadActualPosMoveL(0, out movel2);
                                   if (er == "OK") {
                                       if (IsRobotInsideHexPrism(bottom, moveLHome, heightOffset))
                                       {
                                           AddMachineLog("Robot nằm TRONG vùng 3D → ĐƯỢC phép Move Home");
                                           // gửi lệnh Move Home
                                           er = _robot.SetOverride(0, 0.05);
                                           if (er == "OK")
                                           {
                                               er = _robot.SetUCSByName(0, tablesp);
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
                                                       _robot.SetOverride(0, 0.05);
                                                       er = _robot.SetUCSByName(0, tablesp);
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
                                                                       _data.StopRequested = true;
                                                                       break;
                                                                       //  _data.StopRequested = true;
                                                                   }
                                                               }


                                                       }
                                                       else
                                                       {
                                                           AddMachineLog($"[READY  {istep + 1}]  Error SetUCSByName ");
                                                           Thread.Sleep(500);
                                                           break;
                                                       }

                                                   }
                                                   ///////////////

                                                   er = _robot.MoveL(0, moveLHome, 0);
                                                   if (er == "OK")
                                                   {
                                                       AddRobotHistory($"[READY] Move to  Home -> X: {pose.X}, Y: {pose.Y}, Z: {pose.Z}, RX: {pose.Rx}, RY: {pose.Ry}, RZ: {pose.Rz} ");

                                                       _readyState = ReadySubState.CheckCNC0;
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
                                           AddMachineLog("Robot KHÔNG nằm trong vùng 3D → KHÔNG Move Home Hay di chuyển robot vào vùng an toan");
                                           break;
                                       }
                                   }
                                   else
                                   {
                                       AddMachineLog("Error đọc vị trí robot");
                                       break;
                                   }


                               }
                               else
                               {
                                   // Không tìm thấy NamePoses tương ứng
                                   AddMachineLog($"[READY] Không tìm thấy  Home ");
                                   _data.StopRequested = true;
                               }
                           } */
                        break;
                    case ReadySubState.CheckCNC0:
                        if(triggerRun == true && xpixel.Length>0)
                        {
                            var (X, Y) = _data._affine1.PixelToRobot(xpixel[ivan], ypixel[ivan]);

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
                     /*   {
                            int[] ci = new int[8];
                            string kq = _robot.ReadBoxCI_01234567(out ci);
                            if(kq == "OK")
                            {
                                if(ci[6] == 1)
                                {
                                    if (ci[7] == 1)
                                    {
                                        AddMachineLog($"[READY ] Máy CNC đang RUN, Khí đủ");
                                    _readyState =    ReadySubState.CompleteHome;
                                    }
                                    else
                                    {
                                        AddMachineLog($"[READY ] Error Máy CNC đang STOP");
                                    }
                                }
                                else
                                {
                                    AddMachineLog($"[READY ] Error Khí Chưa đủ");

                                }
                            }
                            else
                            {
                                AddMachineLog($"[READY ] Error ReadBoxCI robot");
                                
                            }
                        } */
                        break;
                    case ReadySubState.CompleteHome:
                      /*  {
                            _robot.SetSerialDO(6, 1);
                            _robot.SetSerialDO(2, 0);
                            _robot.SetSerialDO(3, 0);
                            string kq = _robot.SetBoxCO(1, 1);
                            if (kq == "OK")
                            {
                                kq = _robot.SetBoxCO(0, 0);
                                if (kq == "OK")
                                {
                                    kq = _robot.SetBoxCO(4, 1);
                                    if (kq == "OK")
                                    {
                                        _readyState = ReadySubState.CheckCNC;
                                    }
                                    else
                                    {
                                        AddMachineLog($"[READY] Error Set CO0 {kq}");
                                        Thread.Sleep(500);
                                    }

                                }
                                else
                                {
                                    AddMachineLog($"[READY] Error Set CO0 {kq}");
                                    Thread.Sleep(500);
                                }
                            }
                            else
                            {
                                AddMachineLog($"[READY] Error Set CO0 {kq}");
                                Thread.Sleep(500);
                            }
                        } */

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
                        var pro = VmSolution.Instance["Flow1"] as VmProcedure;
                        if (pro != null)
                        {
                            pro.Run();
                        }
                        else
                        {
                            AddMachineLog("[SETTING] Lỗi: Không tìm thấy Flow1 để chạy Trigger Camera.");
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

                // Tắt đèn đỏ
              

                // Sau khi reset OK -> về Home rồi Idle
                AddMachineLog("[ERROR] Reset OK -> chuyển sang HOMING.");
                _state = AppState.Homing;
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
                    _data.NumTriggerCamera = 0;
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
                            PositionName = $"Position {i}"
                        });
                    }

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
            
            // ✅ Lấy giá trị từ ComboBox SelectedCalibTool
            // Từ SettingsViewModel thông qua _data.SelectedCalibTool (nếu có)
            // Hoặc lấy từ UI của SettingsView (nếu binding được)
            string selectedTool = _data.SelectedCalibTool ?? "Tool1"; // Mặc định "Tool1"
            
            _db.SaveCalibPointsToDb(robotPointCalib, selectedTool);
            _data.RequestSaveAllPositionsTrigger = false;
            
            AddMachineLog($"[CALIB] Đã lưu tất cả {listRobot.Length} điểm calibration vào '{selectedTool}' thành công");
            AutoCloseToast.ShowSuccess($"Saved all {listRobot.Length} calibration points to {selectedTool} ✔", 2000);
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
