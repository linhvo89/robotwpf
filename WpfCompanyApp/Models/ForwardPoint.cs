using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WpfCompanyApp.Models
{
    public class ForwardPoint : INotifyPropertyChanged
    {
        public int Index { get; set; }

        private double velocity;
        public double Velocity
        {
            get => velocity;
            set { velocity = value; OnPropertyChanged(nameof(Velocity)); }
        }

        private RobotTrajectory.MoveTypeEnum moveType;
        public RobotTrajectory.MoveTypeEnum MoveType
        {
            get => moveType;
            set { moveType = value; OnPropertyChanged(nameof(MoveType)); }
        }

        private int idCam;
        public int IdCam
        {
            get => idCam;
            set { idCam = value; OnPropertyChanged(nameof(IdCam)); }
        }

        private bool enableCam;
        public bool EnableCam
        {
            get => enableCam;
            set { enableCam = value; OnPropertyChanged(nameof(EnableCam)); }
        }

        private bool enable;
        public bool Enable
        {
            get => enable;
            set { enable = value; OnPropertyChanged(nameof(Enable)); }
        }

        private string note = "";
        public string Note
        {
            get => note;
            set { note = value; OnPropertyChanged(nameof(Note)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
