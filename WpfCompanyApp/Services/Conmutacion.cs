using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using WpfCompanyApp.Models;

namespace WpfCompanyApp.Services
{
    public class Conmutacion
    {
        private TcpClient client;
        private StreamReader str_read;
        private StreamWriter str_write;
        private string ip;
        int port;
        int readtimeout;
        //private string ip_camera;
        //int port_camera;
        public  bool tcpConnect(string Address, int port_, int read_timeout)
        {
            ip = Address;
            port = port_;
            readtimeout = read_timeout;
            try
            {
                if (client != null)
                {
                    client.Close();
                }

                //client = new TcpClient();

                //IPEndPoint IP_end = new IPEndPoint(IPAddress.Parse(ip), port);

                //client.Connect(IP_end);
                 client = new TcpClient();
                

                int timeoutMs = 1000;

                var task = client.ConnectAsync(ip, port);

                if (!task.Wait(timeoutMs))
                { 
                    client.Close();
                    throw new TimeoutException("Connect timeout");
                }

                if (client.Connected)
                {
                    client.ReceiveTimeout = readtimeout;
                    client.SendTimeout = 1000;
                    str_write = new StreamWriter(client.GetStream());
                    str_read = new StreamReader(client.GetStream());

                    str_write.AutoFlush = true;
                }
                return client.Connected;
            }
            catch
            {
                //MessageBox.Show(ex.Message.ToString());
                return false;
            }
        }

