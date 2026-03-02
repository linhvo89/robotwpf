using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;

namespace WpfCompanyApp.Models
{
    public class RobotPose : ObservableObject
    {
        public int Id { get; set; }
        public int JobId { get; set; }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private double _x;
        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        private double _y;
        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        private double _z;
        public double Z
        {
            get => _z;
            set => SetProperty(ref _z, value);
        }

        private double _rx;
        public double Rx
        {
            get => _rx;
            set => SetProperty(ref _rx, value);
        }

        private double _ry;
        public double Ry
        {
            get => _ry;
            set => SetProperty(ref _ry, value);
        }

        private double _rz;
        public double Rz
        {
            get => _rz;
            set => SetProperty(ref _rz, value);
        }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }
    }

    public class RobotTrajectory
    {
        public enum MoveTypeEnum
        {
            moveL,
            moveJ
        }

        public int Id { get; set; }
        public int JobId { get; set; }
        public string Name { get; set; }
        public string NamePoses { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Rx { get; set; }
        public double Ry { get; set; }
        public double Rz { get; set; }
        public double J1 { get; set; }
        public double J2 { get; set; }
        public double J3 { get; set; }
        public double J4 { get; set; }
        public double J5 { get; set; }
        public double J6 { get; set; }
        public double v { get; set; }
        public double a { get; set; }
        public int IsEnabled { get; set; }

        public MoveTypeEnum MoveType { get; set; } = MoveTypeEnum.moveL;
    }
}
