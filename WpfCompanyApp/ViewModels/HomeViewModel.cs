using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using VM.Core;
using WpfCompanyApp.CalibRobot;
using WpfCompanyApp.Data;
using WpfCompanyApp.Models;
using WpfCompanyApp.Services;
using WpfCompanyApp.Views;

namespace WpfCompanyApp.ViewModels
{
    public partial class HomeViewModel : ViewModelBase
    {
        private readonly DatabaseRobot _db;
        private const string SelectedJobFile = "selected_job.json";

        // ✅ Danh sách Job
        [ObservableProperty]
        private ObservableCollection<JobModelHome> activeJobs = new();

        // ✅ Job đang được chọn
        [ObservableProperty]
        private JobModelHome selectedJob;
        private JobModelHome? _previousJob;

        // ✅ Dữ liệu Modbus
        [ObservableProperty]
        private ObservableCollection<string> modbusData = new(
            Enumerable.Repeat("0", 10).ToList()
        );

        // ✅ Lịch sử dữ liệu Modbus
        [ObservableProperty]
        private ObservableCollection<string> modbusHistory = new();

        // ❌ KHÔNG tạo MachineLog riêng nữa
        // [ObservableProperty]
        // private ObservableCollection<string> machineLog = new();

        private readonly INIFile _ini;
        private readonly AppDataService _data;
        // ======= PROPERTY MAP TỪ AppDataService RA XAML =======
        public AppDataService Data => _data;      // 👈 THÊM DÒNG NÀY


        string ip = "";
     

        private List<RobotPose> robotPoses = new();

        // ======= PROPERTY MAP TỪ AppDataService RA XAML =======

        public string HomeData => _data.HomeData;

