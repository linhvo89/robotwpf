using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using WpfCompanyApp.CalibRobot;
using WpfCompanyApp.Data;
using WpfCompanyApp.Models;
using WpfCompanyApp.ViewModels;

namespace WpfCompanyApp.Services
{
    public partial class AppDataService : ViewModelBase
    {
        private readonly DatabaseRobot _db = new();

        // ====== STATE HIỆN TẠI CỦA APP (để đổi màu nút Start/Stop/Pause) ======
        private AppState _currentState;
        public AppState CurrentState
        {
            get => _currentState;
            set => SetProperty(ref _currentState, value);
        }
        public AppDataService()
        {
            LoadRobotSpeeds();

            // Khi bất kỳ phần tử nào trong Slots bị thay đổi,
            // event này sẽ được gọi (Action = Replace)
        }

        // ====== dữ liệu hiển thị UI khác ======
        [ObservableProperty] private string homeData;
        [ObservableProperty] private string manualData;
        [ObservableProperty] private string settingsData;

        [ObservableProperty] private bool manualActive;
        [ObservableProperty] private bool settingsActive;
        [ObservableProperty] private bool homeActive;
        [ObservableProperty] private int ketqua = 0;   // để log kết quả move robot
        [ObservableProperty] private double instantCycleTime;
        [ObservableProperty] private double averageCycleTime;
        [ObservableProperty] private double currentZone;
        [ObservableProperty] private double basket1Count;
        [ObservableProperty] private double basket2Count;
        [ObservableProperty] private double cycleTime;
        [ObservableProperty] private string cycleTimeDisplay = "00:00:00";
        [ObservableProperty] private double cycleCount;
        [ObservableProperty] private string selectedBasketMode = "Both";
        [ObservableProperty] private bool runTool1 = true;
        [ObservableProperty] private bool runTool2 = true;
        [ObservableProperty] private bool runTool3 = true;
        [ObservableProperty] private double retryZ = 5;
        [ObservableProperty] private double safeH = 50;
        [ObservableProperty] private int vacuumWaitMs = 500;
        [ObservableProperty] private int emptyConfirmShots = 3;
        [ObservableProperty] private int maxToolMissCount = 3;
        [ObservableProperty] private double speedCapture = 0.2;
        [ObservableProperty] private double speedSuction = 0.2;
        [ObservableProperty] private double speedMoveToDrop1 = 0.2;
        [ObservableProperty] private double speedMoveBetweenDrops = 0.2;
        [ObservableProperty] private double speedReturnAfterDrop = 0.2;

        private void LoadRobotSpeeds()
        {
            Dictionary<string, double> savedSpeeds = _db.GetRobotSpeedSettings();

            SpeedCapture = GetSavedSpeed(savedSpeeds, nameof(SpeedCapture), SpeedCapture);
            SpeedSuction = GetSavedSpeed(savedSpeeds, nameof(SpeedSuction), SpeedSuction);
            SpeedMoveToDrop1 = GetSavedSpeed(savedSpeeds, nameof(SpeedMoveToDrop1), SpeedMoveToDrop1);
            SpeedMoveBetweenDrops = GetSavedSpeed(savedSpeeds, nameof(SpeedMoveBetweenDrops), SpeedMoveBetweenDrops);
            SpeedReturnAfterDrop = GetSavedSpeed(savedSpeeds, nameof(SpeedReturnAfterDrop), SpeedReturnAfterDrop);
        }

        private static double GetSavedSpeed(
            IReadOnlyDictionary<string, double> savedSpeeds,
            string speedName,
            double defaultValue)
        {
            return savedSpeeds.TryGetValue(speedName, out double value) &&
                   value > 0 &&
                   value <= 1
                ? value
                : defaultValue;
        }

        private void SaveRobotSpeed(string speedName, double value)
        {
            if (value > 0 && value <= 1)
                _db.SaveRobotSpeedSetting(speedName, value);
        }

        partial void OnSpeedCaptureChanged(double value) =>
            SaveRobotSpeed(nameof(SpeedCapture), value);

        partial void OnSpeedSuctionChanged(double value) =>
            SaveRobotSpeed(nameof(SpeedSuction), value);

        partial void OnSpeedMoveToDrop1Changed(double value) =>
            SaveRobotSpeed(nameof(SpeedMoveToDrop1), value);

        partial void OnSpeedMoveBetweenDropsChanged(double value) =>
            SaveRobotSpeed(nameof(SpeedMoveBetweenDrops), value);

        partial void OnSpeedReturnAfterDropChanged(double value) =>
            SaveRobotSpeed(nameof(SpeedReturnAfterDrop), value);
        private object? _moduleSource;
        public object? ModuleSource
        {
            get => _moduleSource;
            set => SetProperty(ref _moduleSource, value);
        }
        public bool HasError { get; set; }
        public string ErrorMessage { get; set; }
        public bool ResetRequested { get; set; }  // nút Reset trên UI
        public bool LoadJob { get; set; }  // nút Reset trên UI
        public string JobName{ get; set; }  // tên job cần load
        public double JobH1 { get; set; }
        public double JobH2 { get; set; }
        public double JobH3 { get; set; }
       
