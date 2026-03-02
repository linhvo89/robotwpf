using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WpfCompanyApp.Models
{
    class DCSInfo
    {
        public static int Mode { get; set; }
        public static string ActiveRobot { get; set; }
        public static string IPAddrRobot { get; set; }
        public static int PortRobot { get; set; }
        public static int TimeOutRobot { get; set; }
        public static string Username { get; set; }
        public static string Password { get; set; }
        public static string ReadActualPos { get; set; }
        public static int Id_Select { get; set; }
        public static string ActiveCamera { get; set; }
        public static string IPCamera { get; set; }
        public static int PortCamera { get; set; }
        public static int TimeOutCamera { get; set; }
        public static string ActiveMobusTCP { get; set; }
        public static string IPMobusTCP { get; set; }
        public static int PortMobusTCP { get; set; }
        public static int TimeOutMobusTCP { get; set; }
        public static string ActiveServerTCP { get; set; }
        public static string IPServerTCP { get; set; }
        public static int PortServerTCP { get; set; }
        public static int TimeOutServerTCP { get; set; }
        public static string ActiveCom { get; set; }
        public static string ComName { get; set; }
        public static string ActiveTCPClient { get; set; }
        public static string IPTCPClient { get; set; }
        public static int PortTCPClient { get; set; }
        public static int TimeOutTCPClient { get; set; }

        public static int log { get; set; }
        public static bool flagStart { get; set; }
        public static bool flagDemo { get; set; }
        public static bool flagEdit { get; set; }

        public static int Control_Robot { get; set; }
        public static int Control_Rxyz { get; set; }

        public static bool Xup { get; set; }
        public static bool Xdown { get; set; }
        public static bool Yup { get; set; }
        public static bool Ydown { get; set; }
        public static bool Zup { get; set; }
        public static bool Zdown { get; set; }
    }
}
