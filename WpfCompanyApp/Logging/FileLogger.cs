using System;
using System.IO;
using System.Text;

namespace WpfCompanyApp.Logging
{
    /// <summary>
    /// Ghi log ra file .txt, mỗi ngày 1 file, trong thư mục Logs cạnh file .exe.
    /// </summary>
    public class FileLogger
    {
        private readonly string _logFolder;

        public FileLogger()
        {
            // 📁 Thư mục Logs nằm cạnh file .exe
            _logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(_logFolder);
        }

        /// <summary>
        /// Ghi 1 dòng vào file: {prefix}_yyyy-MM-dd.txt
        /// </summary>
        private void WriteTextLog(string filePrefix, string line)
        {
            try
            {
                string fileName = Path.Combine(
                    _logFolder,
                    $"{filePrefix}_{DateTime.Now:yyyy-MM-dd}.txt"
                );

                File.AppendAllText(fileName, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // tùy bạn: có thể bỏ qua, hoặc sau này log chỗ khác
            }
        }

        public void LogMachine(string msg)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}";
            WriteTextLog("MachineLog", line);
        }

        public void LogRobotHistory(string msg)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}";
            WriteTextLog("RobotHistory", line);
        }
    }
}