        public bool RequestSavePositionTrigger { get; set; }
        public int IndexTrigger { get; set; }


        // ====== log, pose, trajectory ======
        public ObservableCollection<string> MachineLog { get; } = new();
        public ObservableCollection<string> RobotHistory { get; } = new();

        public ObservableCollection<RobotPose> RobotPoses { get; } = new();
        public ObservableCollection<RobotTrajectory> RobotTrajectories { get; } = new();

        // ====== EDIT POSE ======
        public bool RequestEditPose { get; set; }
        public RobotPose PoseToEdit { get; set; }

        // ====== MOVE POSE ======
        public bool RequestMovePose { get; set; }
        public string MovePoseName { get; set; }
        public RobotTrajectory.MoveTypeEnum MoveTypeToMove { get; set; }

        public int RobotId { get; set; } = 0;

        public bool FUpdatePose { get; set; }
        public string NamePose { get; set; }

        // ====== START / STOP / PAUSE / HOME ======
        public bool StartRequested { get; set; }
        public bool StopRequested { get; set; }
        public bool PauseRequested { get; set; }
        public bool ClearCycleRequested { get; set; }
        public bool HomeRequested { get; set; }
        public bool ShutdownReq { get; set; }
        public bool RestartReq { get; set; }

        // =====================================================================
        //  Setings viewmodel data
        // =====================================================================





        // =====================================================================
        //  Manual ViewModel Data
        // =====================================================================
        [ObservableProperty] private bool pushAir1;
        [ObservableProperty] private bool pushAir2;
        [ObservableProperty] private bool pushAir3;
        [ObservableProperty] private bool subPush;

        [ObservableProperty] private bool cylinder1;
        [ObservableProperty] private bool cylinder2;
        [ObservableProperty] private bool cylinder3;

        [ObservableProperty] private bool vacuum1;
        [ObservableProperty] private bool vacuum2;
        [ObservableProperty] private bool vacuum3;
        [ObservableProperty] private bool greenLampOn;
        [ObservableProperty] private bool redLampOn;
        [ObservableProperty] private bool yellowLampOn;

        // Robot control toggles
        // 1. Các biến cờ (Flags) - Dùng để báo hiệu cho Background Service
        public bool EnableReq { get; set; }
        public bool DisableReq { get; set; }
        public bool OpenReq { get; set; }
        public bool CloseReq { get; set; }

        // 2. Các lệnh RelayCommand - Để Binding vào nút bấm bên XAML
        // Khi nhấn nút, nó sẽ bật cờ lên True
        [RelayCommand] private void RequestEnable() { EnableReq = true; }
        [RelayCommand] private void RequestDisable() { DisableReq = true; }
        [RelayCommand] private void RequestOpen() { OpenReq = true; }
        [RelayCommand] private void RequestClose() { CloseReq = true; }
        [ObservableProperty] private bool enableOn;
        [ObservableProperty] private bool disableOn;
        [ObservableProperty] private bool openOn;
        [ObservableProperty] private bool closeOn;
        [ObservableProperty] private bool robotPoweredOn;

        // Jog settings
        [ObservableProperty] private bool isStepMode;
        // [ObservableProperty] private double stepMM;
        //   [ObservableProperty] private double stepDegree;
        private double _stepMM;
        public double StepMM
        {
            get => _stepMM;
            set
            {
                // Giới hạn từ 0 đến 50 mm
                if (value < 0) value = 0;
                if (value > 50) value = 50;

                SetProperty(ref _stepMM, value);
            }
        }

        private double _stepDegree;
        public double StepDegree
        {
            get => _stepDegree;
            set
            {
                // Giới hạn từ 0 đến 5 độ
                if (value < 0) value = 0;
                if (value > 5) value = 5;

                SetProperty(ref _stepDegree, value);
            }
        }
        // Thêm cờ yêu cầu cho Background Service
        public bool FreeDriveReq { get; set; }

        // Thêm biến trạng thái hiển thị trên giao diện (False = Khóa phanh, True = Đang mở)
        [ObservableProperty] private bool freeDriveOn;

        // Thêm lệnh Command gắn vào nút bấm
        [RelayCommand]
        private void RequestFreeDrive()
        {
            FreeDriveReq = true;
        }
        // 1. Thêm các cờ yêu cầu (Flags)
        public bool ResetRobotReq { get; set; }
        public bool StatusRobotReq { get; set; }

        // 2. Thêm các lệnh Command
        [RelayCommand]
        private void RequestResetRobot() { ResetRobotReq = true; }

