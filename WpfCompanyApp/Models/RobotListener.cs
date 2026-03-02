using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WpfCompanyApp.Models
{
    public static class CODE
    {
        public const int ERR_Success = 0;
        public const int ERR_Fail = -1;
        public const int ERR_Connect = -1001; //Not connected
        public const int ERR_NotRecive = -1002; //Not Recive

        public const string OK = "OK";
        public const string FAIL = "Fail";
        public const string CONNECT_FAIL = "Fail";
        public const string SUBRECIVE_FAIL = "Fail";
        public const string NULL_DATA = "NULL_DATA";

    }
    public class PosMoveL
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double RX { get; set; }
        public double RY { get; set; }
        public double RZ { get; set; }
    }
    public class PosMoveJ
    {
        public double J1 { get; set; }
        public double J2 { get; set; }
        public double J3 { get; set; }
        public double J4 { get; set; }
        public double J5 { get; set; }
        public double J6 { get; set; }
    }
    public class CalculationPra
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
    public class CalibMode
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double Z1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double Z2 { get; set; }
        public double X3 { get; set; }
        public double Y3 { get; set; }
        public double Z3 { get; set; }
        public double Z01 { get; set; }
        public double Z02 { get; set; }
        public double Z03 { get; set; }
        public double Zchup { get; set; }
        public double Unit { get; set; }
        public double Radius { get; set; }
        public double Rpiex { get; set; }
        public double Cent { get; set; }
    }
    public class PosCaculator
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double RX { get; set; }
        public double RY { get; set; }
        public double RZ { get; set; }
    }
    public class VanBoard
    {
        public int board1 { get; set; }
        public int board2 { get; set; }
        public int board3 { get; set; }
        public int board4 { get; set; }

        public int board5 { get; set; }
        public int board6 { get; set; }
        public int board7 { get; set; }
        public int board8 { get; set; }
        public int board9 { get; set; }
        public int board10 { get; set; }
        public int board11 { get; set; }

        public int board12 { get; set; }
        public int board13 { get; set; }

        public int board14 { get; set; }
        public int board15 { get; set; }
        public int board16 { get; set; }
        public int board17 { get; set; }
        public int board18 { get; set; }
        public int totalBoard { get; set; }
    }
    public class PosMoveL3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double RX { get; set; }
        public double RY { get; set; }
        public double RZ { get; set; }
        public double Lable { get; set; }

    }
    public class PosMoveL3DN
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double RX { get; set; }
        public double RY { get; set; }
        public double RZ { get; set; }
        public double Lable { get; set; }
        public int count { get; set; }

    }
    public class PosMoveL3D_GL_RX
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double RX { get; set; }
        public double RY { get; set; }
        public double RX_gl { get; set; }
        public double RZ { get; set; }
        public double Lable { get; set; }

    }
    public class PosMoveL3D_GL_RY
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double RX { get; set; }
        public double RY { get; set; }
        public double RY_gl { get; set; }
        public double RZ { get; set; }
        public double Lable { get; set; }

    }
    public class PosMoveL3D_Z
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double RX { get; set; }
        public double RY { get; set; }
        public double RZ { get; set; }
        public double Lable { get; set; }

    }
    public class CurrentTime
    {
        public double c_time { get; set; }
        public int countsp { get; set; }
    }
}
