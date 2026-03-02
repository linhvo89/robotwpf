using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WpfCompanyApp.Models;
using Newtonsoft.Json;
using System.Threading;

namespace WpfCompanyApp.Services
{
    public class Command
    {
        public string cmdName { get; set; }
    }
    public class TcpPosResponse
    {
        public string errorCode { get; set; }
        public List<double> tcp_pos { get; set; }
        public string cmdName { get; set; }
        public string errorMsg { get; set; }
    }
    public class Getjointposition
    {
        public string errorCode { get; set; }
        public List<double> joint_pos { get; set; }
        public string cmdName { get; set; }
        public string errorMsg { get; set; }
    }
    public class ConmandRobot : Conmutacion
    {

        // Command
        public int GetPositionMoveLJaka(out string error, out PosMoveL posMoveL)
        {
            try
            {
                string jsonString = "{\"cmdName\":\"get_tcp_pos\"}";
                Command command = JsonConvert.DeserializeObject<Command>(jsonString);
                int sends = tcpsent(jsonString);
                if (sends == 0)
                {
                    string bufdata = subReciveCmdJaka(command.cmdName);
                    TcpPosResponse command2 = JsonConvert.DeserializeObject<TcpPosResponse>(bufdata);
                    if ((bufdata != "") && (command2.cmdName == command.cmdName) && command2.errorCode == "0")
                    {
                        posMoveL = new PosMoveL();
                        posMoveL.X = command2.tcp_pos[0];
                        posMoveL.Y = command2.tcp_pos[1];
                        posMoveL.Z = command2.tcp_pos[2];
                        posMoveL.RX = command2.tcp_pos[3];
                        posMoveL.RY = command2.tcp_pos[4];
                        posMoveL.RZ = command2.tcp_pos[5];
                        error = command2.errorMsg;
                        return 0;
                    }
                    else
                    {
                        posMoveL = new PosMoveL();
                        error = command2.errorMsg;
                        return 1;
                    }
                }
                else
                {
                    posMoveL = new PosMoveL();
                    error = "error send robot";
                    return 1;
                }
            }
            catch (Exception ex)
            {
                posMoveL = new PosMoveL();
                error = "Error: " + ex.Message;
                return 1;
            }

        }
        public int GetPositionMoveJJaka(out string error, out PosMoveJ posMoveJ)
        {
            try
            {
                string jsonString = "{\"cmdName\":\"get_joint_pos\"}";
                Command command = JsonConvert.DeserializeObject<Command>(jsonString);
                int sends = tcpsent(jsonString);
                if (sends == 0)
                {
                    string bufdata = subReciveCmdJaka(command.cmdName);
                    Getjointposition command2 = JsonConvert.DeserializeObject<Getjointposition>(bufdata);
                    if ((bufdata != "") && (command2.cmdName == command.cmdName) && command2.errorCode == "0")
                    {
                        posMoveJ = new PosMoveJ();
                        posMoveJ.J1 = command2.joint_pos[0];
                        posMoveJ.J2 = command2.joint_pos[1];
                        posMoveJ.J3 = command2.joint_pos[2];
                        posMoveJ.J4 = command2.joint_pos[3];
                        posMoveJ.J5 = command2.joint_pos[4];
                        posMoveJ.J6 = command2.joint_pos[5];
                        error = command2.errorMsg;
                        return 0;
                    }
                    else
                    {
                        posMoveJ = new PosMoveJ();
                        error = command2.errorMsg;
                        return 1;
                    }
                }
                else
                {
                    posMoveJ = new PosMoveJ();
                    error = "error send robot";
                    return 1;
                }
            }
            catch (Exception ex)
            {
                posMoveJ = new PosMoveJ();
                error = "Error: " + ex.Message;
                return 1;
            }

        }
        public int GetPositionMoveLHans(out string error, out PosMoveL posMoveL)
        {
            try
            {

                string bufdata = ReadActualPos(0);
                string[] arrdata = bufdata.Split(',');
                if ((bufdata != "") && arrdata[0] == "OK")
                {
                    posMoveL = new PosMoveL();
                    posMoveL.X = double.Parse(arrdata[1], System.Globalization.CultureInfo.InvariantCulture);
                    posMoveL.Y = double.Parse(arrdata[2], System.Globalization.CultureInfo.InvariantCulture);
                    posMoveL.Z = double.Parse(arrdata[3], System.Globalization.CultureInfo.InvariantCulture);
                    posMoveL.RX = double.Parse(arrdata[4], System.Globalization.CultureInfo.InvariantCulture);
                    posMoveL.RY = double.Parse(arrdata[5], System.Globalization.CultureInfo.InvariantCulture);
                    posMoveL.RZ = double.Parse(arrdata[6], System.Globalization.CultureInfo.InvariantCulture);
                    error = "0";
                    return 0;
                }
                else
                {
                    posMoveL = new PosMoveL();
                    error = arrdata[0];
                    return 1;
                }

            }
            catch (Exception ex)
            {
                posMoveL = new PosMoveL();
                error = "Error: " + ex.Message;
                return 1;
            }
        }
        public int SetValueTcpJaka(string tcp, int id, out string error)
        {
            try
            {
                string jsonString = "{\"cmdName\":\"set_tool_offsets\",\"tooloffset\":[" + tcp + "],\"id\":" + id + ",\"name\":\"TCP1\"}";
                Command command = JsonConvert.DeserializeObject<Command>(jsonString);
                int sends = tcpsent(jsonString);
                if (sends == 0)
                {
                    string bufdata = subReciveCmdJaka(command.cmdName);
                    Getjointposition command2 = JsonConvert.DeserializeObject<Getjointposition>(bufdata);
                    if ((bufdata != "") && (command2.cmdName == command.cmdName) && command2.errorCode == "0")
                    {
                        error = command2.errorMsg;
                        return 0;
                    }
                    else
                    {
                        error = command2.errorMsg;
                        return 1;
                    }
                }
                else
                {
                    error = "error send robot";
                    return 1;
                }
            }
            catch (Exception ex)
            {
                error = "Error: " + ex.Message;
                return 1;
            }

        }
        public int SelectTcpJaka(int id, out string error)
        {
            try
            {
                string jsonString = "{\"cmdName\":\"set_tool_id\", \"tool_id\":" + id + "}";
                Command command = JsonConvert.DeserializeObject<Command>(jsonString);
                int sends = tcpsent(jsonString);
                if (sends == 0)
                {
                    string bufdata = subReciveCmdJaka(command.cmdName);
                    Getjointposition command2 = JsonConvert.DeserializeObject<Getjointposition>(bufdata);
                    if ((bufdata != "") && (command2.cmdName == command.cmdName) && command2.errorCode == "0")
                    {
                        error = command2.errorMsg;
                        return 0;
                    }
                    else
                    {
                        error = command2.errorMsg;
                        return 1;
                    }
                }
                else
                {
                    error = "error send robot";
                    return 1;
                }
            }
            catch (Exception ex)
            {
                error = "Error: " + ex.Message;
                return 1;
            }

        }
        public int MoveJJaka(PosMoveJ posMoveJ, out string error)
        {
            try
            {
                string jsonString = "{\"cmdName\":\"joint_move\",\"relFlag\":0,\"jointPosition\":[" + posMoveJ.J1 + "," + posMoveJ.J2 + "," + posMoveJ.J3 + "," + posMoveJ.J4 + "," + posMoveJ.J5 + "," + posMoveJ.J6 + "],\"speed\":20.5,\"accel\":20.5}";
                Command command = JsonConvert.DeserializeObject<Command>(jsonString);
                int sends = tcpsent(jsonString);
                if (sends == 0)
                {
                    string bufdata = subReciveCmdJaka(command.cmdName);
                    TcpPosResponse command2 = JsonConvert.DeserializeObject<TcpPosResponse>(bufdata);
                    if ((bufdata != "") && (command2.cmdName == command.cmdName) && command2.errorCode == "0")
                    {
                        error = command2.errorMsg;
                        return 0;
                    }
                    else
                    {
                        error = command2.errorMsg;
                        return 1;
                    }
                }
                else
                {

                    error = "error send robot";
                    return 1;
                }
            }
            catch (Exception ex)
            {

                error = "Error: " + ex.Message;
                return 1;
            }

        }
        public int MoveLJaka(PosMoveL posMoveL, out string error)
        {
            try
            {
                string jsonString = "{\"cmdName\":\"moveL\",\"relFlag\":1,\"cartPosition\":[" + posMoveL.X + "," + posMoveL.Y + "," + posMoveL.Z + "," + posMoveL.RX + "," + posMoveL.RY + "," + posMoveL.RZ + "],\"speed\":20,\"accel\":50,\"tol\":0.5}";
                Command command = JsonConvert.DeserializeObject<Command>(jsonString);
                int sends = tcpsent(jsonString);
                if (sends == 0)
                {
                    string bufdata = subReciveCmdJaka(command.cmdName);
                    TcpPosResponse command2 = JsonConvert.DeserializeObject<TcpPosResponse>(bufdata);
                    if ((bufdata != "") && (command2.cmdName == command.cmdName) && command2.errorCode == "0")
                    {
                        error = command2.errorMsg;
                        return 0;
                    }
                    else
                    {
                        error = command2.errorMsg;
                        return 1;
                    }
                }
                else
                {

                    error = "error send robot";
                    return 1;
                }
            }
            catch (Exception ex)
            {

                error = "Error: " + ex.Message;
                return 1;
            }

        }
        public int SetCurTCPHans(int id, string data)
        {
            int sends = tcpsent("SetCurTCP," + id + "," + data + ";");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("SetCurTCP");
                if ((bufdata != "") && (bufdata.Contains("SetCurTCP")))
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        return 0;
                    }
                    else return int.Parse(arraybuf[2]);
                }
                else
                    return CODE.ERR_NotRecive;
            }
            else
            {
                return CODE.ERR_Connect;
            }
        }
        public int CompleteMoveLJaka(PosMoveL pla)
        {
            bool flag01 = true;
            bool flag02 = true;
            bool flag03 = true;
            //bool flag04 = true;
            //bool flag05 = true;
            bool flag06 = true;
            bool flag2 = true;
            int i = 0;
            int j = 0;
            while (flag2 == true)
            {
                Thread.Sleep(10);
                j++;
                string err;
                PosMoveL posMoveL;
                int er = GetPositionMoveLJaka(out err, out posMoveL);
                if (er == 0)
                {
                    j = 0;
                    if ((posMoveL.X > (pla.X - 0.03)) && (posMoveL.X < (pla.X + 0.03)))
                    {
                        flag01 = true;
                    }
                    else
                    {
                        flag01 = false;
                    }
                    if (posMoveL.Y > (pla.Y - 0.03) && (posMoveL.Y < (pla.Y + 0.03)))
                    {
                        flag02 = true;
                    }
                    else
                    {
                        flag02 = false;
                    }
                    if ((posMoveL.Z > (pla.Z - 0.03)) && (posMoveL.Z < (pla.Z + 0.03)))
                    {
                        flag03 = true;
                    }
                    else
                    {
                        flag03 = false;
                    }
                    //if ((double.Parse(arr[4]) > (pla.RX - 0.03)) && (double.Parse(arr[4]) < (pla.RX + 0.03)))
                    //{
                    //	flag04 = true;
                    //}
                    //else
                    //{
                    //	flag04 = false;
                    //}
                    //if ((double.Parse(arr[5]) > (pla.RY - 0.03)) && (double.Parse(arr[5]) < (pla.RY + 0.03)))
                    //{
                    //	flag05 = true;
                    //}
                    //else
                    //{
                    //	flag05 = false;
                    //}
                    if ((posMoveL.RZ > (pla.RZ - 0.03)) && (posMoveL.RZ < (pla.RZ + 0.03)))
                    {
                        flag06 = true;
                    }
                    else
                    {
                        flag06 = false;
                    }
                }
                else
                {
                    if (j > 5)
                    {
                        return 1;
                    }
                }

                if (flag03 == true && flag01 == true && flag02 == true && flag06 == true)
                {
                    flag2 = false;
                    Thread.Sleep(10);
                    return 0;
                }
                i++;
                if (i > 1500)
                {
                    return 1;
                }

            }
            return 1;
        }
        public int CompleteMoveJJaka(PosMoveJ pla)
        {
            bool flag01 = true;
            bool flag02 = true;
            bool flag03 = true;
            bool flag04 = true;
            bool flag05 = true;
            bool flag06 = true;
            bool flag2 = true;
            int i = 0;
            int j = 0;
            while (flag2 == true)
            {
                j++;
                Thread.Sleep(10);
                string err;
                PosMoveJ posMoveJ;
                int er = GetPositionMoveJJaka(out err, out posMoveJ);
                if (er == 0)
                {
                    j = 0;
                    if (posMoveJ.J1 > (pla.J1 - 0.1) && (posMoveJ.J1 < (pla.J1 + 0.1)))
                    {
                        flag01 = true;
                    }
                    else
                    {
                        flag01 = false;
                    }
                    if (posMoveJ.J2 > (pla.J2 - 0.1) && (posMoveJ.J2 < (pla.J2 + 0.1)))
                    {
                        flag02 = true;
                    }
                    else
                    {
                        flag02 = false;
                    }
                    if ((posMoveJ.J3 > (pla.J3 - 0.1)) && (posMoveJ.J3 < (pla.J3 + 0.1)))
                    {
                        flag03 = true;
                    }
                    else
                    {
                        flag03 = false;
                    }
                    if ((posMoveJ.J4 > (pla.J4 - 0.1)) && (posMoveJ.J4 < (pla.J4 + 0.1)))
                    {
                        flag04 = true;
                    }
                    else
                    {
                        flag04 = false;
                    }
                    if ((posMoveJ.J5 > (pla.J5 - 0.1)) && (posMoveJ.J5 < (pla.J5 + 0.1)))
                    {
                        flag05 = true;
                    }
                    else
                    {
                        flag05 = false;
                    }
                    if ((posMoveJ.J6 > (pla.J6 - 0.1)) && (posMoveJ.J6 < (pla.J6 + 0.1)))
                    {
                        flag06 = true;
                    }
                    else
                    {
                        flag06 = false;
                    }
                }
                else
                {
                    if (j > 5)
                    {
                        return 1;
                    }

                }

                if (flag03 == true && flag01 == true && flag02 == true && flag04 == true && flag05 == true && flag06 == true)
                {
                    flag2 = false;
                    Thread.Sleep(50);
                    return 0;
                }
                i++;
                if (i > 1700)
                {
                    return 1;
                }

            }
            return 1;
        }
        public int Electrify()
        {
            int sends = tcpsent("Electrify,;");
            if (sends == 0)
            {
                string bufdata = subRecive();
                if ((bufdata != "") && (bufdata.Contains("Electrify")))
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK") return 0;
                    else return int.Parse(arraybuf[2]);
                }
                else
                    return CODE.ERR_NotRecive;
            }
            else
            {
                return CODE.ERR_Connect;
            }
        }

        public int BlackOut()
        {
            int sends = tcpsent("BlackOut,;");
            if (sends == 0)
            {
                string bufdata = subRecive();
                if ((bufdata != "") && (bufdata.Contains("BlackOut")))
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK") return 0;
                    else return int.Parse(arraybuf[2]);
                }
                else
                    return CODE.ERR_NotRecive;
            }
            else
            {
                return CODE.ERR_Connect;
            }
        }

        public int StartMaster(int rbtID)
        {
            int sends = tcpsent("StartMaster,;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("StartMaster");
                if ((bufdata != "") && (bufdata.Contains("StartMaster")))
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        return 0;
                    }
                    else return int.Parse(arraybuf[2]);
                }
                else
                    return CODE.ERR_NotRecive;
            }
            else
            {
                return CODE.ERR_Connect;
            }
        }
        public int CloseMaster()
        {
            int sends = tcpsent("CloseMaster,;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("CloseMaster");
                if ((bufdata != "") && (bufdata.Contains("CloseMaster")))
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK") return 0;
                    else return int.Parse(arraybuf[2]);
                }
                else
                    return CODE.ERR_NotRecive;
            }
            else
            {
                return CODE.ERR_Connect;
            }
        }
        public int GrpDisable()
        {
            int sends = tcpsent("GrpDisable,0;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("GrpDisable");
                if ((bufdata != "") && (bufdata.Contains("GrpDisable")))
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK") return 0;
                    else return int.Parse(arraybuf[2]);
                }
                else
                    return CODE.ERR_NotRecive;
            }
            else
            {
                return CODE.ERR_Connect;
            }
        }
       
        public int OSCmd()
        {
            //	for(int i = 0; i < 10; i++)
            {
                int sends = tcpsent("OSCmd,1;");
                if (sends == 0)
                {
                    string bufdata = subReciveCmd("OSCmd");
                    if ((bufdata != "") && (bufdata.Contains("OSCmd")))
                    {
                        string[] arraybuf = bufdata.Split(',');
                        if (arraybuf[1] == "OK") return 0;
                        else return int.Parse(arraybuf[2]);
                    }
                    else
                    {
                        return CODE.ERR_NotRecive;

                    }

                }
                else
                {

                    return CODE.ERR_Connect;


                }
            }
            return CODE.ERR_Connect;
        }

        public int GrpPowerOn(int rbtID)
        {
            int sends = tcpsent("GrpPowerOn," + rbtID + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("GrpPowerOn");
                if ((bufdata != "") && (bufdata.Contains("GrpPowerOn")))
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK") return 0;
                    else return int.Parse(arraybuf[2]);
                }
                else
                    return CODE.ERR_NotRecive;
            }
            else
            {
                return CODE.ERR_Connect;
            }
        }
        public string EnableRobot(int rbtID)
        {
            // Gọi hàm có sẵn GrpPowerOn
            int result = GrpPowerOn(rbtID);
            if (result == 0) return "OK";
            return "FAIL";
        }
        public int GrpPowerOff(int rbtID)
        {
            int sends = tcpsent("GrpPowerOff," + rbtID + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("GrpPowerOff");
                if ((bufdata != "") && (bufdata.Contains("GrpPowerOff")))
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK") return 0;
                    else return int.Parse(arraybuf[2]);
                }
                else
                    return CODE.ERR_NotRecive;
            }
            else
            {
                return CODE.ERR_Connect;
            }
        }
        public string DisableRobot(int rbtID)
        {
            // Gọi hàm có sẵn GrpPowerOff
            int result = GrpPowerOff(rbtID);
            if (result == 0) return "OK";
            return "FAIL";
        }
        public int GrpStop(int rbtID)
        {
            int sends = tcpsent("GrpStop," + rbtID + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("GrpStop");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK") return 0;
                    else return int.Parse(arraybuf[2]);
                }
                else
                    return CODE.ERR_NotRecive;
            }
            else
            {
                return CODE.ERR_Connect;
            }
        }

        public int GrpReset(int rbtID)
        {
            int sends = tcpsent("GrpReset," + rbtID + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("GrpReset");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK") return 0;
                    else return int.Parse(arraybuf[2]);
                }
                else
                    return CODE.ERR_NotRecive;
            }
            else
            {
                return CODE.ERR_Connect;
            }
        }
        public string ReadRobotState(int rbtID, out int[] data)
        {
            string Pos = "";
            int[] da = new int[15];
            //int sends = tcpsent("StartPushMovePath, " + rbtID + "," + pla.X + "," + pla.Y + "," + pla.Z + "," + pla.RX + "," + pla.RY + "," + pla.RZ + "," + vt + "," + Radius + ",;");
            int sends = tcpsent("ReadRobotState," + rbtID + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("ReadRobotState");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        int j = 0;
                        for (int i = 2; i < 15; i++)
                        {
                            da[j] = int.Parse(arraybuf[i]);
                            j++;
                        }
                        data = da;
                        return CODE.OK;
                    }
                    else
                    {
                        data = null;
                        return CODE.SUBRECIVE_FAIL;
                    }

                }
                else
                {
                    data = null;
                    return CODE.SUBRECIVE_FAIL;
                }

            }
            else
            {
                data = null;
                return CODE.CONNECT_FAIL;
            }
        }
        public int GrpInterrupt(int rbtID)
        {
            int sends = tcpsent("GrpInterrupt," + rbtID + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("GrpInterrupt");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK") return 0;
                    else return int.Parse(arraybuf[2]);
                }
                else
                    return CODE.ERR_NotRecive;
            }
            else
            {
                return CODE.ERR_Connect;
            }
        }
        public int GrpContinue(int rbtID)
        {
            int sends = tcpsent("GrpContinue," + rbtID + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("GrpContinue");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK") return 0;
                    else return int.Parse(arraybuf[2]);
                }
                else
                    return CODE.ERR_NotRecive;
            }
            else
            {
                return CODE.ERR_Connect;
            }
        }
        public int GrpOpenFreeDriver(int rbtID)
        {
            int sends = tcpsent("GrpOpenFreeDriver," + rbtID + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("GrpOpenFreeDriver");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK") return 0;
                    else return int.Parse(arraybuf[2]);
                }
                else
                    return CODE.ERR_NotRecive;
            }
            else
            {
                return CODE.ERR_Connect;
            }
        }
        public string OpenFreeDriver(int rbtID)
        {
            // Gọi hàm có sẵn GrpOpenFreeDriver
            int result = GrpOpenFreeDriver(rbtID);
            if (result == 0) return "OK";
            return "FAIL";
        }
        public int GrpCloseFreeDriver(int rbtID)
        {
            int sends = tcpsent("GrpCloseFreeDriver," + rbtID + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("GrpCloseFreeDriver");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK") return 0;
                    else return int.Parse(arraybuf[2]);
                }
                else
                    return CODE.ERR_NotRecive;
            }
            else
            {
                return CODE.ERR_Connect;
            }
        }
        public string CloseFreeDriver(int rbtID)
        {
            // Gọi hàm có sẵn GrpCloseFreeDriver
            int result = GrpCloseFreeDriver(rbtID);
            if (result == 0) return "OK";
            return "FAIL";
        }
        public string StartAssistiveMode(int rbtID)
        {
            string Pos = "";
            int sends = tcpsent("StartAssistiveMode," + rbtID + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("StartAssistiveMode");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        return Pos = arraybuf[1];
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }

        public string CloseAssistiveMode(int rbtID)
        {
            string Pos = "";
            int sends = tcpsent("CloseAssistiveMode," + rbtID + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("CloseAssistiveMode");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        return Pos = arraybuf[1];
                    }
                    else
                        return arraybuf[1] + "," + arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }

        public string ReadActualPos(int rbtID)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    string Pos = "";
                    int sends = tcpsent("ReadActPos," + rbtID + ",;");
                    if (sends == 0)
                    {
                        string bufdata = subReciveCmd("ReadActPos");
                        if ((bufdata != "") && (bufdata.Contains("ReadActPos")))
                        {
                            string[] arraybuf = bufdata.Split(',');
                            if (arraybuf[1] == "OK")
                            {
                                return Pos = arraybuf[1] + "," + arraybuf[8] + "," + arraybuf[9] + "," + arraybuf[10] + "," + arraybuf[11] + "," + arraybuf[12] + "," + arraybuf[13]
                                    + "," + arraybuf[2] + "," + arraybuf[3] + "," + arraybuf[4] + "," + arraybuf[5] + "," + arraybuf[6] + "," + arraybuf[7];
                            }
                            else
                                return arraybuf[2];
                        }
                        else
                            return CODE.SUBRECIVE_FAIL;
                    }
                    else
                    {
                        return CODE.CONNECT_FAIL;
                    }
                }
                catch
                {

                }
            }
            return CODE.CONNECT_FAIL;
        }
        public string ReadActualPosMoveL(int rbtID,out PosMoveL moveL)
        {
            PosMoveL moveL1 = new PosMoveL();
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    string Pos = "";
                    int sends = tcpsent("ReadActPos," + rbtID + ",;");
                    if (sends == 0)
                    {
                        string bufdata = subReciveCmd("ReadActPos");
                        if ((bufdata != "") && (bufdata.Contains("ReadActPos")))
                        {
                            string[] arraybuf = bufdata.Split(',');
                            if (arraybuf[1] == "OK")
                            {
                                moveL1.X = double.Parse(arraybuf[8], System.Globalization.CultureInfo.InvariantCulture);
                                moveL1.Y = double.Parse(arraybuf[9], System.Globalization.CultureInfo.InvariantCulture);
                                moveL1.Z = double.Parse(arraybuf[10], System.Globalization.CultureInfo.InvariantCulture);
                                moveL1.RX = double.Parse(arraybuf[11], System.Globalization.CultureInfo.InvariantCulture);
                                moveL1.RY = double.Parse(arraybuf[12], System.Globalization.CultureInfo.InvariantCulture);
                                moveL1.RZ = double.Parse(arraybuf[13], System.Globalization.CultureInfo.InvariantCulture);
                                moveL = moveL1;
                                return Pos = arraybuf[1];
                             }
                            else  
                            {
                                moveL = null;
                                return arraybuf[2];

                            }
                              
                        }
                        else
                        {
                            moveL = null;
                            return CODE.SUBRECIVE_FAIL;

                        }
                       
                    }
                    else
                    {
                        moveL = null;
                        return CODE.CONNECT_FAIL;
                    }
                }
                catch
                {

                }
            }
            moveL = null;
            return CODE.CONNECT_FAIL;
        }

        public string SetEndDO(int rbtID, int bit, int state)
        {
            for (int i = 0; i < 5; i++)
            {
                int sends = tcpsent("SetEndDO," + rbtID + "," + bit + "," + state + ",;");
                if (sends == 0)
                {
                    string bufdata = subReciveCmd("SetEndDO");
                    if (bufdata != "")
                    {
                        string[] arraybuf = bufdata.Split(',');
                        if (arraybuf[1] == "OK")
                        {
                            return arraybuf[1];
                        }
                        else
                        {
                            if (i > 1)
                            {
                                return arraybuf[2];
                            }
                        }
                    }
                    else
                    {
                        if (i > 4)
                        {
                            return CODE.CONNECT_FAIL;
                        }
                    }
                }
                else
                {
                    if (i > 4)
                    {
                        return CODE.CONNECT_FAIL;
                    }
                }
            }
            return CODE.CONNECT_FAIL;
        }
        public string ReadEO(int rbtID, int bit, out int value)
        {
            value = 0;
            for (int i = 0; i < 10; i++)
            {

                int sends = tcpsent("ReadEO," + rbtID + ',' + bit + ",;");
                if (sends == 0)
                {
                    string bufdata = subReciveCmd("ReadEO");
                    if (bufdata != "")
                    {
                        string[] arraybuf = bufdata.Split(',');
                        if (arraybuf[1] == "OK")
                        {
                            value = int.Parse(arraybuf[2]);
                            return arraybuf[1];
                        }
                        else
                        {
                            if (i > 1)
                            {
                                return arraybuf[2];
                            }
                        }

                    }
                    else
                    {
                        if (i > 1)
                        {
                            return CODE.SUBRECIVE_FAIL;
                        }

                    }
                }
                else
                {
                    if (i > 5)
                    {
                        return CODE.SUBRECIVE_FAIL;
                    }

                }
                Thread.Sleep(10);
            }
            return CODE.SUBRECIVE_FAIL;
        }
        public string SetSerialDO(int bit, int state)
        {
            int sends = tcpsent("SetBoxDO," + bit + "," + state + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("SetBoxDO");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        return arraybuf[1];
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }
        public string SetBoxCO(int bit, int state)
        {
            int sends = tcpsent("SetBoxCO," + bit + "," + state + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("SetBoxCO");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        return arraybuf[1];
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }
        public string ReadBoxCO(int bit, ref int value)
        {
            value = 0;
            for (int i = 0; i < 10; i++)
            {

                int sends = tcpsent("ReadBoxCO," + bit + ",;");
                if (sends == 0)
                {
                    string bufdata = subReciveCmd("ReadBoxCO");
                    if (bufdata != "")
                    {
                        string[] arraybuf = bufdata.Split(',');
                        if (arraybuf[1] == "OK")
                        {
                            value = int.Parse(arraybuf[2]);
                            //       if (value == 1)
                            {
                                return arraybuf[1];
                            }
                        }
                        else
                            return arraybuf[2];
                    }
                    else
                        return CODE.SUBRECIVE_FAIL;

                }
                else
                {
                    return CODE.SUBRECIVE_FAIL;
                }
                Thread.Sleep(10);
            }
            return CODE.SUBRECIVE_FAIL;
        }
        public string ReadBoxDO(int bit, ref int value)
        {
            value = 0;
            for (int i = 0; i < 10; i++)
            {

                int sends = tcpsent("ReadBoxDO," + bit + ",;");
                if (sends == 0)
                {
                    string bufdata = subReciveCmd("ReadBoxDO");
                    if (bufdata != "")
                    {
                        string[] arraybuf = bufdata.Split(',');
                        if (arraybuf[1] == "OK")
                        {
                            value = int.Parse(arraybuf[2]);
                            //if (value == 1)
                            //{
                            return arraybuf[1];
                            //}
                        }
                        else
                            return arraybuf[2];
                    }
                    else
                        return CODE.SUBRECIVE_FAIL;

                }
                else
                {
                    return CODE.SUBRECIVE_FAIL;
                }

            }
            return CODE.SUBRECIVE_FAIL;
        }
        public string ReadBoxDO_01234567(out int[] value)
        {
            value = new int[8];
            try
            {
                //  for (int i = 0; i < 30; i++)
                {

                    int sends = tcpsent("ReadBoxDO," + "0,1,2,3,4,5,6,7" + ",;");
                    if (sends == 0)
                    {
                        string bufdata = subReciveCmd("ReadBoxDO");
                        if (bufdata != "")
                        {
                            string[] arraybuf = bufdata.Split(',');
                            if (arraybuf[1] == "OK")
                            {
                                value[0] = int.Parse(arraybuf[2]);
                                value[1] = int.Parse(arraybuf[3]);
                                value[2] = int.Parse(arraybuf[4]);
                                value[3] = int.Parse(arraybuf[5]);
                                value[4] = int.Parse(arraybuf[6]);
                                value[5] = int.Parse(arraybuf[7]);
                                value[6] = int.Parse(arraybuf[8]);
                                value[7] = int.Parse(arraybuf[9]);
                                return arraybuf[1];
                            }
                            else
                            {
                                return arraybuf[2];
                            }
                        }
                        else
                            return CODE.SUBRECIVE_FAIL;

                    }
                    else
                    {
                        return CODE.SUBRECIVE_FAIL;
                    }
                }
            }
            catch
            {
            }
            return CODE.SUBRECIVE_FAIL;
        }
        public string ReadBoxCO_01234567(out int[] value)
        {
            value = new int[8];
            try
            {
                //  for (int i = 0; i < 30; i++)
                {

                    int sends = tcpsent("ReadBoxCO," + "0,1,2,3,4,5,6,7" + ",;");
                    if (sends == 0)
                    {
                        string bufdata = subReciveCmd("ReadBoxCO");
                        if (bufdata != "")
                        {
                            string[] arraybuf = bufdata.Split(',');
                            if (arraybuf[1] == "OK")
                            {
                                value[0] = int.Parse(arraybuf[2]);
                                value[1] = int.Parse(arraybuf[3]);
                                value[2] = int.Parse(arraybuf[4]);
                                value[3] = int.Parse(arraybuf[5]);
                                value[4] = int.Parse(arraybuf[6]);
                                value[5] = int.Parse(arraybuf[7]);
                                value[6] = int.Parse(arraybuf[8]);
                                value[7] = int.Parse(arraybuf[9]);
                                return arraybuf[1];
                            }
                            else
                            {
                                return arraybuf[2];
                            }
                        }
                        else
                            return CODE.SUBRECIVE_FAIL;

                    }
                    else
                    {
                        return CODE.SUBRECIVE_FAIL;
                    }
                }
            }
            catch
            {
            }
            return CODE.SUBRECIVE_FAIL;
        }
        public string ReadBoxDI_01234567(out int[] value)
        {
            value = new int[8];
            try
            {
                //  for (int i = 0; i < 30; i++)
                {

                    int sends = tcpsent("ReadBoxDI," + "0,1,2,3,4,5,6,7" + ",;");
                    if (sends == 0)
                    {
                        string bufdata = subReciveCmd("ReadBoxDI");
                        if (bufdata != "")
                        {
                            string[] arraybuf = bufdata.Split(',');
                            if (arraybuf[1] == "OK")
                            {
                                value[0] = int.Parse(arraybuf[2]);
                                value[1] = int.Parse(arraybuf[3]);
                                value[2] = int.Parse(arraybuf[4]);
                                value[3] = int.Parse(arraybuf[5]);
                                value[4] = int.Parse(arraybuf[6]);
                                value[5] = int.Parse(arraybuf[7]);
                                value[6] = int.Parse(arraybuf[8]);
                                value[7] = int.Parse(arraybuf[9]);
                                return arraybuf[1];
                            }
                            else
                            {
                                return arraybuf[2];
                            }
                        }
                        else
                            return CODE.SUBRECIVE_FAIL;

                    }
                    else
                    {
                        return CODE.SUBRECIVE_FAIL;
                    }
                    Thread.Sleep(10);
                }
            }
            catch
            {
            }
            return CODE.SUBRECIVE_FAIL;
        }
        public string ReadBoxCI_01234567(out int[] value)
        {
            value = new int[8];
            try
            {
                //  for (int i = 0; i < 30; i++)
                {

                    int sends = tcpsent("ReadBoxCI," + "0,1,2,3,4,5,6,7" + ",;");
                    if (sends == 0)
                    {
                        string bufdata = subReciveCmd("ReadBoxCI");
                        if (bufdata != "")
                        {
                            string[] arraybuf = bufdata.Split(',');
                            if (arraybuf[1] == "OK")
                            {
                                value[0] = int.Parse(arraybuf[2]);
                                value[1] = int.Parse(arraybuf[3]);
                                value[2] = int.Parse(arraybuf[4]);
                                value[3] = int.Parse(arraybuf[5]);
                                value[4] = int.Parse(arraybuf[6]);
                                value[5] = int.Parse(arraybuf[7]);
                                value[6] = int.Parse(arraybuf[8]);
                                value[7] = int.Parse(arraybuf[9]);
                                return arraybuf[1];
                            }
                            else
                            {
                                return arraybuf[2];
                            }
                        }
                        else
                            return CODE.SUBRECIVE_FAIL;

                    }
                    else
                    {
                        return CODE.SUBRECIVE_FAIL;
                    }
                    Thread.Sleep(10);
                }
            }
            catch
            {
            }
            return CODE.SUBRECIVE_FAIL;
        }
        public string ReadBoxDI(int bit, ref int value)
        {
            value = 0;
            for (int i = 0; i < 10; i++)
            {
                value = 0;
                try
                {
                    int sends = tcpsent("ReadBoxDI," + bit + ",;");
                    if (sends == 0)
                    {
                        string bufdata = subReciveCmd("ReadBoxDI");
                        if (bufdata != "")
                        {
                            string[] arraybuf = bufdata.Split(',');
                            if (arraybuf[1] == "OK")
                            {
                                value = int.Parse(arraybuf[2]);
                                return arraybuf[1];
                            }
                            else
                            {
                                if (i > 5)
                                    return arraybuf[2];
                            }
                        }
                        else
                        {
                            if (i > 5)
                                return CODE.SUBRECIVE_FAIL;
                        }

                    }
                    else
                    {
                        if (i > 4)
                            return CODE.SUBRECIVE_FAIL;
                    }
                    Thread.Sleep(10);

                }
                catch
                {
                    if (i > 4)
                        return CODE.SUBRECIVE_FAIL;
                }
            }
            return CODE.SUBRECIVE_FAIL;
        }
        public string ReadBoxDI_1(int bit, ref int value, int ctime)
        {
            value = 0;
            int j = 0;
            try
            {
                for (int i = 0; i < ctime; i++)
                {

                    int sends = tcpsent("ReadBoxDI," + bit + ",;");
                    if (sends == 0)
                    {
                        string bufdata = subReciveCmd("ReadBoxDI");
                        if (bufdata != "")
                        {
                            string[] arraybuf = bufdata.Split(',');
                            value = int.Parse(arraybuf[2]);
                            j = 0;
                            if (arraybuf[1] == "OK" && value == 1)
                            {

                                return arraybuf[1];
                            }
                        }
                        else
                        {
                            j++;
                            if (j > 10)
                            {
                                return CODE.SUBRECIVE_FAIL;
                            }

                        }


                    }
                    else
                    {
                        j++;
                        if (j > 4)
                        {
                            return CODE.SUBRECIVE_FAIL;
                        }
                    }
                    Thread.Sleep(10);
                }
            }
            catch
            {
                j++;
                if (j > 10)
                {
                    return CODE.SUBRECIVE_FAIL;
                }
            }
            return CODE.SUBRECIVE_FAIL;
        }
        public string ReadBoxDI_0(int bit, ref int value, int ctime)
        {
            value = 2;
            int j = 0;
            try
            {
                for (int i = 0; i < ctime; i++)
                {

                    int sends = tcpsent("ReadBoxDI," + bit + ",;");
                    if (sends == 0)
                    {
                        string bufdata = subReciveCmd("ReadBoxDI");
                        if (bufdata != "")
                        {
                            string[] arraybuf = bufdata.Split(',');
                            value = int.Parse(arraybuf[2]);
                            j = 0;
                            if (arraybuf[1] == "OK" && value == 0)
                            {

                                return arraybuf[1];
                            }
                        }
                        else
                        {

                            j++;
                            if (j > 10)
                            {
                                return CODE.SUBRECIVE_FAIL;
                            }
                        }

                    }
                    else
                    {

                        j++;
                        if (j > 4)
                        {
                            return CODE.SUBRECIVE_FAIL;
                        }
                    }
                    Thread.Sleep(10);
                }
            }
            catch
            {
                j++;
                if (j > 10)
                {
                    return CODE.SUBRECIVE_FAIL;
                }
            }
            return CODE.SUBRECIVE_FAIL;
        }
        public string ReadBoxCI(int bit, ref int value)
        {
            value = 0;
            //   for (int i = 0; i < 30; i++)
            {
                try
                {

                    int sends = tcpsent("ReadBoxCI," + bit + ",;");
                    if (sends == 0)
                    {
                        string bufdata = subReciveCmd("ReadBoxCI");
                        if (bufdata != "")
                        {
                            string[] arraybuf = bufdata.Split(',');
                            if (arraybuf[1] == "OK")
                            {
                                value = int.Parse(arraybuf[2]);
                                return arraybuf[1];
                            }
                            else
                            {
                                return arraybuf[2];
                            }
                        }
                        else
                            return CODE.SUBRECIVE_FAIL;

                    }
                    else
                    {
                        return CODE.SUBRECIVE_FAIL;
                    }
                    Thread.Sleep(10);
                }
                catch
                {

                    return CODE.SUBRECIVE_FAIL;

                }

            }
            return CODE.SUBRECIVE_FAIL;
        }
        public string ReadBoxCI_1(int bit, ref int value, int ctime)
        {
            value = 0;
            int j = 0;
            for (int i = 0; i < ctime; i++)
            {
                try
                {
                    int sends = tcpsent("ReadBoxCI," + bit + ",;");
                    if (sends == 0)
                    {
                        string bufdata = subReciveCmd("ReadBoxCI");
                        if (bufdata != "")
                        {
                            string[] arraybuf = bufdata.Split(',');
                            value = int.Parse(arraybuf[2]);
                            j = 0;
                            if (arraybuf[1] == "OK" && value == 1)
                            {
                                return arraybuf[1];
                            }

                        }
                        else
                        {
                            j++;
                            if (j > 10)
                            {
                                return CODE.SUBRECIVE_FAIL;
                            }
                        }

                    }
                    else
                    {
                        j++;
                        if (j > 4)
                        {
                            return CODE.SUBRECIVE_FAIL;
                        }
                    }
                    Thread.Sleep(10);
                }
                catch
                {
                    j++;
                    if (j > 10)
                    {
                        return CODE.SUBRECIVE_FAIL;
                    }
                }

            }
            return CODE.SUBRECIVE_FAIL;
        }
        public string ReadBoxCI_0(int bit, ref int value, int ctime)
        {
            value = 2;
            int j = 0;
            for (int i = 0; i < ctime; i++)
            {
                try
                {
                    int sends = tcpsent("ReadBoxCI," + bit + ",;");
                    if (sends == 0)
                    {
                        string bufdata = subReciveCmd("ReadBoxCI");
                        if (bufdata != "")
                        {
                            string[] arraybuf = bufdata.Split(',');
                            value = int.Parse(arraybuf[2]);
                            j = 0;
                            if (arraybuf[1] == "OK" && value == 0)
                            {
                                return arraybuf[1];
                            }

                        }
                        else
                        {
                            j++;
                            if (j > 10)
                            {
                                return CODE.SUBRECIVE_FAIL;
                            }
                        }

                    }
                    else
                    {
                        j++;
                        if (j > 4)
                        {
                            return CODE.SUBRECIVE_FAIL;
                        }
                    }
                    Thread.Sleep(10);
                }
                catch
                {
                    j++;
                    if (j > 10)
                    {
                        return CODE.SUBRECIVE_FAIL;
                    }
                }

            }
            return CODE.SUBRECIVE_FAIL;
        }
        public string ReadSerialDI_1(int state, ref int value)
        {
            value = 0;
            for (int i = 0; i < 10; i++)
            {

                int sends = tcpsent("ReadBoxDI," + state + ",;");
                if (sends == 0)
                {
                    string bufdata = subReciveCmd("ReadBoxDI");
                    if (bufdata != "")
                    {
                        string[] arraybuf = bufdata.Split(',');
                        if (arraybuf[1] == "OK")
                        {
                            value = int.Parse(arraybuf[2]);
                            if (value == 1)
                            {
                                return arraybuf[1];
                            }
                        }
                        else
                            return arraybuf[2];
                    }
                    else
                        return CODE.SUBRECIVE_FAIL;

                }
                else
                {
                    return CODE.SUBRECIVE_FAIL;
                }
                Thread.Sleep(10);
            }
            return CODE.SUBRECIVE_FAIL;
        }
        public string ReadSerialDI_0(int state, ref int value)
        {
            value = 1;
            for (int i = 0; i < 100; i++)
            {
                int sends = tcpsent("ReadBoxDI," + state + ",;");
                if (sends == 0)
                {
                    string bufdata = subReciveCmd("ReadBoxDI");
                    if (bufdata != "")
                    {
                        string[] arraybuf = bufdata.Split(',');
                        if (arraybuf[1] == "OK")
                        {
                            value = int.Parse(arraybuf[2]);
                            if (value == 0)
                            {
                                return arraybuf[1];
                            }
                        }
                        else
                            return arraybuf[2];
                    }
                    else
                        return CODE.SUBRECIVE_FAIL;

                }
                else
                {
                    return CODE.SUBRECIVE_FAIL;
                }
                Thread.Sleep(10);
            }
            return CODE.SUBRECIVE_FAIL;
        }
        public string MoveJ_CheckJ(int rbtID, PosMoveJ Jpos)
        {
            if ((Jpos.J1 > 126 && Jpos.J1 < 220) && (Jpos.J2 > -90 && Jpos.J2 < 40) && (Jpos.J3 > 30 && Jpos.J3 < 145) && (Jpos.J4 > -2 && Jpos.J4 < 4) && (Jpos.J5 > 25 && Jpos.J5 < 80) && (Jpos.J6 > 80 && Jpos.J6 < 230))
            {

            }
            else
            {
                return "Error Nằm ngoài dịch chuyển vùng an toàn";
            }
            string Jposition = "," + Jpos.J1 + "," + Jpos.J2 + "," + Jpos.J3 + "," + Jpos.J4 + "," + Jpos.J5 + "," + Jpos.J6;
            int sends = tcpsent("MoveJ," + rbtID + Jposition + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("MoveJ");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        int complete = CompleteMoveJ(Jpos);
                        if (complete == 0)
                        {
                            return CODE.OK;
                        }
                        else
                        {
                            return CODE.FAIL;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }
        public string MoveJ(int rbtID, PosMoveJ Jpos)
        {
            string Jposition = "," + Jpos.J1 + "," + Jpos.J2 + "," + Jpos.J3 + "," + Jpos.J4 + "," + Jpos.J5 + "," + Jpos.J6;
            int sends = tcpsent("MoveJ," + rbtID + Jposition + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("MoveJ");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        int complete = CompleteMoveJ(Jpos);
                        if (complete == 0)
                        {
                            return CODE.OK;
                        }
                        else
                        {
                            return CODE.FAIL;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }
        public string MoveJ_NoComplete(int rbtID, PosMoveJ Jpos)
        {
            string Jposition = "," + Jpos.J1 + "," + Jpos.J2 + "," + Jpos.J3 + "," + Jpos.J4 + "," + Jpos.J5 + "," + Jpos.J6;
            int sends = tcpsent("MoveJ," + rbtID + Jposition + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("MoveJ");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        //int complete = CompleteMoveJ(Jpos);
                        //if (complete == 0)
                        //{
                        return CODE.OK;
                        //}
                        //else
                        //{
                        //    return CODE.FAIL;
                        // }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }
        public string MoveL_CheckXYZ(int rbtID, PosMoveL Jpos, int err)
        {
            if (Jpos.Z > 50 && Jpos.Z < 300 && Jpos.X > -450 && Jpos.X < 150 && Jpos.Y > -12 && Jpos.Y < 667)
            {

            }
            else
            {
                return "Error Nằm ngoài dịch chuyển vùng an toàn";
            }
            if ((Jpos.RX < -160 || Jpos.RX > 160) && (Jpos.RY > -30 && Jpos.RY < 30) && (Jpos.RZ < -140 || Jpos.RZ > 170))
            {

            }
            else
            {
                return "Error Nằm ngoài dịch chuyển vùng an toàn";
            }
            string Jposition;//= string.Concat(new object[]{ ",",Jpos.X,",",Jpos.Y,",",Jpos.Z, ",",Jpos.RX,",",Jpos.RY,",",Jpos.RZ});
            if (err == 0)
            {
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ });
            }
            else if (err == 1)
            {
                Jpos.Z = Jpos.Z - 8;
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ });
            }
            else if (err == 2)
            {
                Jpos.Z = Jpos.Z - 8;
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ });
            }
            else if (err == 3)
            {
                Jpos.Z = Jpos.Z - 8;
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ });
            }
            else
            {
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ });
            }
            int sends = this.tcpsent(string.Concat(new object[] { "MoveL,", rbtID, Jposition, ",;" }));
            bool flag = sends == 0;
            string result;
            if (flag)
            {
                string bufdata = this.subReciveCmd("MoveL");
                bool flag2 = bufdata != "";
                if (flag2)
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        int complete = CompleteXYZ(Jpos);
                        if (complete == 0)
                        {
                            // Thread.Sleep(100);
                            return CODE.OK;
                        }
                        else
                        {
                            return CODE.FAIL;
                        }
                        //string AxisCompleted = this.WaitMotionFinish();
                        //if (AxisCompleted == "0") {
                        //    result = arraybuf[1];
                        //}
                        //else {
                        //    result = AxisCompleted;
                        //}
                    }
                    else
                    {
                        result = arraybuf[2];
                    }
                }
                else
                {
                    result = CODE.SUBRECIVE_FAIL;
                }
            }
            else
            {
                result = CODE.CONNECT_FAIL;
            }
            return result;
        }
        public string MoveL(int rbtID, PosMoveL Jpos, int err)
        {
            string Jposition;//= string.Concat(new object[]{ ",",Jpos.X,",",Jpos.Y,",",Jpos.Z, ",",Jpos.RX,",",Jpos.RY,",",Jpos.RZ});
            if (err == 0)
            {
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ });
            }
            else if (err == 1)
            {
                Jpos.Z = Jpos.Z - 8;
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ });
            }
            else if (err == 2)
            {
                Jpos.Z = Jpos.Z - 8;
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ });
            }
            else if (err == 3)
            {
                Jpos.Z = Jpos.Z - 8;
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ });
            }
            else
            {
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ });
            }
            int sends = this.tcpsent(string.Concat(new object[] { "MoveL,", rbtID, Jposition, ",;" }));
            bool flag = sends == 0;
            string result;
            if (flag)
            {
                string bufdata = this.subReciveCmd("MoveL");
                bool flag2 = bufdata != "";
                if (flag2)
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        int complete = CompleteXYZ(Jpos);
                        if (complete == 0)
                        {
                            // Thread.Sleep(100);
                            return CODE.OK;
                        }
                        else
                        {
                            return CODE.FAIL;
                        }
                        //string AxisCompleted = this.WaitMotionFinish();
                        //if (AxisCompleted == "0") {
                        //    result = arraybuf[1];
                        //}
                        //else {
                        //    result = AxisCompleted;
                        //}
                    }
                    else
                    {
                        result = arraybuf[2];
                    }
                }
                else
                {
                    result = CODE.SUBRECIVE_FAIL;
                }
            }
            else
            {
                result = CODE.CONNECT_FAIL;
            }
            return result;
        }
        public string MoveL_NoComplete(int rbtID, PosMoveL Jpos, int err)
        {
            string Jposition;//= string.Concat(new object[]{ ",",Jpos.X,",",Jpos.Y,",",Jpos.Z, ",",Jpos.RX,",",Jpos.RY,",",Jpos.RZ});
            if (err == 0)
            {
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ });
            }
            else if (err == 1)
            {
                Jpos.Z = Jpos.Z - 10;
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ });
            }
            else if (err == 2)
            {
                Jpos.Z = Jpos.Z - 10;
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ });
            }
            else if (err == 3)
            {
                Jpos.Z = Jpos.Z - 10;
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ });
            }
            else
            {
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ });
            }
            int sends = this.tcpsent(string.Concat(new object[] { "MoveL,", rbtID, Jposition, ",;" }));
            bool flag = sends == 0;
            string result;
            if (flag)
            {
                string bufdata = this.subReciveCmd("MoveL");
                bool flag2 = bufdata != "";
                if (flag2)
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        return CODE.OK;
                        //int complete = CompleteXYZ(Jpos);
                        //if (complete == 0)
                        //{
                        //    // Thread.Sleep(100);
                        //    return CODE.OK;
                        //}
                        //else
                        //{
                        // return CODE.FAIL;
                        //}
                    }
                    else
                    {
                        result = arraybuf[2];
                    }
                }
                else
                {
                    result = CODE.SUBRECIVE_FAIL;
                }
            }
            else
            {
                result = CODE.CONNECT_FAIL;
            }
            return result;
        }

        public string MoveHoming(int rbtID)
        {
            string Pos = "";
            int sends = tcpsent("MoveHoming," + rbtID + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("MoveHoming");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            return AxisCompleted;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }

        public string WaitMotionFinish()
        {
            string[] arraybuf = new string[3];
            int count = 0;
            while (true)
            {
                Thread.Sleep(50);
                try
                {
                    // 1. Send get robot motion state message: ReadMoveState,;
                    int sends = tcpsent("ReadRobotState,0,;");
                    if (sends == 0)
                    {
                        //Thread.Sleep(20);
                        string bufdata = subReciveCmd("ReadRobotState");
                        if (bufdata != "")
                        {
                            arraybuf = bufdata.Split(',');
                        }
                    }
                    else
                    {
                        return CODE.FAIL;
                    }

                    // 2. Parsing the returned motion state: MoveState if(MoveState == 1009)
                    if (arraybuf[2] == "1009")
                    {
                        // In motion, wait for 10 milliseconds, query again 
                        Thread.Sleep(20);
                    }
                    else if (arraybuf[2] == "0")
                    {
                        // The movement is completed and is returned directly to
                        return arraybuf[2];
                    }
                    else
                    {
                        // Other error conditions are dealt with separately 
                        return CODE.FAIL;
                    }
                    if (count > 400)
                    {
                        return CODE.FAIL;
                    }
                    count++;
                }
                catch
                {
                    count++;
                }

            }
            return CODE.CONNECT_FAIL;
        }

        public string MoveBEX(int rbtID, PosMoveL Jpos, int err, double vt)
        {
            string Jposition;//= string.Concat(new object[]{ ",",Jpos.X,",",Jpos.Y,",",Jpos.Z, ",",Jpos.RX,",",Jpos.RY,",",Jpos.RZ});
            if (err == 0)
            {
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ, ",", vt });
            }
            else if (err == 1)
            {
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ, ",", vt });
            }
            else if (err == 2)
            {
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z - 8, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ, ",", vt });
            }
            else if (err == 3)
            {
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z - 12, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ, ",", vt });
            }
            else if (err == 4)
            {
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z - 16, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ, ",", vt });
            }
            else if (err == 5)
            {
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z - 22, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ, ",", vt });
            }
            else
            {
                Jposition = string.Concat(new object[] { ",", Jpos.X, ",", Jpos.Y, ",", Jpos.Z, ",", Jpos.RX, ",", Jpos.RY, ",", Jpos.RZ, ",", vt });
            }

            int sends = this.tcpsent(string.Concat(new object[] { "MoveBEX,", rbtID, Jposition, ",;" }));
            bool flag = sends == 0;
            string result;
            if (flag)
            {
                string bufdata = this.subRecive();
                bool flag2 = bufdata != "";
                if (flag2)
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = this.WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            result = CODE.OK;
                        }
                        else
                        {
                            result = CODE.SUBRECIVE_FAIL;
                        }
                    }
                    else
                    {
                        result = CODE.SUBRECIVE_FAIL;
                    }
                }
                else
                {
                    result = CODE.SUBRECIVE_FAIL;
                }
            }
            else
            {
                result = CODE.CONNECT_FAIL;
            }
            return result;
        }
        public string SetSpeedUp(int rbtID, int nType, int nSwitch)
        {
            string Pos = "";
            int sends = tcpsent("SetSpeedUp," + rbtID + "," + nType + "," + nSwitch + ",;");
            if (sends == 0)
            {
                string bufdata = subRecive();
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            return AxisCompleted;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }
        public string SetSpeedDown(int rbtID, int nType, int nSwitch)
        {
            string Pos = "";
            int sends = tcpsent("SetSpeedDown," + rbtID + "," + nType + "," + nSwitch + ",;");
            if (sends == 0)
            {
                string bufdata = subRecive();
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            return AxisCompleted;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }
        public string SetOverride(int rbtID, double vt)
        {
            string Pos = "";
            int sends = tcpsent("SetOverride," + rbtID + "," + vt.ToString() + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("SetOverride");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            return AxisCompleted;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }
        public string StartPushMovePath(int rbtID, string Trajectory, double vt, double Radius)
        {
            string Pos = "";
            //int sends = tcpsent("StartPushMovePath, " + rbtID + "," + pla.X + "," + pla.Y + "," + pla.Z + "," + pla.RX + "," + pla.RY + "," + pla.RZ + "," + vt + "," + Radius + ",;");
            int sends = tcpsent("StartPushMovePath, " + rbtID + "," + Trajectory + "," + vt + "," + Radius + ",;");
            if (sends == 0)
            {
                string bufdata = subRecive();
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            return AxisCompleted;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }

        public string EndPushMovePath(int rbtID, string Trajectory)
        {
            string Pos = "";
            int sends = tcpsent("EndPushMovePath, " + rbtID + "," + Trajectory + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("EndPushMovePath");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            return AxisCompleted;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }

        public string PushMovePathL(int rbtID, string Trajectory, PosMoveL pla)
        {
            string Pos = "";
            string str = "PushMovePathL, " + rbtID + "," + Trajectory + "," + pla.X + "," + pla.Y + "," + pla.Z + "," + pla.RX + "," + pla.RY + "," + pla.RZ + ",;";
            int sends = tcpsent("PushMovePathL, " + rbtID + "," + Trajectory + "," + pla.X + "," + pla.Y + "," + pla.Z + "," + pla.RX + "," + pla.RY + "," + pla.RZ + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("PushMovePathL");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        return CODE.OK;
                        //string AxisCompleted = WaitMotionFinish();
                        //if (AxisCompleted == "0")
                        //{
                        //    return Pos = arraybuf[1];
                        //}
                        //else
                        //{
                        //    return AxisCompleted;
                        //}
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }

        public string PushMovePathJ(int rbtID, string Trajectory, PosMoveJ pla)
        {
            string Pos = "";
            string str = "PushMovePathJ, " + rbtID + "," + Trajectory + "," + pla.J1 + "," + pla.J2 + "," + pla.J3 + "," + pla.J4 + "," + pla.J5 + "," + pla.J6 + ",;";
            int sends = tcpsent(str);
            if (sends == 0)
            {
                string bufdata = subReciveCmd("PushMovePathJ");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        return CODE.OK;
                        //string AxisCompleted = WaitMotionFinish();
                        //if (AxisCompleted == "0")
                        //{
                        //    return Pos = arraybuf[1];
                        //}
                        //else
                        //{
                        //    return AxisCompleted;
                        //}
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }

        public string PushMovePaths(int rbtID, string Trajectory, PosMoveL pla)
        {
            string Pos = "";
            int sends = tcpsent("PushMovePaths, " + rbtID + "," + Trajectory + ",1" + "," + pla.X + "," + pla.Y + "," + pla.Z + "," + pla.RX + "," + pla.RY + "," + pla.RZ + ",;");
            if (sends == 0)
            {
                string bufdata = subRecive();
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            return AxisCompleted;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }
        public string MovePath(int rbtID, string Trajectory, PosMoveJ JposDeck)
        {
            string Pos = "MovePath, " + rbtID + "," + Trajectory + ",;";
            string str = "";
            //   for (int i = 0; i < 2; i++)
            {
                int sends = tcpsent(Pos);
                if (sends == 0)
                {
                    string bufdata = subReciveCmd("MovePath");
                    if (bufdata != "")
                    {
                        string[] arraybuf = bufdata.Split(',');
                        if (arraybuf[1] == "OK")
                        {

                            int complete = CompleteMoveJ(JposDeck);
                            if (complete == 0)
                            {
                                return CODE.OK;
                            }
                            else
                            {

                                return "1";
                            }
                        }
                        else
                            return arraybuf[2];
                    }
                    else
                        return CODE.SUBRECIVE_FAIL;
                }
                else
                {
                    str = CODE.CONNECT_FAIL;
                }
            }
            return str;
        }
        public string MovePath_No(int rbtID, string Trajectory, PosMoveJ JposDeck)
        {
            string Pos = "MovePath, " + rbtID + "," + Trajectory + ",;";
            string str = "";
            //   for (int i = 0; i < 2; i++)
            {
                int sends = tcpsent(Pos);
                if (sends == 0)
                {
                    string bufdata = subReciveCmd("MovePath");
                    if (bufdata != "")
                    {
                        string[] arraybuf = bufdata.Split(',');
                        if (arraybuf[1] == "OK")
                        {
                            return CODE.OK;
                        }
                        else
                            return arraybuf[2];
                    }
                    else
                        return CODE.SUBRECIVE_FAIL;
                }
                else
                {
                    str = CODE.CONNECT_FAIL;
                }
            }
            return str;
        }
        public string ReadMovePathJState(int rbtID, string Trajectory, int ctime)
        {
            string[] arraybuf = new string[3];
            int count = 0;
            for (int i = 0; i < ctime; i++)
            {
                Thread.Sleep(50);
                try
                {
                    // 1. Send get robot motion state message: ReadMoveState,;
                    int sends = tcpsent("ReadMovePathState," + rbtID + "," + Trajectory + ",;");
                    if (sends == 0)
                    {
                        //Thread.Sleep(20);
                        string bufdata = subRecive();
                        if (bufdata != "")
                        {
                            arraybuf = bufdata.Split(',');
                            if (arraybuf[2] == "3")
                            {
                                return CODE.OK;
                            }
                        }
                    }
                    else
                    {
                        return CODE.FAIL;
                    }


                }
                catch
                {

                }

            }
            return CODE.CONNECT_FAIL;
        }

        public string MovePathL(int rbtID, string Trajectory)
        {
            string Pos = "MovePathL, " + rbtID + "," + Trajectory + ",;";
            int sends = tcpsent(Pos);
            if (sends == 0)
            {
                string bufdata = subRecive();
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            return AxisCompleted;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }
        public string ReadMovePathState(int rbtID, string Trajectory)
        {
            string[] arraybuf = new string[3];
            int count = 0;
            while (true)
            {
                Thread.Sleep(50);
                try
                {
                    // 1. Send get robot motion state message: ReadMoveState,;
                    int sends = tcpsent("ReadMovePathState," + rbtID + "," + Trajectory + ",;");
                    if (sends == 0)
                    {
                        //Thread.Sleep(20);
                        string bufdata = subRecive();
                        if (bufdata != "")
                        {
                            arraybuf = bufdata.Split(',');
                            if (arraybuf[1] == "OK")
                            {
                                return CODE.OK;
                            }
                            else
                            {
                                return CODE.FAIL;
                            }
                        }
                    }
                    else
                    {
                        return CODE.FAIL;
                    }

                    // 2. Parsing the returned motion state: MoveState if(MoveState == 1009)
                    if (arraybuf[2] == "1009")
                    {
                        // In motion, wait for 10 milliseconds, query again 
                        Thread.Sleep(20);
                    }
                    else if (arraybuf[2] == "0")
                    {
                        // The movement is completed and is returned directly to
                        return arraybuf[2];
                    }
                    else
                    {
                        // Other error conditions are dealt with separately 
                        return CODE.FAIL;
                    }
                    if (count > 400)
                    {
                        return CODE.FAIL;
                    }
                    count++;
                }
                catch
                {
                    count++;
                }

            }
            return CODE.CONNECT_FAIL;
        }
        public string InitMovePathL(int rbtID, string Trajectory)
        {
            string Pos = "";
            //int sends = tcpsent("StartPushMovePath, " + rbtID + "," + pla.X + "," + pla.Y + "," + pla.Z + "," + pla.RX + "," + pla.RY + "," + pla.RZ + "," + vt + "," + Radius + ",;");
            int sends = tcpsent("InitMovePathJ, " + rbtID + "," + Trajectory + ",100,2500,1000000,Base,TCP,;");
            if (sends == 0)
            {
                string bufdata = subRecive();
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            return AxisCompleted;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }

        public string SetMovePathOverride(int rbtID, string Trajectory, double vt)
        {
            string Pos = "";
            //int sends = tcpsent("StartPushMovePath, " + rbtID + "," + pla.X + "," + pla.Y + "," + pla.Z + "," + pla.RX + "," + pla.RY + "," + pla.RZ + "," + vt + "," + Radius + ",;");
            int sends = tcpsent("SetMovePathOverride, " + rbtID + " ," + Trajectory + +vt + ",;");
            if (sends == 0)
            {
                string bufdata = subRecive();
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            return AxisCompleted;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }

        public string DelMovePath(int rbtID, string Trajectory)
        {
            string Pos = "";
            //int sends = tcpsent("StartPushMovePath, " + rbtID + "," + pla.X + "," + pla.Y + "," + pla.Z + "," + pla.RX + "," + pla.RY + "," + pla.RZ + "," + vt + "," + Radius + ",;");
            int sends = tcpsent("DelMovePath, " + rbtID + "," + Trajectory + ",;");
            if (sends == 0)
            {
                string bufdata = subRecive();
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            return AxisCompleted;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }

        public string PushServoP(int rbtID)
        {
            string Pos = "";
            //int sends = tcpsent("StartPushMovePath, " + rbtID + "," + pla.X + "," + pla.Y + "," + pla.Z + "," + pla.RX + "," + pla.RY + "," + pla.RZ + "," + vt + "," + Radius + ",;");
            int sends = tcpsent("PushServoP," + rbtID + ",420,0,445,180,0,180,0,0,0,0,0,0,0,0,0,0,0,0,;");
            if (sends == 0)
            {
                string bufdata = subRecive();
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            return AxisCompleted;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }

        public string StartServo(int rbtID)
        {
            string Pos = "";
            //int sends = tcpsent("StartPushMovePath, " + rbtID + "," + pla.X + "," + pla.Y + "," + pla.Z + "," + pla.RX + "," + pla.RY + "," + pla.RZ + "," + vt + "," + Radius + ",;");
            int sends = tcpsent("StartServo," + rbtID + ", 0.02,0.02,;");
            if (sends == 0)
            {
                string bufdata = subRecive();
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            return AxisCompleted;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }

     
        public string SettingStatus(int[] data)
        {
            return "Ok";
        }
        public int CompleteXYZ(PosMoveL pla)
        {
            bool flag01 = true;
            bool flag02 = true;
            bool flag03 = true;
            //bool flag04 = true;
            //bool flag05 = true;
            bool flag06 = true;
            bool flag2 = true;
            int i = 0;
            while (flag2 == true)
            {
                Thread.Sleep(20);
                string str = ReadActualPos(0);
                string[] arr = str.Split(',');
                if (arr[0] == "OK")
                {
                    if ((double.Parse(arr[1], System.Globalization.CultureInfo.InvariantCulture) > (pla.X - 0.03)) && (double.Parse(arr[1]) < (pla.X + 0.03)))
                    {
                        flag01 = true;
                    }
                    else
                    {
                        flag01 = false;
                    }
                    if ((double.Parse(arr[2], System.Globalization.CultureInfo.InvariantCulture) > (pla.Y - 0.03)) && ((double.Parse(arr[2]) < (pla.Y + 0.03))))
                    {
                        flag02 = true;
                    }
                    else
                    {
                        flag02 = false;
                    }
                    if ((double.Parse(arr[3], System.Globalization.CultureInfo.InvariantCulture) > (pla.Z - 0.03)) && (double.Parse(arr[3]) < (pla.Z + 0.03)))
                    {
                        flag03 = true;
                    }
                    else
                    {
                        flag03 = false;
                    }
                    //if ((double.Parse(arr[4]) > (pla.RX - 0.03)) && (double.Parse(arr[4]) < (pla.RX + 0.03)))
                    //{
                    //	flag04 = true;
                    //}
                    //else
                    //{
                    //	flag04 = false;
                    //}
                    //if ((double.Parse(arr[5]) > (pla.RY - 0.03)) && (double.Parse(arr[5]) < (pla.RY + 0.03)))
                    //{
                    //	flag05 = true;
                    //}
                    //else
                    //{
                    //	flag05 = false;
                    //}
                    if ((double.Parse(arr[6], System.Globalization.CultureInfo.InvariantCulture) > (pla.RZ - 0.03)) && (double.Parse(arr[6]) < (pla.RZ + 0.03)))
                    {
                        flag06 = true;
                    }
                    else
                    {
                        flag06 = false;
                    }
                }
                else
                {
                    return 1;
                }

                if (flag03 == true && flag01 == true && flag02 == true && flag06 == true)
                {
                    flag2 = false;
                    Thread.Sleep(10);
                    return 0;
                }
                i++;
                if (i > 1500)
                {
                    return 1;
                }

            }
            return 1;
        }
        public int CompleteMoveJ(PosMoveJ pla)
        {
            bool flag01 = true;
            bool flag02 = true;
            bool flag03 = true;
            bool flag04 = true;
            bool flag05 = true;
            bool flag06 = true;
            bool flag2 = true;
            int i = 0;
            int j = 0;
            while (flag2 == true)
            {
                j++;
                Thread.Sleep(10);
                string str = ReadActualPos(0);
                string[] arr = str.Split(',');
                if (arr[0] == "OK")
                {
                    j = 0;
                    if ((double.Parse(arr[7]) > (pla.J1 - 0.1)) && (double.Parse(arr[7]) < (pla.J1 + 0.1)))
                    {
                        flag01 = true;
                    }
                    else
                    {
                        flag01 = false;
                    }
                    if ((double.Parse(arr[8]) > (pla.J2 - 0.1)) && ((double.Parse(arr[8]) < (pla.J2 + 0.1))))
                    {
                        flag02 = true;
                    }
                    else
                    {
                        flag02 = false;
                    }
                    if ((double.Parse(arr[9]) > (pla.J3 - 0.1)) && (double.Parse(arr[9]) < (pla.J3 + 0.1)))
                    {
                        flag03 = true;
                    }
                    else
                    {
                        flag03 = false;
                    }
                    if ((double.Parse(arr[10]) > (pla.J4 - 0.1)) && (double.Parse(arr[10]) < (pla.J4 + 0.1)))
                    {
                        flag04 = true;
                    }
                    else
                    {
                        flag04 = false;
                    }
                    if ((double.Parse(arr[11]) > (pla.J5 - 0.1)) && (double.Parse(arr[11]) < (pla.J5 + 0.1)))
                    {
                        flag05 = true;
                    }
                    else
                    {
                        flag05 = false;
                    }
                    if ((double.Parse(arr[12]) > (pla.J6 - 0.1)) && (double.Parse(arr[12]) < (pla.J6 + 0.1)))
                    {
                        flag06 = true;
                    }
                    else
                    {
                        flag06 = false;
                    }
                }
                else
                {
                    if (j > 5)
                    {
                        return 1;
                    }

                }

                if (flag03 == true && flag01 == true && flag02 == true && flag04 == true && flag05 == true && flag06 == true)
                {
                    flag2 = false;
                    Thread.Sleep(50);
                    return 0;
                }
                i++;
                if (i > 1700)
                {
                    return 1;
                }

            }
            return 1;
        }
        public int CompleteRobot(int rbtID)
        {
            bool flag = true;
            int i = 0;
            while (flag == true)
            {
                Thread.Sleep(10);
                int[] data;
                string err = ReadRobotState(0, out data);
                if (err == "OK")
                {
                    if (data[0] == 0)
                    {
                        return 0;
                    }

                }
                else
                {
                    return 1;
                }
                i++;
                if (i > 1000)
                {
                    return 1;
                }
            }
            return 1;
        }

        public string StartPushBlending(int rbtID)
        {
            string Pos = "";
            int sends = tcpsent("StartPushMovePath," + rbtID + ", " + "name " + ", " + "0.1" + ", " + "0" + ",;");
            if (sends == 0)
            {
                string bufdata = subRecive();
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            return AxisCompleted;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }
        public string EndPushBlending(int rbtID)
        {
            string Pos = "";
            int sends = tcpsent("EndPushMovePath, " + rbtID + ",;");
            if (sends == 0)
            {
                string bufdata = subRecive();
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            return AxisCompleted;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }
        public string PushBlendingL(int rbtID, PosMoveL pla, double Radius)
        {
            string Pos = "";
            int sends = tcpsent("PushMovePathL, " + rbtID + "," + pla.X + "," + pla.Y + "," + pla.Z + "," + pla.RX + "," + pla.RY + "," + pla.RZ + "," + Radius + ",;");
            if (sends == 0)
            {
                string bufdata = subRecive();
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        string AxisCompleted = WaitMotionFinish();
                        if (AxisCompleted == "0")
                        {
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            return AxisCompleted;
                        }
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }
        public int CheckErrRobot(int rbtID, out string StrErr, out int Err)
        {
            int[] data;
            string str = "";
            int err = 0;
            Err = 0;
            StrErr = "";
            try
            {
                string ErrType = ReadRobotState(rbtID, out data);
                if (ErrType == "OK")
                {
                    for (int i = 0; i < 15; i++)
                    {
                        if (i == 0)
                        {
                        }
                        if (i == 1)
                        {
                            if (data[i] == 0)
                            {
                                err = 1;
                                str += ">>> powerState:" + " de enable state " + Environment.NewLine;
                            }
                            else
                            {

                            }
                        }
                        if (i == 2)
                        {
                            if (data[i] == 0)
                            {
                            }
                            else
                            {
                                err = 2;
                                str += ">>> errorState:" + " error reported " + Environment.NewLine;
                            }
                        }
                        if (i == 3)
                        {
                            if (data[i] > 0)
                            {
                                err = 3;
                                str += ">>> errorCode: " + data[i].ToString() + Environment.NewLine;
                                Err = data[i];
                            }

                        }
                        if (i == 4)
                        {
                            if (data[i] > 0)
                            {
                                err = 4;
                                str += ">>> errAxisID: " + data[i].ToString() + Environment.NewLine;
                            }

                        }
                        if (i == 5)
                        {
                            if (data[i] == 0)
                            {
                            }
                            else
                            {
                                err = 5;
                                str += ">>> BrakingState " + "brake in operation  " + data[i].ToString() + Environment.NewLine;
                            }
                        }
                        if (i == 6)
                        {
                            if (data[i] > 0)
                            {
                                err = 6;
                                str += ">>> HoldingState: " + Environment.NewLine;
                            }

                        }
                        if (i == 7)
                        {
                            if (data[i] == 0)
                            {

                            }
                            else
                            {
                                err = 7;
                                str += ">>> Emergency: Emergency Stop " + Environment.NewLine;
                            }
                        }
                        if (i == 8)
                        {
                            if (data[i] == 0)
                            {
                                //   err = 1;
                                //str += ">>> SaftyGuard : no safety light curtain " + data[i].ToString() + Environment.NewLine;
                            }
                            else
                            {
                            }
                        }
                        if (i == 9)
                        {
                            if (data[i] == 0)
                            {
                                err = 9;
                                str += ">>> Electrify: not powered on " + Environment.NewLine;
                            }
                            else
                            {

                            }
                        }
                        if (i == 10)
                        {
                            if (data[i] == 0)
                            {
                                err = 10;
                                str += ">>> IsConnectToBox:  not connected " + Environment.NewLine;
                            }
                            else
                            {
                            }
                        }
                        if (i == 11)
                        {
                            if (data[i] == 0)
                            {
                                err = 11;
                                str += ">>> blendingDone:   blending motion not " + Environment.NewLine;
                            }
                            else
                            {
                            }
                        }

                    }
                }
                else
                {
                    Err = 0; StrErr = "Error cmd" + Environment.NewLine;
                    return 30;
                }
                Err = err;
                StrErr = str;
                return err;
            }
            catch
            {
                Err = 0; StrErr = "Error read to robot cmd" + Environment.NewLine;
                return 20;
            }

        }
        public int CheckStatusMove(int rbtID, int ctime)
        {
            for (int i = 0; i < ctime; i++)
            {
                int[] data;
                string ErrType = ReadRobotState(rbtID, out data);
                if (data[0] == 0)
                {
                    return 0;
                }
                Thread.Sleep(10);
            }
            return 1;
        }
        public int Enable(int rbtID)
        {
            int Err = 0;
            int[] data;
            string ErrType = ReadRobotState(rbtID, out data);
            if (ErrType == "OK")
            {
                if (data[1] == 0)
                {
                    Err = GrpPowerOn(0);
                    Thread.Sleep(2000);
                }
            }
            else
            {
                return 2;
            }
            return 0;
        }
        public int ResetError(int rbtID)
        {
            int Err = 0;
            int[] data;
            string ErrType = ReadRobotState(rbtID, out data);
            if (ErrType == "OK")
            {
                if (data[7] == 1)
                {
                    GrpReset(0);
                    Thread.Sleep(500);
                }
                if (data[3] > 0)
                {
                    GrpReset(0);
                    Thread.Sleep(500);
                }
                if (data[4] > 0)
                {
                    GrpReset(0);
                    Thread.Sleep(500);
                }
                if (data[9] == 0)
                {
                    Err = StartMaster(0);
                    Thread.Sleep(20000);
                }
                if (data[1] == 0)
                {
                    Err = GrpPowerOn(0);
                    Thread.Sleep(2500);
                }
            }
            else
            {
                return 2;
            }
            return 0;
        }
        public string PCS2ACS(int rbtID, PosMoveL pl, PosMoveJ pj, string coordinate, out PosMoveJ outdataJ)
        {
            outdataJ = new PosMoveJ();
            try
            {
                string kp = "OK";
                string Jposition = "," + pj.J1 + "," + pj.J2 + "," + pj.J3 + "," + pj.J4 + "," + pj.J5 + "," + pj.J6;
                string Lposition = "," + pl.X + "," + pl.Y + "," + pl.Z + "," + pl.RX + "," + pl.RY + "," + pl.RZ;
                string str = coordinate + ",0,0,0,0,0,0";
                int sends = tcpsent("PCS2ACS," + rbtID + Lposition + Jposition + str + ",;");
                if (sends == 0)
                {
                    string bufdata = subReciveCmd("PCS2ACS");
                    if (bufdata != "")
                    {
                        string[] arraybuf = bufdata.Split(',');
                        outdataJ.J1 = double.Parse(arraybuf[2]);
                        outdataJ.J2 = double.Parse(arraybuf[3]);
                        outdataJ.J3 = double.Parse(arraybuf[4]);
                        outdataJ.J4 = double.Parse(arraybuf[5]);
                        outdataJ.J5 = double.Parse(arraybuf[6]);
                        outdataJ.J6 = double.Parse(arraybuf[7]);
                    }
                    else
                        return CODE.SUBRECIVE_FAIL;
                }
                else
                {
                    return CODE.CONNECT_FAIL;
                }
                return kp;
            }
            catch (Exception ex)
            {
                outdataJ = null;
                return ex.Message;
            }

        }
        public string SetUCSByName(int rbtID, string ucsname)
        {
            string Pos = "";
            int sends = tcpsent("SetUCSByName," + rbtID + ',' + ucsname + ",;");
            if (sends == 0)
            {
                string bufdata = subReciveCmd("SetUCSByName");
                if (bufdata != "")
                {
                    string[] arraybuf = bufdata.Split(',');
                    if (arraybuf[1] == "OK")
                    {
                        return Pos = arraybuf[1];
                    }
                    else
                        return arraybuf[2];
                }
                else
                    return CODE.SUBRECIVE_FAIL;
            }
            else
            {
                return CODE.CONNECT_FAIL;
            }
        }
        public string ReadCurFSM(int rbtID, out int error)
        {
            error = 0;
            try
            {
                string Pos = "";
                int sends = tcpsent("ReadCurFSM," + rbtID + ",;");
                if (sends == 0)
                {
                    string bufdata = subReciveCmd("ReadCurFSM");
                    if (bufdata != "")
                    {
                        string[] arraybuf = bufdata.Split(',');
                        if (arraybuf[1] == "OK")
                        {
                            try
                            {
                                error = int.Parse(arraybuf[2]);
                            }
                            catch
                            {

                            }
                            return Pos = arraybuf[1];
                        }
                        else
                        {
                            try
                            {
                                error = int.Parse(arraybuf[2]);
                            }
                            catch
                            {

                            }
                            return CODE.SUBRECIVE_FAIL;
                        }

                    }
                    else
                        return CODE.SUBRECIVE_FAIL;
                }
                else
                {
                    return CODE.CONNECT_FAIL;
                }
            }
            catch
            {
                return CODE.CONNECT_FAIL;
            }
        }

        internal string ReadBoxDO_01234567()
        {
            throw new NotImplementedException();
        }
    }

}