        [RelayCommand]
        private void RequestStatusRobot() { StatusRobotReq = true; }
        // Jog commands
        public bool JogXPlusReq { get; set; }
        public bool JogXMinusReq { get; set; }
        public bool JogYPlusReq { get; set; }
        public bool JogYMinusReq { get; set; }
        public bool JogZPlusReq { get; set; }
        public bool JogZMinusReq { get; set; }
        public bool JogRXPlusReq { get; set; }
        public bool JogRXMinusReq { get; set; }
        public bool JogRYPlusReq { get; set; }
        public bool JogRYMinusReq { get; set; }
        public bool JogRZPlusReq { get; set; }
        public bool JogRZMinusReq { get; set; }
        [RelayCommand] private void JogXPlus() { JogXPlusReq = true; }
        [RelayCommand] private void JogXMinus() { JogXMinusReq = true; }
        [RelayCommand] private void JogYPlus() { JogYPlusReq = true; }
        [RelayCommand] private void JogYMinus() { JogYMinusReq = true; }
        [RelayCommand] private void JogZPlus() { JogZPlusReq = true; }
        [RelayCommand] private void JogZMinus() { JogZMinusReq = true; }
        [RelayCommand] private void JogRXPlus() { JogRXPlusReq = true; }
        [RelayCommand] private void JogRXMinus() { JogRXMinusReq = true; }
        [RelayCommand] private void JogRYPlus() { JogRYPlusReq = true; }
        [RelayCommand] private void JogRYMinus() { JogRYMinusReq = true; }
        [RelayCommand] private void JogRZPlus() { JogRZPlusReq = true; }
        [RelayCommand] private void JogRZMinus() { JogRZMinusReq = true; }

        // =====================================================================
        //  Sensor IO Data
        // =====================================================================
        [ObservableProperty] private bool xl1Down;
        [ObservableProperty] private bool xl1Up;
        [ObservableProperty] private bool xl2Down;
        [ObservableProperty] private bool xl2Up;
        [ObservableProperty] private bool xl3Down;
        [ObservableProperty] private bool xl3Up;

        [ObservableProperty] private bool ssSc1;
        [ObservableProperty] private bool ssSc2;
        [ObservableProperty] private bool ssSc3;

        [ObservableProperty] private bool frontDoor;
        [ObservableProperty] private bool backDoor;
        [ObservableProperty] private bool buzzer;

        [ObservableProperty] private bool lampRed;
        [ObservableProperty] private bool lampYellow;
        [ObservableProperty] private bool lampGreen;

        [ObservableProperty] private bool basket1;
        [ObservableProperty] private bool basket2;

        [ObservableProperty] private bool mayPolishing;
        [ObservableProperty] private bool maySeatFinishin;

        [ObservableProperty] private bool stop;
        [ObservableProperty] private bool reset;
        [ObservableProperty] private bool start;
        [ObservableProperty] private bool airP;

        [ObservableProperty] private double currentX;
        [ObservableProperty] private double currentY;
        [ObservableProperty] private double currentZ;
        [ObservableProperty] private double currentRx;
        [ObservableProperty] private double currentRy;
        [ObservableProperty] private double currentRz;

        // =====================================================================
        //  Trigger Camera Data
        // =====================================================================
        [ObservableProperty] private bool requestTriggerCamera = false;
        [ObservableProperty] private string selectedTriggerCamera = "Camera1";
        public ObservableCollection<RobotPositionItem> RobotPositionList { get; } = new();
        [ObservableProperty] private bool showTriggerPositions = false;

        [ObservableProperty]
        private int numTriggerCamera=0;
        [ObservableProperty] private bool requestSaveAllPositionsTrigger;
        
        // ✅ Thêm property để lưu tool được chọn từ ComboBox
        [ObservableProperty]
        private string selectedCalibTool = "Tool1"; // Mặc định "Tool1"
        public ObservableCollection<RobotPointCalib> CalibPointsTool1 { get; } = new();
        public ObservableCollection<RobotPointCalib> CalibPointsTool2 { get; } = new();
        public ObservableCollection<RobotPointCalib> CalibPointsTool3 { get; } = new();
        public ObservableCollection<RobotPointCalib> CalibPointsCamera1 { get; } = new();
        public ObservableCollection<RobotPointCalib> CalibPointsCamera2 { get; } = new();
        public Affine2D? _affine1;
        public Affine2D? _affine2;
        public Affine2D? _affine3;
        public Affine2D? AffineCamera1 { get; set; }
        public Affine2D? AffineCamera2 { get; set; }
        public Dictionary<string, Affine2D?> CalibAffines { get; } = new();

        public string GetCalibName(string? tool = null, string? camera = null)
        {
            string toolName = string.IsNullOrWhiteSpace(tool) ? SelectedCalibTool : tool;
            string cameraName = string.IsNullOrWhiteSpace(camera) ? SelectedTriggerCamera : camera;

            return $"{toolName}_{cameraName}";
        }

        public void SetCalibAffine(string? tool, string? camera, Affine2D? affine)
        {
            CalibAffines[GetCalibName(tool, camera)] = affine;
        }

        public Affine2D? GetCalibAffine(string? tool = null, string? camera = null)
        {
            CalibAffines.TryGetValue(GetCalibName(tool, camera), out var affine);
            return affine;
        }

        public void ResetTriggerSaveStatus()
        {
            IsSaveAllSuccess = false;

            foreach (var item in RobotPositionList)
                item.IsStatus = false;
        }

        [ObservableProperty]
        private bool isSaveAllSuccess;
    }
}