        // ✅ Log máy và robot lấy trực tiếp từ AppDataService
        public ObservableCollection<string> MachineLog => _data.MachineLog;
        public ObservableCollection<string> RobotHistory => _data.RobotHistory;
        int idJob = 0;
        public HomeViewModel(INIFile ini, AppDataService data)
        {
            _data = data;
            _ini = ini;

            ip = _ini.Read("IPAddr", "PLCTCP");

            _db = new DatabaseRobot();
            LoadJobs();
            // Load trajectories vào _data
            var trajFromDb = _db.GetRobotTrajectories();
            _data.RobotTrajectories.Clear();
            foreach (var t in trajFromDb)
                _data.RobotTrajectories.Add(t);
            LoadSavedJobSelection();
          //  Application.Current.Dispatcher.InvokeAsync(LoadSavedJobSelection);

            // Đồng bộ HomeData từ AppDataService sang ViewModel
            _data.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AppDataService.HomeData))
                    OnPropertyChanged(nameof(HomeData));
            };
            _data.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AppDataService.ModuleSource))
                    OnPropertyChanged(nameof(ModuleSource));

                if (e.PropertyName == nameof(AppDataService.CurrentState))
                {
                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher == null || dispatcher.CheckAccess())
                        HomeCommand.NotifyCanExecuteChanged();
                    else
                        dispatcher.BeginInvoke(new Action(HomeCommand.NotifyCanExecuteChanged));
                }
            };

            // Load 15 ô từ DB 1 lần khi tạo VM
            try
            {
                int mask = _db.GetSlotsMask();
            

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi load TableSp: {ex.Message}");
            }
        }
        // ====== COMMAND CHO 15 Ô PHÔI ======

     

        [RelayCommand]
        private void ClearCycle()
        {
            _data.ClearCycleRequested = true;
        }

        [RelayCommand]
        private void Start()
        {
            _data.Ketqua++;
            // Load poses của Job này vào _data.RobotPoses
            var posesFromDb = _db.GetRobotPoses(idJob);
            _data.RobotPoses.Clear();
            foreach (var p in posesFromDb)
                _data.RobotPoses.Add(p);
            var trajFromDb = _db.GetRobotTrajectories();
            _data.RobotTrajectories.Clear();
            foreach (var t in trajFromDb)
                _data.RobotTrajectories.Add(t);
            // ✅ Load đủ 6 bộ calib: Tool1/2/3 x Camera1/2 để khi chạy chọn linh hoạt.
            LoadCalibAffines();
            _data.StartRequested = true;

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
                    var points = _db.GetCalibPoints(_data.GetCalibName(tool, camera));
                    _data.SetCalibAffine(tool, camera, TryFitAffine(points));
                }
            }

            LoadSelectedToolPreviewPoints();

            // Giữ lại field cũ để các đoạn code cũ không bị mất dữ liệu mặc định.
            _data.AffineCamera1 = _data.GetCalibAffine(camera: "Camera1");
            _data.AffineCamera2 = _data.GetCalibAffine(camera: "Camera2");
            _data._affine1 = _data.AffineCamera1;
            _data._affine2 = _data.AffineCamera2;
        }

        private void LoadSelectedToolPreviewPoints()
        {
            var camera1Points = _db.GetCalibPoints(_data.GetCalibName(camera: "Camera1"));
            var camera2Points = _db.GetCalibPoints(_data.GetCalibName(camera: "Camera2"));

            _data.CalibPointsCamera1.Clear();
            foreach (var p in camera1Points) _data.CalibPointsCamera1.Add(p);

            _data.CalibPointsCamera2.Clear();
            foreach (var p in camera2Points) _data.CalibPointsCamera2.Add(p);
        }

        private static Affine2D? TryFitAffine(IReadOnlyList<RobotPointCalib> points)
        {
            if (points == null || points.Count < 3)
                return null;

            try
            {
                return Affine2D.FitFromCalibPoints(points);
            }
            catch
            {
                return null;
            }
        }
        private bool CanHome() =>
            _data.CurrentState == AppState.Idle ||
            (_data.CurrentState == AppState.Error && !_data.IsResetProcessing);

        [RelayCommand(CanExecute = nameof(CanHome))]
        private void Home()
        {
            if (!CanHome())
                return;

            _data.HomeRequested = true;

            // Khi robot đang lỗi, nút Home đồng thời yêu cầu xóa lỗi. Chỉ sau
            // khi reset robot thành công AppBackgroundService mới cho phép chạy
            // quỹ đạo phục hồi về Home.
            if (_data.CurrentState == AppState.Error)
            {
                _data.IsResetProcessing = true;
                _data.ResetRequested = true;
            }
        }
        [RelayCommand]
        private void Pause()
        {
            
            _data.PauseRequested = true;
        }
        [RelayCommand]
        private void Stop()
        {

            _data.StopRequested = true;
        }
        [RelayCommand]
        private void Reset()
        {
            if (_data.IsResetProcessing ||
                (_data.CurrentState != AppState.Idle && _data.CurrentState != AppState.Error))
                return;

            // Gửi cờ Reset cho AppBackgroundService (HandleError sẽ xử lý)
            _data.IsResetProcessing = true;
            _data.ResetRequested = true;
        }
        [RelayCommand]
        private void Shutdown()
        {
            if (_data.CurrentState != AppState.Idle)
            {
                AddMachineLog(
                    $"[SYSTEM][BLOCKED] Không được phép nhấn Shutdown khi máy đang ở trạng thái {_data.CurrentState}. " +
                    "Hãy nhấn STOP và chờ máy về trạng thái Idle.");
                return;
            }

            if (!VietnameseConfirmationDialog.Confirm(
                    "Xác nhận tắt hệ thống",
                    "Bạn có chắc chắn muốn TẮT toàn bộ hệ thống Robot và máy tính không?\n\n" +
                    "Hãy bảo đảm robot đã dừng và mọi dữ liệu đã được lưu."))
                return;

            _data.ShutdownReq = true;
        }

        [RelayCommand]
        private void Restart()
        {
            if (_data.CurrentState != AppState.Idle)
            {
                AddMachineLog(
                    $"[SYSTEM][BLOCKED] Không được phép nhấn Restart khi máy đang ở trạng thái {_data.CurrentState}. " +
                    "Hãy nhấn STOP và chờ máy về trạng thái Idle.");
                return;
            }

            if (!VietnameseConfirmationDialog.Confirm(
                    "Xác nhận khởi động lại",
                    "Bạn có chắc chắn muốn KHỞI ĐỘNG LẠI hệ thống không?\n\n" +
                    "Hãy bảo đảm robot đã dừng và mọi dữ liệu đã được lưu."))
                return;

            _data.RestartReq = true;
        }

        private void AddMachineLog(string message)
        {
            string line = $"{DateTime.Now:HH:mm:ss} {message}";
            _data.MachineLog.Insert(0, line);
            if (_data.MachineLog.Count > 1000)
                _data.MachineLog.RemoveAt(_data.MachineLog.Count - 1);
        }
        // ======= VIEW MODE STATE (JOB / CAMERA) =======
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCameraViewVisible))]
        [NotifyPropertyChangedFor(nameof(JobCameraButtonText))]
        private bool isJobViewVisible = true;

        public bool IsCameraViewVisible => !IsJobViewVisible;
        public string JobCameraButtonText => IsJobViewVisible ? "Model" : "Cam";

        [RelayCommand]
        private void ShowJob()
        {
            IsJobViewVisible = true;
        }

        [RelayCommand]
        private void ShowCamera()
        {
            IsJobViewVisible = false;
        }

        [RelayCommand]
        private void ToggleJobCamera()
        {
            IsJobViewVisible = !IsJobViewVisible;
        }
        // ===== MODULE SOURCE CHO VmRenderControl =====
        public object? ModuleSource => _data.ModuleSource;


        public ProcessInfoList vmProcessInfoList = new ProcessInfoList();
        // ✅ Khi người dùng chọn Job khác
      bool chonjob = false;
        partial void OnSelectedJobChanged(JobModelHome value)
        {
            if (_isInternalChange) return;
            if (value == null) return;

            if (idJob == 0)
            {
                var result = MessageBox.Show(
                    "Bạn có chắc muốn thực hiện Chọn Job không?",
                    "Xác nhận",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    _isInternalChange = true;
                    SelectedJob = _previousJob;   // rollback
                    _isInternalChange = false;
                    return;
                }
            }

            idJob = 0;
            _previousJob = value;

            try
            {
                foreach (var job in ActiveJobs)
                    job.IsActiveJob = false;

                value.IsActiveJob = true;

                _data.JobName = value.JobName;
                UpdateSelectedJobHeightData(value);
                _data.LoadJobCounters(value.Id);
                _data.LoadJob = true;

                SaveSelectedJob();
            }
            catch
            {
                AutoCloseToast.ShowError("Load Solution Error", 1000);
            }
        }

        private void LoadJobs()
        {
            idJob = 2;
            ActiveJobs.Clear();

            var jobs = _db.GetJobsName();
            foreach (var job in jobs)
                ActiveJobs.Add(job);
        }

        public void UpdateJobHomeValue(JobModelHome job, string columnName, double value)
        {
            if (job == null) return;

            _db.UpdateJobHomeValue(job.Id, columnName, value);

            switch (columnName)
            {
                case "H1":
                    job.H1 = value;
                    break;
                case "H2":
                    job.H2 = value;
                    break;
                case "H3":
                    job.H3 = value;
                    break;
                case "R":
                    job.R = value;
                    break;
                default:
                    throw new ArgumentException("Cột không hợp lệ.", nameof(columnName));
            }

            if (SelectedJob?.Id == job.Id)
                UpdateSelectedJobHeightData(job);
        }

        private void UpdateSelectedJobHeightData(JobModelHome job)
        {
            _data.JobH1 = job.H1;
            _data.JobH2 = job.H2;
            _data.JobH3 = job.H3;
        }

        // ✅ Lưu Job được chọn
        private void SaveSelectedJob()
        {
            if (SelectedJob == null) return;
            var json = JsonSerializer.Serialize(new { SelectedJob.Id, SelectedJob.JobName });
            File.WriteAllText(SelectedJobFile, json);
        }
        private bool _isInternalChange;


        // ✅ Tải lại Job được chọn khi mở app
        private void LoadSavedJobSelection()
        {
            if (!File.Exists(SelectedJobFile)) return;

            try
            {
                var json = File.ReadAllText(SelectedJobFile);
                var saved = JsonSerializer.Deserialize<SavedJob>(json);

                if (saved != null && ActiveJobs.Any())
                {
                    var match = ActiveJobs.FirstOrDefault(j => j.Id == saved.Id);
                    if (match != null)
                    {
                        _isInternalChange = true;

                        foreach (var job in ActiveJobs)
                            job.IsActiveJob = false;

                        match.IsActiveJob = true;
                        SelectedJob = match;
                        _previousJob = match;

                        _data.JobName = match.JobName;
                        UpdateSelectedJobHeightData(match);
                        _data.LoadJobCounters(match.Id);
                        _data.LoadJob = true;

                        _isInternalChange = false;
                    }

                }
            }
            catch
            {
                // ignore
            }
        }

        private class SavedJob
        {
            public int Id { get; set; }
            public string JobName { get; set; } = "";
        }
    }
}
