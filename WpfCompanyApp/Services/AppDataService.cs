using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using WpfCompanyApp.Models;
using WpfCompanyApp.ViewModels;

namespace WpfCompanyApp.Services
{
    public partial class AppDataService : ViewModelBase
    {
        // ====== STATE HIỆN TẠI CỦA APP (để đổi màu nút Start/Stop/Pause) ======
        private AppState _currentState;
        public AppState CurrentState
        {
            get => _currentState;
            set => SetProperty(ref _currentState, value);
        }
        public AppDataService()
        {
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
        [ObservableProperty] private double cycleCount;
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
    }
}
