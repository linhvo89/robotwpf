using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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


        [ObservableProperty] private double speedCapture = 0.2;
        [ObservableProperty] private double speedWaitPick = 0.2;
        [ObservableProperty] private double speedMoveUp = 0.2;
        [ObservableProperty] private double speedReturn = 0.2;
        [ObservableProperty] private double speedUnused = 0.2;
        // ⭐ 5 giá trị đang được chọn cho 5 điểm Forward
        public ObservableCollection<RobotTrajectory.MoveTypeEnum> MoveTypes { get; } =
            new ObservableCollection<RobotTrajectory.MoveTypeEnum>
            {
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
                RobotTrajectory.MoveTypeEnum.moveL
            };
        [ObservableProperty]
        private RobotTrajectory.MoveTypeEnum prePickMoveType = RobotTrajectory.MoveTypeEnum.moveL;

        public SettingsViewModel(AppDataService data)
        {
            _data = data;
            LoadJobs();
            LoadInitialValues();
            MoveTypes.CollectionChanged += MoveTypes_CollectionChanged;
            ReturnMoveTypes.CollectionChanged += ReturnMoveTypes_CollectionChanged;
        }
        public int SelectedJobIndex { get; set; }
        bool isFirstLoad = false;
        private void LoadInitialValues()
        {
            try
            {
                // Ví dụ: đọc giá trị Vel1 từ bảng Config hoặc JobPose
                var result = _db.GetRobotTrajectories(); // trả về double
              //  _data.RobotTrajectories = result;
                ForwardVelocities[0] = result[0].v;  // bind trực tiếp
                ForwardVelocities[1] = result[1].v;
                ForwardVelocities[2] = result[2].v;
                ForwardVelocities[3] = result[3].v;
                ForwardVelocities[4] = result[4].v;
                ReturnVelocities[0]= result[5].v;
                ReturnVelocities[1] = result[6].v;
                ReturnVelocities[2] = result[7].v;
                ReturnVelocities[3] = result[8].v;
                ReturnVelocities[4] = result[9].v;
                // ===== MoveType FORWARD (Combobox Forward) =====
                MoveTypes[0] = result[0].MoveType;
                MoveTypes[1] = result[1].MoveType;
                MoveTypes[2] = result[2].MoveType;
                MoveTypes[3] = result[3].MoveType;
                MoveTypes[4] = result[4].MoveType;

                // ===== MoveType RETURN (Combobox Return) =====
                ReturnMoveTypes[0] = result[5].MoveType;
                ReturnMoveTypes[1] = result[6].MoveType;
                ReturnMoveTypes[2] = result[7].MoveType;
                ReturnMoveTypes[3] = result[8].MoveType;
                ReturnMoveTypes[4] = result[9].MoveType;
                PrePickVelocity = result[11].v;
                PrePickMoveType = result[11].MoveType;
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
                int index = e.NewStartingIndex;     // 0..4
                string namePoses = $"ForwardPose{index + 1}";
                var type = MoveTypes[index];

                _db.UpdateMoveTypeByNamePoses(namePoses, type);
            }
        }
        private void ReturnMoveTypes_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Replace)
            {
                int index = e.NewStartingIndex;        // 0..4
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

        // ====== FORWARD TRAJECTORY ======
        [RelayCommand]
        public void SaveForwardPoint(object param)
        {
            var result = MessageBox.Show(
                "Bạn có chắc muốn thực hiện Lưu vị trí hiện tại robot  không?",
                "Xác nhận",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {

                if (param is string str && int.TryParse(str, out int pointIndex))
                {
                    //MessageBox.Show($"Save Forward Point {pointIndex}");
                    // TODO: Gọi model lưu dữ liệu
                    _data.FUpdatePose = true;
                    _data.NamePose = $"ForwardPose{pointIndex}";
                }
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
        }

        [RelayCommand]
        public void MoveForwardPoint(object param)
        {
                var result = MessageBox.Show(
             "Bạn có chắc muốn thực hiện dịch chuyển robot  không?",
             "Xác nhận",
             MessageBoxButton.YesNo,
             MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (param is string str && int.TryParse(str, out int pointIndex))
                {
                    int idx = pointIndex - 1;                      // 1→0, 2→1, ...

                    string poseName = $"ForwardPose{pointIndex}"; // tên pose
                    var moveType = MoveTypes[idx];                 // moveL / moveJ từ ComboBox

                    _data.MovePoseName = poseName;
                    _data.MoveTypeToMove = moveType;
                    _data.RequestMovePose = true;
                }
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
         
        }

        // ====== RETURN TRAJECTORY ======
        [RelayCommand]
        public void SaveReturnPoint(object param)
        {
            var result = MessageBox.Show(
           "Bạn có chắc muốn thực hiện Lưu vị trí hiện tại robot  không?",
           "Xác nhận",
           MessageBoxButton.YesNo,
           MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (param is string str && int.TryParse(str, out int pointIndex))
                {
                    //MessageBox.Show($"Save Return Point {pointIndex}");
                    // TODO: Gọi model lưu dữ liệu
                    _data.FUpdatePose = true;
                    _data.NamePose = $"ReturnPose{pointIndex}";

                }
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
        }

        [RelayCommand]
        public void MoveReturnPoint(object param)
        {
            var result = MessageBox.Show(
          "Bạn có chắc muốn thực hiện dịch chuyển robot  không?",
          "Xác nhận",
          MessageBoxButton.YesNo,
          MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (param is string str && int.TryParse(str, out int pointIndex))
                {
                    int idx = pointIndex - 1;

                    string poseName = $"ReturnPose{pointIndex}";
                    var moveType = ReturnMoveTypes[idx];

                    _data.MovePoseName = poseName;
                    _data.MoveTypeToMove = moveType;
                    _data.RequestMovePose = true;
                }
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
           
        }

        // ====== HOME POSITION ======
        [RelayCommand]
        public void SaveHome()
        {
            var result = MessageBox.Show(
          "Bạn có chắc muốn thực hiện Lưu vị trí home robot không?",
          "Xác nhận",
          MessageBoxButton.YesNo,
          MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _data.FUpdatePose = true;
                _data.NamePose = "HomePose";
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
        }

        [RelayCommand]
        public void MoveHome()
        {
            var result = MessageBox.Show(
            "Bạn có chắc muốn thực hiện dịch chuyển robot về home không?",
            "Xác nhận",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _data.MovePoseName = "HomePose";
                _data.RequestMovePose = true;
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
        }
        // Velocity collection cho Return
        [ObservableProperty]
        private ObservableCollection<double> returnVelocities = new ObservableCollection<double> { 0, 0, 0, 0, 0 };

        // Velocity collection cho Forward (nếu cần)
        [ObservableProperty]
        private ObservableCollection<double> forwardVelocities = new ObservableCollection<double> { 0, 0, 0, 0, 0 };


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
            var result = MessageBox.Show(
             "Bạn có chắc muốn thực hiện Lưu vị trí robot không?",
             "Xác nhận",
             MessageBoxButton.YesNo,
             MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _data.FUpdatePose = true;
                _data.NamePose = $"PrePickPose";
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
          
        }

        /// <summary>
        /// Move robot tới PRE-PICK POSE với kiểu di chuyển & vận tốc đang chọn.
        /// </summary>
        [RelayCommand]
        private void MovePrePickPose()
        {
            var result = MessageBox.Show(
               "Bạn có chắc muốn thực hiện dịch chuyển robot không?",
               "Xác nhận",
               MessageBoxButton.YesNo,
               MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _data.MovePoseName = "PrePickPose";
                _data.RequestMovePose = true;
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
            
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
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
            
        }

        // ================== WORKSPACE POINTS (P1..P4) ==================

        /// <summary>
        /// Lưu 4 điểm workspace (P1..P4). CommandParameter trong XAML là 1..4.
        /// </summary>
        [RelayCommand]
        private void SaveWorkspacePoint(object? param)
        {
            var result = MessageBox.Show(
               "Bạn có chắc muốn thực hiện lưu vị trí robot hiện tại không không?",
               "Xác nhận",
               MessageBoxButton.YesNo,
               MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (param == null)
                    return;

                if (!int.TryParse(param.ToString(), out int index))
                    return;

                if (index < 1 || index > 6)
                    return;

                _data.FUpdatePose = true;
                _data.NamePose = $"WorkP{index}";
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
            
        }

        /// <summary>
        /// Move robot tới từng điểm workspace P1..P4.
        /// </summary>
        [RelayCommand]
        private void MoveWorkspacePoint(object? param)
        {
            var result = MessageBox.Show(
            "Bạn có chắc muốn thực hiện dịch chuyển robot không?",
            "Xác nhận",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (param == null)
                    return;

                if (!int.TryParse(param.ToString(), out int index))
                    return;

                if (index < 1 || index > 4)
                    return;
                _data.MovePoseName = $"WorkP{index}";
                _data.RequestMovePose = true;
            }
            else
            {
                // Nhấn NO: bỏ qua
                return;
            }
           
        }
        // Thêm các property cho Trigger Camera
        [ObservableProperty]
        private bool requestTriggerCamera = false;

        [ObservableProperty]
        private int numTriggerCamera = 0;

        [ObservableProperty]
        private ObservableCollection<RobotPositionItem> robotPositionList = new();
        // Add inside HomeViewModel class
      

        // Command Trigger từ UI
        [RelayCommand]
        private void TriggerCameraReq()
        {
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
    }
}
