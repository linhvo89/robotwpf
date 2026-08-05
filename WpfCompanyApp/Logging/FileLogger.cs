using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace WpfCompanyApp.Logging
{
    /// <summary>
    /// Đưa log vào hàng đợi và ghi theo batch trên một worker nền.
    /// Luồng điều khiển máy không phải chờ thao tác mở/ghi/đóng file.
    /// </summary>
    public sealed class FileLogger : IDisposable
    {
        private const int MaxBatchSize = 100;
        private static readonly TimeSpan BatchWait = TimeSpan.FromMilliseconds(200);

        private readonly string _logFolder;
        private readonly BlockingCollection<LogEntry> _queue =
            new BlockingCollection<LogEntry>(new ConcurrentQueue<LogEntry>());
        private readonly Thread _writerThread;
        private int _disposed;

        private sealed class LogEntry
        {
            public string Prefix { get; set; } = "";
            public string Line { get; set; } = "";
            public DateTime Timestamp { get; set; }
        }

        public FileLogger()
        {
            _logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(_logFolder);

            _writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "KBOT File Log Writer"
            };
            _writerThread.Start();
        }

        public void LogMachine(string msg) => Enqueue("MachineLog", msg);

        public void LogRobotHistory(string msg) => Enqueue("RobotHistory", msg);

        private void Enqueue(string prefix, string message)
        {
            if (Volatile.Read(ref _disposed) != 0 || _queue.IsAddingCompleted)
                return;

            DateTime now = DateTime.Now;
            try
            {
                _queue.Add(new LogEntry
                {
                    Prefix = prefix,
                    Timestamp = now,
                    Line = $"{now:yyyy-MM-dd HH:mm:ss}  {message}"
                });
            }
            catch (InvalidOperationException)
            {
                // Dispose vừa đóng hàng đợi; không nhận thêm log mới.
            }
        }

        private void WriterLoop()
        {
            var batch = new List<LogEntry>(MaxBatchSize);

            while (!_queue.IsCompleted)
            {
                if (!_queue.TryTake(out LogEntry? first, BatchWait))
                    continue;

                batch.Add(first);
                DateTime deadlineUtc = DateTime.UtcNow.Add(BatchWait);
                while (batch.Count < MaxBatchSize)
                {
                    TimeSpan remainingWait = deadlineUtc - DateTime.UtcNow;
                    if (remainingWait <= TimeSpan.Zero ||
                        !_queue.TryTake(out LogEntry? next, remainingWait))
                        break;

                    batch.Add(next);
                }

                WriteBatch(batch);
                batch.Clear();
            }

            while (_queue.TryTake(out LogEntry? remaining))
                batch.Add(remaining);

            if (batch.Count > 0)
                WriteBatch(batch);
        }

        private void WriteBatch(IReadOnlyCollection<LogEntry> batch)
        {
            foreach (var group in batch.GroupBy(x => new
                     {
                         x.Prefix,
                         Date = x.Timestamp.Date
                     }))
            {
                try
                {
                    string fileName = Path.Combine(
                        _logFolder,
                        $"{group.Key.Prefix}_{group.Key.Date:yyyy-MM-dd}.txt");
                    string text = string.Join(Environment.NewLine, group.Select(x => x.Line))
                                  + Environment.NewLine;
                    File.AppendAllText(fileName, text, Encoding.UTF8);
                }
                catch
                {
                    // Lỗi ghi log không được làm dừng worker hoặc chương trình máy.
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _queue.CompleteAdding();
            _writerThread.Join(TimeSpan.FromSeconds(5));
            _queue.Dispose();
        }
    }
}
