using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using VM.Core;
using WpfCompanyApp.Converters;
using WpfCompanyApp.Data;
using WpfCompanyApp.Models;
using WpfCompanyApp.Services;
using WpfCompanyApp.Views;

namespace WpfCompanyApp.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly DatabaseRobot _db = new();

        [ObservableProperty]
        private ObservableCollection<JobModelSetting> jobs;

        [ObservableProperty]
        private string jobNameInput; // <-- Dữ liệu từ TextBox

        [ObservableProperty]
        private JobModelSetting selectedJob;
        /// <summary>
        /// Vận tốc cho điểm PRE-PICK (0..1, đang được binding với TextBox PrePickVelocity).
        /// </summary>
        [ObservableProperty]
        private double prePickVelocity = 0.02;   // giá trị mặc định, bạn chỉnh tùy ý

        /// <summary>
        /// Kiểu di chuyển cho PRE-PICK (moveL / moveJ), binding với ComboBox PrePickMoveType.
        /// </summary>
     
        [ObservableProperty]
        private ObservableCollection<RobotPose> robotPoses = new();
        private readonly AppDataService _data;
        public AppDataService Data => _data;
        // ⭐ Các lựa chọn cho ComboBox (moveL, moveJ)
        public Array MoveTypeOptions => Enum.GetValues(typeof(RobotTrajectory.MoveTypeEnum));
        // Danh sách tốc độ 0.05 → 1.00
        public ObservableCollection<double> SpeedOptions { get; } =
            new(Enumerable.Range(1, 20)
                .Select(i => Math.Round(i * 0.05, 2)));
        // ⭐ 6 giá trị đang được chọn cho 6 điểm Forward
        public ObservableCollection<RobotTrajectory.MoveTypeEnum> MoveTypes { get; } =
            new ObservableCollection<RobotTrajectory.MoveTypeEnum>
            {
            RobotTrajectory.MoveTypeEnum.moveL,
            RobotTrajectory.MoveTypeEnum.moveL,
            RobotTrajectory.MoveTypeEnum.moveL,
            RobotTrajectory.MoveTypeEnum.moveL,
            RobotTrajectory.MoveTypeEnum.moveL,
            RobotTrajectory.MoveTypeEnum.moveL
            };
        // ⭐ thêm cho RETURN
        public ObservableCollection<RobotTrajectory.MoveTypeEnum> ReturnMoveTypes { get; } =
            new ObservableCollection<RobotTrajectory.MoveTypeEnum>
            {
                RobotTrajectory.MoveTypeEnum.moveL,
                RobotTrajectory.MoveTypeEnum.moveL,
                RobotTrajectory.MoveTypeEnum.moveL,
                RobotTrajectory.MoveTypeEnum.moveL,
                RobotTrajectory.MoveTypeEnum.moveL,
                RobotTrajectory.MoveTypeEnum.moveL
            };
        [ObservableProperty]
        private RobotTrajectory.MoveTypeEnum prePickMoveType = RobotTrajectory.MoveTypeEnum.moveL;

        public SettingsViewModel(AppDataService data)
        {
            _data = data;
            LoadJobs();
            LoadInitialValues();
            _data.PropertyChanged += Data_PropertyChanged;
            MoveTypes.CollectionChanged += MoveTypes_CollectionChanged;
            ReturnMoveTypes.CollectionChanged += ReturnMoveTypes_CollectionChanged;
        }

        private void Data_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppDataService.SelectedCalibTool) ||
                e.PropertyName == nameof(AppDataService.SelectedTriggerCamera))
            {
                _data.ResetTriggerSaveStatus();
            }
        }
        public int SelectedJobIndex { get; set; }
        bool isFirstLoad = false;
        private void LoadInitialValues()
        {
            try
            {
                var result = _db.GetRobotTrajectories()
                    .Where(item => !string.IsNullOrWhiteSpace(item.NamePoses))
                    .GroupBy(item => item.NamePoses, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < 6; i++)
                {
                    if (result.TryGetValue($"ForwardPose{i + 1}", out RobotTrajectory forward))
                    {
                        ForwardVelocities[i] = forward.v;
                        MoveTypes[i] = forward.MoveType;
                    }

                    if (result.TryGetValue($"ReturnPose{i + 1}", out RobotTrajectory returnPoint))
                    {
                        ReturnVelocities[i] = returnPoint.v;
                        ReturnMoveTypes[i] = returnPoint.MoveType;
                    }
                }

                if (result.TryGetValue("PrePickPose", out RobotTrajectory prePick))
                {
                    PrePickVelocity = prePick.v;
                    PrePickMoveType = prePick.MoveType;
                }
            }
            catch (Exception ex)
            {
                // Nếu lỗi, gán giá trị mặc định
                //Vel1 = 0;
            }
        }
        [RelayCommand]
        private void LoadJobs()
        {
            
            // 🟦 Lưu lại job đang được chọn
            var oldSelected = SelectedJob;

            // Nếu Jobs chưa có thì khởi tạo
            if (Jobs == null)
                Jobs = new ObservableCollection<JobModelSetting>();

            // 🟦 Làm rỗng danh sách cũ thay vì tạo mới
            Jobs.Clear();

            // 🟦 Nạp lại dữ liệu từ DB
            foreach (var job in _db.GetJobs())
                Jobs.Add(job);
            isFirstLoad =true;
            //// 🟦 Gán lại SelectedJob nếu job cũ còn tồn tại
            //if (oldSelected != null)
            //{
            //    var match = Jobs.FirstOrDefault(j => j.Id == oldSelected.Id);
            //    if (match != null)
            //        SelectedJob = match;
            //}
        }
       
        partial void OnPrePickMoveTypeChanged(RobotTrajectory.MoveTypeEnum value)
        {
            string namePoses = $"PrePickPose";
            var type = value;

            _db.UpdateMoveTypeByNamePoses(namePoses, type);
            // ví dụ: lưu xuống _data, hoặc settings
        }
        private void MoveTypes_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Replace)
            {
                int index = e.NewStartingIndex;     // 0..5
                string namePoses = $"ForwardPose{index + 1}";
                var type = MoveTypes[index];

                _db.UpdateMoveTypeByNamePoses(namePoses, type);
            }
        }
        private void ReturnMoveTypes_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Replace)
            {
                int index = e.NewStartingIndex;        // 0..5
                string namePoses = $"ReturnPose{index + 1}"; // hoặc tên bạn đang dùng trong DB
                var type = ReturnMoveTypes[index];

                _db.UpdateMoveTypeByNamePoses(namePoses, type);
                System.Diagnostics.Debug.WriteLine($"[ReturnMoveTypes] {namePoses} => {type}");
            }
        }
        public ProcessInfoList vmProcessInfoList = new ProcessInfoList();
        partial void OnSelectedJobChanged(JobModelSetting value)
        {
            try
            {
                if(isFirstLoad) 
                {
                    isFirstLoad = false;
                    return;
                }
             
                //VmSolutionInfo vmSolutionInfo = new VmSolutionInfo();
                //string path111 = AppDomain.CurrentDomain.BaseDirectory + "Solution\\" + _data.JobName + ".sol";
                //vmSolutionInfo.vmSolutionPath = path111;
                //try
                //{
                //    //   if(nameSolution !=  nameSolutionClear) 
                //    {
                //        Task task = Task.Run(() =>
                //        {
                //            if (VmSolution.Instance.SolutionPath != null)
                //            {
                //                VmSolution.Save();
                //                VmSolution.Instance.CloseSolution();
                //            }
                //        });
                //        task.Wait();  // Chờ task hoàn thành trước khi tiếp tục
                //    }
                //}
                //catch
                //{

                //}
                //vmSolutionInfo.vmSolutionPath = path111;
                //VmSolution.Load(vmSolutionInfo.vmSolutionPath, "196370");
                //vmProcessInfoList = VmSolution.Instance.GetAllProcedureList();//Obtain all processes in the solution
                _data.JobName = value.JobName;
                _data.LoadJob = true;
                //AutoCloseToast.ShowSuccess("Load Solution successfulg ✔", 1000);

            }
            catch
            {
                AutoCloseToast.ShowError("Load Solution Error", 1000);
            } 

        }
      
        string path_fileSolution = AppDomain.CurrentDomain.BaseDirectory + "Solution\\";

        [RelayCommand]
        private void AddJob()
        {
            string NewModel = "";
            string vmSolutionPath = "";
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "KBOT Sol File|*.sol*";

            bool? openFileRes = openFileDialog.ShowDialog();

            if (openFileRes == true)
            {
                vmSolutionPath = openFileDialog.FileName;

                // Cách gọn và đúng chuẩn hơn
                NewModel = System.IO.Path.GetFileNameWithoutExtension(vmSolutionPath);
            }
            else
            {
                return;
            }
              bool kq=  _db.IsJobModelExists(NewModel);
            if(kq==false)
            {

                // Tạo đường dẫn tới file đích bằng cách kết hợp đường dẫn thư mục đích và tên file nguồn
                string destinationFilePath = System.IO.Path.Combine(path_fileSolution, System.IO.Path.GetFileName(vmSolutionPath));

                try
                {
                    // Copy file
                    File.Copy(vmSolutionPath, destinationFilePath, true);
                    _db.InsertJobModel(NewModel);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred: {ex.Message}");
                    return;
                }
            }
            else
            {
                MessageBox.Show("Job đã tồn tại, vui lòng chọn tên khác");
                return;
            }

            
        }
        [RelayCommand]
        private void SaveJob()
        {
            try
            {
                VmSolution.Save();
                AutoCloseToast.ShowSuccess("Lưu Job thành công ✔", 1000);
            }
            catch {
                AutoCloseToast.ShowError("Error Lưu Job ", 1000);
            }
         
        }

        [RelayCommand]
        private void DeleteJob()
        {
            var result = MessageBox.Show(
            "Bạn có chắc muốn thực hiện Xóa Job không?",
            "Xác nhận",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                
                if (SelectedJob == null) return;
                _db.DeleteJobModelByName(SelectedJob.JobName);
                
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
          
        }

    

        [RelayCommand]
        private void DeletePose(RobotPose pose)
        {

            var result = MessageBox.Show(
              "Bạn có chắc muốn thực hiện Xóa pose Robot không?",
              "Xác nhận",
              MessageBoxButton.YesNo,
              MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (pose == null) return;
                _db.DeletePose(pose.Id);
                RobotPoses.Remove(pose);
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
           
        }

        [RelayCommand]
        private void EditPose(RobotPose pose)
        {
           
            var result = MessageBox.Show(
              "Bạn có chắc muốn thực hiện cập nhật Lưu vị trí hiện tại Robot không?",
              "Xác nhận",
              MessageBoxButton.YesNo,
              MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (pose == null) return;
                // _db.UpdatePose(pose);
                // Đây chính là RobotPose của đúng dòng user vừa click
                _data.PoseToEdit = pose;
                _data.RequestEditPose = true;
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
        }

      
        [ObservableProperty]
        private double speed;

        private RobotTrajectory? GetSavedPose(string poseName)
        {
            try
            {
                return _db.GetRobotTrajectoryByNamePoses(poseName);
            }
            catch
            {
                return null;
            }
        }

        private static string FormatPoseCoordinates(RobotTrajectory pose) =>
            $"X: {pose.X:0.###}   Y: {pose.Y:0.###}   Z: {pose.Z:0.###}\n" +
            $"Rx: {pose.Rx:0.###}   Ry: {pose.Ry:0.###}   Rz: {pose.Rz:0.###}";

        private bool ConfirmSaveRobotPoint(string poseName, string displayName)
        {
            var savedPose = GetSavedPose(poseName);
            var oldValue = savedPose == null
                ? "Điểm này chưa có dữ liệu đã lưu."
                : $"Tọa độ đang lưu:\n{FormatPoseCoordinates(savedPose)}\n\nDữ liệu trên sẽ bị ghi đè.";

            return VietnameseConfirmationDialog.Confirm(
                "Xác nhận lưu điểm robot",
                $"Bạn chuẩn bị LƯU VỊ TRÍ HIỆN TẠI của robot vào:\n\n" +
                $"Điểm: {displayName}\nMã điểm: {poseName}\n\n{oldValue}\n\n" +
                "Bạn có chắc chắn muốn lưu không?");
        }

        private bool ConfirmMoveRobotPoint(string poseName, string displayName)
        {
            var savedPose = GetSavedPose(poseName);
            if (savedPose == null)
            {
                VietnameseConfirmationDialog.ShowWarning(
                    "Không thể di chuyển",
                    $"Điểm {displayName} ({poseName}) chưa có dữ liệu tọa độ.\n\nHãy lưu điểm trước khi di chuyển robot.");
                return false;
            }

            return VietnameseConfirmationDialog.Confirm(
                "Xác nhận di chuyển robot",
                $"Robot chuẩn bị DI CHUYỂN đến:\n\n" +
                $"Điểm: {displayName}\nMã điểm: {poseName}\n\n" +
                $"Tọa độ đích:\n{FormatPoseCoordinates(savedPose)}\n\n" +
                "Hãy bảo đảm vùng làm việc an toàn. Bạn có chắc chắn muốn di chuyển không?");
        }

        // ====== FORWARD TRAJECTORY ======
        [RelayCommand]
        public void SaveForwardPoint(object param)
        {
            if (param is string str && int.TryParse(str, out int pointIndex))
            {
                string poseName = $"ForwardPose{pointIndex}";
                if (!ConfirmSaveRobotPoint(poseName, $"Đi thả {pointIndex}")) return;
                _data.FUpdatePose = true;
                _data.NamePose = poseName;
            }
        }

        [RelayCommand]
        public void MoveForwardPoint(object param)
        {
            if (param is string str && int.TryParse(str, out int pointIndex))
            {
                int idx = pointIndex - 1;
                string poseName = $"ForwardPose{pointIndex}";
                if (!ConfirmMoveRobotPoint(poseName, $"Đi thả {pointIndex}")) return;
                _data.MovePoseName = poseName;
                _data.MoveTypeToMove = MoveTypes[idx];
                _data.RequestMovePose = true;
            }
         
        }

        // ====== RETURN TRAJECTORY ======
        [RelayCommand]
        public void SaveReturnPoint(object param)
        {
            if (param is string str && int.TryParse(str, out int pointIndex))
            {
                string poseName = $"ReturnPose{pointIndex}";
                if (!ConfirmSaveRobotPoint(poseName, $"Quay về {pointIndex}")) return;
                _data.FUpdatePose = true;
                _data.NamePose = poseName;
            }
        }

        [RelayCommand]
        public void MoveReturnPoint(object param)
        {
            if (param is string str && int.TryParse(str, out int pointIndex))
            {
                int idx = pointIndex - 1;
                string poseName = $"ReturnPose{pointIndex}";
                if (!ConfirmMoveRobotPoint(poseName, $"Quay về {pointIndex}")) return;
                _data.MovePoseName = poseName;
                _data.MoveTypeToMove = ReturnMoveTypes[idx];
                _data.RequestMovePose = true;
            }
           
        }

        // ====== HOME POSITION ======
        [RelayCommand]
        public void SaveHome()
        {
            if (!ConfirmSaveRobotPoint("HomePose", "Vị trí Home")) return;
            _data.FUpdatePose = true;
            _data.NamePose = "HomePose";
        }

        [RelayCommand]
        public void MoveHome()
        {
            if (!ConfirmMoveRobotPoint("HomePose", "Vị trí Home")) return;
            _data.MovePoseName = "HomePose";
            _data.RequestMovePose = true;
        }

        [RelayCommand]
        public void SavePickProduct()
        {
            if (!ConfirmSaveRobotPoint("PickProductPose", "Vị trí nhặt sản phẩm")) return;
            _data.FUpdatePose = true;
            _data.NamePose = "PickProductPose";
        }

        [RelayCommand]
        public void MovePickProduct()
        {
            if (!ConfirmMoveRobotPoint("PickProductPose", "Vị trí nhặt sản phẩm")) return;
            _data.MovePoseName = "PickProductPose";
            _data.RequestMovePose = true;
        }
        // Velocity collection cho Return
        [ObservableProperty]
        private ObservableCollection<double> returnVelocities = new ObservableCollection<double> { 0, 0, 0, 0, 0, 0 };

        // Velocity collection cho Forward (nếu cần)
        [ObservableProperty]
        private ObservableCollection<double> forwardVelocities = new ObservableCollection<double> { 0, 0, 0, 0, 0, 0 };


        [RelayCommand]
        private void SaveReturnVelocity(object param)
        {
            var result = MessageBox.Show(
           "Bạn có chắc muốn thực hiện Lưu vị trí robot không?",
           "Xác nhận",
           MessageBoxButton.YesNo,
           MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (param is string s && int.TryParse(s, out int index))
                {
                    double vel = ReturnVelocities[index - 1];
                    RobotTrajectory robotTrajectory = new RobotTrajectory();
                    robotTrajectory.v = vel;
                    robotTrajectory.NamePoses = $"ReturnPose{index}";
                    _db.UpdateVel(robotTrajectory);
                    AutoCloseToast.ShowSuccess(
                        $"Đã lưu vận tốc ReturnPose{index}: {vel:0.##} ✔",
                        1800);
                    // lưu…
                }
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
          
        }

        [RelayCommand]
        private void SaveForwardVelocity(object param)
        {
           
            var result = MessageBox.Show(
           "Bạn có chắc muốn thực hiện Lưu vị trí hiện tại robot không?",
           "Xác nhận",
           MessageBoxButton.YesNo,
           MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                 if (param is string s && int.TryParse(s, out int index))
                {
                    double vel = ForwardVelocities[index - 1];
                    RobotTrajectory robotTrajectory = new RobotTrajectory();
                    robotTrajectory.v = vel;
                    robotTrajectory.NamePoses = $"ForwardPose{index}";
                    _db.UpdateVel(robotTrajectory);
                    AutoCloseToast.ShowSuccess(
                        $"Đã lưu vận tốc ForwardPose{index}: {vel:0.##} ✔",
                        1800);
                    // lưu…
                }
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
        }
        /// <summary>
        /// Lưu pose hiện tại của robot làm PRE-PICK POSE.
        /// </summary>
        [RelayCommand]
        private void SavePrePickPose()
        {
            if (!ConfirmSaveRobotPoint("PrePickPose", "Điểm trước khi xuống gắp")) return;
            _data.FUpdatePose = true;
            _data.NamePose = "PrePickPose";
        }

        /// <summary>
        /// Move robot tới PRE-PICK POSE với kiểu di chuyển & vận tốc đang chọn.
        /// </summary>
        [RelayCommand]
        private void MovePrePickPose()
        {
            if (!ConfirmMoveRobotPoint("PrePickPose", "Điểm trước khi xuống gắp")) return;
            _data.MovePoseName = "PrePickPose";
            _data.RequestMovePose = true;
        }

        /// <summary>
        /// Lệnh "Save Vel" cho PRE-PICK (nếu bạn muốn lưu vận tốc này xuống DB/settings).
        /// Nếu không cần lưu DB thì lệnh này có thể chỉ để ghi log.
        /// </summary>
        [RelayCommand]
        private void SavePrePickVelocity()
        {
            var result = MessageBox.Show(
               "Bạn có chắc muốn thực hiện Lưu vi trí robot hiện tại không?",
               "Xác nhận",
               MessageBoxButton.YesNo,
               MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                double vel = PrePickVelocity;
                RobotTrajectory robotTrajectory = new RobotTrajectory();
                robotTrajectory.v = vel;
                robotTrajectory.NamePoses = $"PrePickPose";
                _db.UpdateVel(robotTrajectory);
                AutoCloseToast.ShowSuccess(
                    $"Đã lưu vận tốc PrePickPose: {vel:0.##} ✔",
                    1800);
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
            
        }

        // ================== WORKSPACE POINTS (P1..P10) ==================

        /// <summary>
        /// Lưu 10 điểm workspace (P1..P10). CommandParameter trong XAML là 1..10.
        /// </summary>
        [RelayCommand]
        private void SaveWorkspacePoint(object? param)
        {
            if (param == null || !int.TryParse(param.ToString(), out int index) || index < 1 || index > 10)
                return;

            string poseName = $"WorkP{index}";
            if (!ConfirmSaveRobotPoint(poseName, $"Điểm không gian P{index}")) return;
            _data.FUpdatePose = true;
            _data.NamePose = poseName;
        }

        [RelayCommand]
        private void SavePickOffsets()
        {
            _data.SavePickOffsets();
            AutoCloseToast.ShowSuccess("Đã lưu Offset điểm hút ✔", 1800);
        }

        /// <summary>
        /// Move robot tới từng điểm workspace P1..P10.
        /// </summary>
        [RelayCommand]
        private void MoveWorkspacePoint(object? param)
        {
            if (param == null || !int.TryParse(param.ToString(), out int index) || index < 1 || index > 10)
                return;

            string poseName = $"WorkP{index}";
            if (!ConfirmMoveRobotPoint(poseName, $"Điểm không gian P{index}")) return;
            _data.MovePoseName = poseName;
            _data.RequestMovePose = true;
        }
        // Thêm các property cho Trigger Camera
        [ObservableProperty]
        private bool requestTriggerCamera = false;

        [ObservableProperty]
        private int numTriggerCamera = 0;

        [ObservableProperty]
        private ObservableCollection<RobotPositionItem> robotPositionList = new();

        // ✅ Danh sách calibration tool (tool1, tool2, tool3)
        [ObservableProperty]
        private ObservableCollection<string> calibToolList = new(new[] { "Tool1", "Tool2", "Tool3" });

        // ✅ Danh sách camera dùng cho trigger VisionMaster Flow1/Flow2
        [ObservableProperty]
        private ObservableCollection<string> triggerCameraList = new(new[] { "Camera1", "Camera2" });

        [ObservableProperty]
        private ObservableCollection<string> basketRunModeList = new(new[] { "Basket1", "Basket2", "Both" });

        [ObservableProperty]
        private ObservableCollection<string> fullWorkSensorList = new(new[] { "Máy1", "Máy2" });

        // ✅ Tool được chọn (mặc định Tool1)
        [ObservableProperty]
        private string selectedCalibTool = "Tool1";

        // Add inside HomeViewModel class
      

        // Command Trigger từ UI
        [RelayCommand]
        private void TriggerCameraReq()
        {
            _data.NumTriggerCamera = 0;
            _data.RobotPositionList.Clear();
            _data.IsSaveAllSuccess = false;
            _data.ShowTriggerPositions = false;
            _data.RequestTriggerCamera = true;
        }

        // Command Save Position
        [RelayCommand]
        private void SavePositionReq(RobotPositionItem position)
        {
            if (position == null) return;
            _data.RequestSavePositionTrigger = true;
            _data.IndexTrigger = position.PositionId;
        }
        [RelayCommand]
        private void SaveAllPositionsReq()
        {
            // Chưa có vị trí thì thôi
            if (_data.RobotPositionList == null || _data.RobotPositionList.Count == 0)
                return;

            // Cách 1: gọi y như bấm từng nút (set flag theo index từng cái)
            // Nếu bạn xử lý save ở Background theo RequestSavePositionTrigger, dùng Cách 2 bên dưới cho “đúng kiến trúc”.

            // Cách 2 (khuyến nghị): set flag SaveAll để background xử lý 1 lần
            _data.RequestSaveAllPositionsTrigger = true;
        }
        [RelayCommand]
        private void ExportCalibPointsToTxt()
        {
            try
            {
                string selectedCalibName = _data.GetCalibName();
                var points = _db.GetCalibPoints(selectedCalibName);

                if (points == null || points.Count == 0)
                {
                    AutoCloseToast.ShowError($"Không có dữ liệu calib_points cho {selectedCalibName}", 1200);
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Title = $"Xuất calib_points - {selectedCalibName}",
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    FileName = $"{selectedCalibName}_calib_points.txt"
                };

                bool? result = dialog.ShowDialog();
                if (result != true)
                    return;

                var lines = points.Select(p =>
                 string.Format(CultureInfo.InvariantCulture,
                     "{0,12:0.000}{1,12:0.000}{2,12:0.000}{3,12:0.000}{4,10:0.000}",
                     p.ImageX, p.ImageY, p.RobotX, p.RobotY, p.Angle));

                File.WriteAllLines(dialog.FileName, lines);
                AutoCloseToast.ShowSuccess($"Đã xuất {points.Count} dòng ra file txt", 1200);
            }
            catch (Exception ex)
            {
                AutoCloseToast.ShowError($"Xuất calib_points thất bại: {ex.Message}", 1500);
            }
        }
    }
}