        /// <summary>
        /// Disconnect
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (client != null)
                {
                    // BỎ kiểm tra client.Connected để tránh lỗi NullReferenceException
                    client.Close();
                    client = null; // Đặt về null để làm sạch hoàn toàn
                }
            }
            catch (Exception)
            {
                // Bỏ qua lỗi trong quá trình đóng
            }
        }

        public int checkConnect()
        {
            try
            {
                char[] _buf = new char[1024];
                int receive1 = str_read.Read(_buf, 0, _buf.Length);
                if (receive1 == 0)
                {
                    return 0;
                }
                return 2;
            }
            catch
            {
                return 1;
            }
        }
        /// <summary>
        /// Thành công: 0
        /// thất bại: -1
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>

        public int tcpsent(string command)
        {
            try
            {
                if (client == null)
                {
                    return CODE.ERR_Fail;
                }

                // 1. Thêm điều kiện client.Client == null để tránh lỗi NullReferenceException
                if (client.Client == null || !client.Connected || !IsSocketConnected(client.Client))
                {
                    Disconnect();
                    bool isConnected = tcpConnect(ip, port, readtimeout); // Thử kết nối lại

                    // 2. Nếu kết nối lại THẤT BẠI, thoát ngay lập tức!
                    if (!isConnected)
                    {
                        return CODE.ERR_Connect;
                    }
                }

                // 3. Lúc này chắc chắn Socket đã an toàn, không bị null nữa
                if ((command != "") && (client.Connected))
                {
                    str_write.Write(command);
                    return CODE.ERR_Success;
                }

                return CODE.ERR_Fail;
            }
            catch (Exception ex)
            {
                return CODE.ERR_Fail;
            }
        }
        public string subReciveCmdJaka(string cmd)
        {
            string barcoderv = "";
            try
            {
                if (!client.Connected || !IsSocketConnected(client.Client))
                {
                    Disconnect();
                    tcpConnect(ip, port, readtimeout);
                }
                int i = 0;
                while (i < 5)
                {
                    char[] _buf = new char[1024];
                    int receive1 = str_read.Read(_buf, 0, _buf.Length);
                    barcoderv = new string(_buf).TrimEnd('\x0');
                    string[] arr = barcoderv.Split(',');
                    return barcoderv;
                    //if (arr[0] == cmd.Trim())
                    //{
                    //	return barcoderv;
                    //}
                    //	i++;
                }
                return barcoderv = "Fail";
            }
            catch
            {
                if (client.Connected == false)
                {
                    bool st = tcpConnect(ip, port, readtimeout);
                }
                return "";
            }
        }
        public string subReciveCmd(string cmd)
        {

            string barcoderv = "";
            try
            {
                if (!client.Connected || !IsSocketConnected(client.Client))
                {
                    Disconnect();
                    tcpConnect(ip, port, readtimeout);
                }
                int i = 0;
                while (i < 5)
                {
                    char[] _buf = new char[1024];
                    int receive1 = str_read.Read(_buf, 0, _buf.Length);
                    barcoderv = new string(_buf).TrimEnd('\x0');
                    string[] arr = barcoderv.Split(',');
                    if (arr[0] == cmd.Trim())
                    {
                        return barcoderv;
                    }
                    i++;
                }
                return barcoderv = "Fail";
            }
            catch
            {
                if (client.Connected == false)
                {
                    bool st = tcpConnect(ip, port, readtimeout);
                }
                return "";
            }
        }

        public string subRecive()
        {

            string barcoderv = "";
            try
            {

                char[] _buf = new char[1024];
                int receive1 = str_read.Read(_buf, 0, _buf.Length);
                barcoderv = new string(_buf).TrimEnd('\x0');
                string[] arr = barcoderv.Split(',');
                return barcoderv;

            }
            catch (Exception ex)
            {
                if (client.Connected == false)
                {
                    bool st = tcpConnect(ip, port, readtimeout);
                }
                string asq = ex.Message;
                return barcoderv;
            }
        }

        public string subRecive2()
        {

            string barcoderv = "";
            try
            {

                char[] _buf = new char[1000000];
                int receive1 = str_read.Read(_buf, 0, _buf.Length);
                barcoderv = new string(_buf);
                // string[] arr = barcoderv.Split(',');
                return barcoderv;

            }
            catch
            {
                if (client.Connected == false)
                {
                    bool st = tcpConnect(ip, port, readtimeout);
                }
                return "";
            }
        }
        private static bool IsSocketConnected(Socket socket)
        {
            try
            {
                if (socket == null) return false;
                return !(socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch (SocketException) { return false; }
            catch (ObjectDisposedException) { return false; }
        }

    }
    public class Conmutacion_cam
    {
        private TcpClient client;
        private StreamReader str_read;
        private StreamWriter str_write;
        private string ip;
        int port;
        int readtimeout;
        //private string ip_camera;
        //int port_camera;
        public bool tcpConnect(string Address, int port_, int read_timeout)
        {
            ip = Address;
            port = port_;
            readtimeout = read_timeout;
            try
            {
                if (client != null)
                {
                    client.Close();
                }

                client = new TcpClient();
                IPEndPoint IP_end = new IPEndPoint(IPAddress.Parse(ip), port);
                client.Connect(IP_end);
                if (client.Connected)
                {
                    client.ReceiveTimeout = readtimeout;
                    client.SendTimeout = 8000;
                    str_write = new StreamWriter(client.GetStream());
                    str_read = new StreamReader(client.GetStream());

                    str_write.AutoFlush = true;
                }
                return client.Connected;
            }
            catch
            {
                //MessageBox.Show(ex.Message.ToString());
                return false;
            }
        }
        public void SetTimeOut(int timeout)
        {
            client.ReceiveTimeout = timeout;
        }
        /// <summary>
        /// Disconnect
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (client != null)
                {
                    client.Close();
                    client = null;
                }
            }
            catch (Exception)
            {
                // Bỏ qua lỗi
            }
        }

        public int checkConnect()
        {
            try
            {
                char[] _buf = new char[1024];
                int receive1 = str_read.Read(_buf, 0, _buf.Length);
                if (receive1 == 0)
                {
                    return 0;
                }
                return 2;
            }
            catch
            {
                return 1;
            }
        }
        /// <summary>
        /// Thành công: 0
        /// thất bại: -1
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>

        public int tcpsent(string command)
        {
            try
            {
                // 1. Chặn lỗi rỗng nếu chưa kết nối bao giờ
                if (client == null)
                {
                    return CODE.ERR_Fail;
                }

                // 2. Chặn lỗi rỗng khi mất kết nối và tự động kết nối lại an toàn
                if (client.Client == null || !client.Connected || !IsSocketConnected(client.Client))
                {
                    Disconnect();
                    bool isConnected = tcpConnect(ip, port, readtimeout);
                    if (!isConnected)
                    {
                        return CODE.ERR_Connect;
                    }
                }

                // 3. Gửi lệnh đi
                if ((command != "") && (client.Connected))
                {
                    str_write.Write(command);
                    return CODE.ERR_Success;
                }

                return CODE.ERR_Fail;
            }
            catch (Exception)
            {
                return CODE.ERR_Fail;
            }
        }

        public string subReciveCmd(string cmd)
        {

            string barcoderv = "";
            try
            {
                if (!client.Connected || !IsSocketConnected(client.Client))
                {
                    Disconnect();
                    tcpConnect(ip, port, readtimeout);
                }
                int i = 0;
                while (i < 5)
                {
                    char[] _buf = new char[1024];
                    int receive1 = str_read.Read(_buf, 0, _buf.Length);
                    barcoderv = new string(_buf).TrimEnd('\x0');
                    string[] arr = barcoderv.Split(',');
                    if (arr[0] == cmd.Trim())
                    {
                        return barcoderv;
                    }
                    i++;
                }
                return barcoderv = "Fail";
            }
            catch
            {
                if (client.Connected == false)
                {
                    bool st = tcpConnect(ip, port, readtimeout);
                }
                return "";
            }
        }
        public void checkConnecnt()
        {
            if (!client.Connected || !IsSocketConnected(client.Client))
            {
                Disconnect();
                tcpConnect(ip, port, readtimeout);
            }
        }
        public string subRecive()
        {

            string barcoderv = "";
            char[] _buf = new char[5024];
            double receive1;
            try
            {
                receive1 = str_read.Read(_buf, 0, _buf.Length);
                if (receive1 == 0)
                {
                    if (!client.Connected || !IsSocketConnected(client.Client))
                    {
                        Disconnect();
                        tcpConnect(ip, port, readtimeout);
                    }
                }
                barcoderv = new string(_buf).TrimEnd('\x0');
                return barcoderv;

            }
            catch (Exception ex)
            {
                barcoderv = "Timeout Camera";
                if (client.Client.Poll(0, SelectMode.SelectRead))
                {
                    byte[] buff = new byte[1];
                    if (client.Client.Receive(buff, SocketFlags.Peek) == 0)
                    {
                        barcoderv = "Client disconnected";
                        bool st = tcpConnect(ip, port, readtimeout);
                    }
                }
                if (client.Connected == false)
                {
                    bool st = tcpConnect(ip, port, readtimeout);
                    barcoderv = "Disconnect Camera";
                }

                return barcoderv;
            }

        }
        public string subRecivebyte()
        {

            string barcoderv = "";
            char[] buffer = new char[2];
            int receive1;
            try
            {
                int i = 0;
                bool flag = true;
                while (true)
                {
                    receive1 = str_read.Read(buffer, 0, 1);
                    if (buffer[0] == ']')
                    {
                        return barcoderv;

                    }
                    if (flag == true)
                    {
                        if (buffer[0] == '[')
                        {
                            flag = false;
                        }
                    }
                    else
                    {
                        barcoderv += buffer[0].ToString();
                    }
                    if (receive1 == 0)
                    {
                        Disconnect();
                        tcpConnect(ip, port, readtimeout);
                        return "";
                    }
                }
                return barcoderv;

            }
            catch (Exception ex)
            {
                barcoderv = "Timeout";
                if (client.Client.Poll(0, SelectMode.SelectRead))
                {
                    byte[] buff = new byte[1];
                    if (client.Client.Receive(buff, SocketFlags.Peek) == 0)
                    {
                        barcoderv = "Disconnected";
                        bool st = tcpConnect(ip, port, readtimeout);
                    }
                }
                if (client.Connected == false)
                {
                    bool st = tcpConnect(ip, port, readtimeout);
                    barcoderv = "Disconnect Camera";
                }

                return barcoderv;
            }

        }
        public string reciveByteEmpty()
        {

            string barcoderv = "";

            int receive1;
            try
            {
                int i = 0;
                bool flag = true;
                if (!client.Connected || !IsSocketConnected(client.Client))
                {
                    Disconnect();
                    tcpConnect(ip, port, readtimeout);
                }
                while (true)
                {
                    try
                    {
                        char[] buffer = new char[2048];
                        receive1 = str_read.Read(buffer, 0, buffer.Length);
                        barcoderv += string.Concat(buffer);
                        if (receive1 == 0)
                        {
                            Disconnect();
                            tcpConnect(ip, port, readtimeout);
                            return "Disconnected";
                        }
                    }
                    catch (Exception ex)
                    {
                        return barcoderv;
                    }

                }
                return barcoderv;

            }
            catch (Exception ex)
            {

                if (!client.Connected || !IsSocketConnected(client.Client))
                {
                    Disconnect();
                    tcpConnect(ip, port, readtimeout);
                    barcoderv = "Timeout";
                }
                return barcoderv;
            }

        }
        public string readRecive()
        {

            string barcoderv = "";

            double receive1;
            StringBuilder jsonDataBuilder = new StringBuilder();
            try
            {
                if (!client.Connected || !IsSocketConnected(client.Client))
                {
                    Disconnect();
                    tcpConnect(ip, port, readtimeout);
                }
                while (true)
                {
                    try
                    {

                        char[] _buf = new char[1024];
                        receive1 = str_read.Read(_buf, 0, _buf.Length);

                        client.ReceiveTimeout = 10;
                        string str = string.Concat(_buf).Replace("\0", ""); ;

                        barcoderv += str;
                        if (receive1 == 0)
                        {
                            Disconnect();
                            tcpConnect(ip, port, readtimeout);
                            return "Disconnected";
                        }
                    }
                    catch (Exception ex)
                    {
                        return barcoderv;
                    }

                }
                // Đọc dữ liệu từ client một cách từng dòng
                //   StringBuilder jsonDataBuilder = new StringBuilder();
                //string line = "";
                //while ((line = str_read.ReadLine()) != null)
                //{
                //    jsonDataBuilder.AppendLine(line);
                //}

                ////Chuỗi JSON hoàn chỉnh

                //string jsonData = jsonDataBuilder.ToString();
                //return jsonData;

            }
            catch (Exception ex)
            {
                barcoderv = "Timeout Camera";
                if (!client.Connected || !IsSocketConnected(client.Client))
                {
                    Disconnect();
                    tcpConnect(ip, port, readtimeout);
                }

                return barcoderv;
            }

        }
        public string EmptyBuf()
        {
            try
            {
                return str_read.ReadToEnd();
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
        public string subRecive2()
        {

            string barcoderv = "";
            try
            {

                char[] _buf = new char[1000000];
                int receive1 = str_read.Read(_buf, 0, _buf.Length);
                barcoderv = new string(_buf);
                // string[] arr = barcoderv.Split(',');
                return barcoderv;

            }
            catch
            {
                if (client.Connected == false)
                {
                    bool st = tcpConnect(ip, port, readtimeout);
                }
                return "";
            }
        }
        private static bool IsSocketConnected(Socket socket)
        {
            try
            {
                if (socket == null) return false;
                return !(socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch (SocketException) { return false; }
            catch (ObjectDisposedException) { return false; }
        }
    }
}
