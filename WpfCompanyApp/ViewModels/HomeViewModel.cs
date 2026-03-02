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
            _data.CycleTime = 1;
            _data.CycleCount = 0;
        }

        [RelayCommand]
        private void Start()
        {
            _data.Ketqua++;
            _data.StartRequested = true;
            // Load poses của Job này vào _data.RobotPoses
            var posesFromDb = _db.GetRobotPoses(idJob);
            _data.RobotPoses.Clear();
            foreach (var p in posesFromDb)
                _data.RobotPoses.Add(p);
            var trajFromDb = _db.GetRobotTrajectories();
            _data.RobotTrajectories.Clear();
            foreach (var t in trajFromDb)
                _data.RobotTrajectories.Add(t);
        }
        [RelayCommand]
        private void Home()
        {
           
            _data.HomeRequested = true;
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
            // Gửi cờ Reset cho AppBackgroundService (HandleError sẽ xử lý)
            _data.ResetRequested = true;
        }
        [RelayCommand]
        private void Shutdown()
        {
            _data.ShutdownReq = true;
        }

        [RelayCommand]
        private void Restart()
        {
            _data.RestartReq = true;
        }
        // ======= VIEW MODE STATE (JOB / CAMERA) =======
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCameraViewVisible))]
        private bool isJobViewVisible = true;

        public bool IsCameraViewVisible => !IsJobViewVisible;

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
