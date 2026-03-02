using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WpfCompanyApp.Models
{
    public class VmSolutionInfo : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public VmSolutionInfo()
        {
            _vmSolutionPath = "";
            _vmSolutionPassword = "";
        }

        /// <summary>
        /// 方案路径
        /// Solution Path
        /// </summary>
        private string _vmSolutionPath;
        public string vmSolutionPath
        {

            get { return _vmSolutionPath; }
            set
            {
                _vmSolutionPath = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("vmSolutionPath"));
            }
        }

        /// <summary>
        /// 方案密码
        /// Solution Password
        /// </summary>
        private string _vmSolutionPassword;
        public string vmSolutionPassword
        {

            get { return _vmSolutionPassword; }
            set
            {
                _vmSolutionPassword = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("vmSolutionPassword"));
            }
        }

    }

}
