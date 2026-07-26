namespace WpfCompanyApp.Services
{
    /// <summary>
    /// Chuyển mã lỗi robot HANS thành nội dung dễ đọc.
    /// Bổ sung các mã mới vào switch khi có thêm tài liệu từ nhà sản xuất.
    /// </summary>
    public class Error_Robot
    {
        public string Ss_Error(int maloi)
        {
            switch (maloi)
            {
                case 10000: return "Short circuit error";
                case 10001: return "Over voltage limit error";
                case 10002: return "Under voltage limit error";
                case 10003: return "Over velocity limit error";
                case 10004: return "Execute error";
                case 10005: return "Over current error";
                case 10006: return "Encoder error";
                case 10007: return "Following position error";
                case 10008: return "Following velocity error";
                case 10009: return "Negative limit error";
                case 10010: return "Positive limit error";
                case 10011: return "Server over heating error";
                case 10012: return "Max current error";
                case 10013: return "Emergency stop error";
                case 10014: return "UDM error";
                case 10015: return "Server parameter error";

                case 20000: return "Controller is not started";
                case 20001: return "Master is not started";
                case 20002: return "Some slave is dropped";
                case 20003: return "Robot on safe stop state";
                case 20004: return "Robot on physical stop state";
                case 20005: return "Robot out safe space";
                case 20006: return "Robot enable time out";
                case 20007: return "Robot not electrify";
                case 20008: return "Starting master station error";

                case 30000: return "Collision shutdown";
                case 30001: return "Robot collide with body";
                case 30002: return "Over joint limit error";
                case 30003: return "Singularity error";
                case 30004: return "General stopping criterion";
                case 30005: return "Calculate failed";
                case 30006: return "UDM Status Error";
                case 30007: return "Init slave Error";
                case 30008: return "Home Step2 Error";
                case 30009: return "Out Of Direction Limit Error";
                case 30010: return "Out Of Direction Current Error";
                case 30011: return "Wrong load or mounting angle";
                case 30012: return "Motor limit temperature exceeded";

                case 1001: return "The robot has not been initialized";
                case 1002: return "Master station has not been started";
                case 1003: return "Slave station drop off";
                case 1004: return "The robot is safely locked";
                case 1005: return "The physical stop";
                case 1006: return "Robot has not been servo on";
                case 1007: return "Error reporting from slave station";
                case 1008: return "Robot beyond safe space";
                case 1009: return "In robot motion";
                case 1010: return "Invalid command";
                case 1011: return "Parameter error";
                case 1012: return "Function call format error";
                case 1013: return "Waiting for command execution";
                case 1014: return "IO does not exist";
                case 1015: return "Robots do not exist";
                case 1016: return "No connection server";
                case 1017: return "Network timeout";
                case 1018: return "Connection failed";
                case 1019: return "Serial connection failed";
                case 1020: return "No zero position is set";
                case 1021: return "The last same command has not been";
                case 1022: return "Serial port Di is empty";
                case 1023: return "Serial port DO is empty";
                case 1024: return "Wait timeout";
                case 1025: return "Error status";
                case 1026: return "Stop robot";
                case 1027: return "Robot has been servo off";
                case 1028: return "Robot has been servo on";
                case 1029: return "Function has not been enabled";
                case 1030: return "Start master timeout";
                case 1031: return "The robot has not been powered on";
                case 1032: return "Serial port has not been started";
                case 1033: return "The simulation state command is invalid";
                case 1034: return "RTOS Library not exsit";
                case 1035: return "DCS Handle Command thread crash";
                case 1039: return "Script running";
                case 1040: return "Xml Param Error";
                case 1041: return "System Board Not Connect";
                case 1042: return "Controller Not Start";
                case 1043: return "Controller Statu Error";
                case 1044: return "Robot in TeachMode";
                case 1045: return "Robot Already Electrify";
                case 1046: return "Connect to Modbus Failed";
                case 1047: return "Master is Started";
                case 1048: return "Parameter over specified payload";
                case 1049: return "DCS Status Error";
                case 1050: return "Target position invalid";
                case 1051: return "Robot Drive Operating";
                case 1052: return "Start Master Error";
                case 1053: return "Initilize slaves Error!HomeStep2 Fail";
                case 1054: return "ModebusRTU disconnected state";
                case 1055: return "ModebusRTU is busy";
                case 1056: return "Blending didn't start";
                case 1057: return "Blending is not over";

                case 2000: return "Failed to load library";
                case 2001: return "The script is empty";
                case 2002: return "Compile error";
                case 2003: return "Reload script error";
                case 2004: return "Function does not exist";
                case 2005: return "Function return type error";
                case 2006: return "MissSignal1";
                case 2007: return "MissSignal2";
                case 2008: return "Parameter type error";
                case 2009: return "There is no header file included";
                case 2010: return "No return value";
                case 2012: return "UDM Stack Err";
                case 2013: return "Script been lock,maybe compiling";
                case 2014: return "Not In RunScript Statu";
                case 2015: return "Serial Close";
                case 2016: return "Serial Close";
                case 2017: return "Controller not started";
                case 2018: return "Socket Not Connected";
                case 2020: return "Function Name have Space.";
                case 2021: return "Socket Error";
                case 2022: return "Function broken stop.";
                case 2023: return "Timer running error";
                case 2024: return "Enable SwitchON key error";

                default:
                    return $"Unknown robot error ({maloi})";
            }
        }
    }
}
