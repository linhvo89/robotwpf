using NModbus;
using NModbus.IO;
using Serilog;
using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace WpfCompanyApp.Services
{
    /// <summary>
    /// Continuously reads the PLC ready and vacuum sensor coils over Modbus RTU.
    /// Coil values are cached so the robot cycle never blocks on a COM read.
    /// </summary>
    public sealed class ModbusRtuToolSensorService : IDisposable
    {
        private const ushort Machine1FullAddress = 20480;
        private const ushort Machine2FullAddress = 20481;
        private const ushort Basket1ReadyAddress = 20482;
        private const ushort Basket2ReadyAddress = 20483;
        private const ushort AirPressureReadyAddress = 20484;
        private const ushort Tool1Address = 20485; // Tool1: cảm biến đầu hút PLC X5
        private const ushort Tool2Address = 20486; // Tool2: cảm biến đầu hút PLC X6
        private const ushort Tool3Address = 20487; // Tool3: cảm biến đầu hút PLC X7

        private readonly INIFile _ini;
        private readonly object _sync = new object();
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private SerialPort? _serialPort;
        private IModbusSerialMaster? _master;
        private int _disposed;

        private volatile bool _machine1Full;
        private volatile bool _machine2Full;
        private volatile bool _basket1Ready;
        private volatile bool _basket2Ready;
        private volatile bool _airPressureReady;
        private volatile bool _tool1Holding;
        private volatile bool _tool2Holding;
        private volatile bool _tool3Holding;
        private volatile bool _communicationHealthy;
        private bool _hasConnectedSuccessfully;
        private bool _connectionFailureNotified;

        public Action<string>? ConnectionStatusChanged { get; set; }
        public bool IsCommunicationHealthy => _communicationHealthy;
        public bool IsMachine1Full => _machine1Full;
        public bool IsMachine2Full => _machine2Full;
        public bool IsBasket1Ready => _basket1Ready;
        public bool IsBasket2Ready => _basket2Ready;
        public bool IsAirPressureReady => _airPressureReady;

        public ModbusRtuToolSensorService(INIFile ini)
        {
            _ini = ini;
        }

        public void Start(Action<string>? statusCallback = null)
        {
            if (statusCallback != null)
                ConnectionStatusChanged = statusCallback;

            if (!string.Equals(_ini.Read("Activated", "MobusRTU").Trim(), "ENABLE",
                    StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("[MODBUS RTU] MobusRTU is disabled.");
                ConnectionStatusChanged?.Invoke(
                    "[MODBUS RTU] Kết nối PLC đang bị tắt trong file INI (Activated không phải ENABLE).");
                return;
            }

            string portName = _ini.Read("Name", "MobusRTU").Trim();
            if (string.IsNullOrWhiteSpace(portName))
                portName = "COM1";

            ConnectionStatusChanged?.Invoke(
                $"[MODBUS RTU] Chế độ đọc theo yêu cầu: {portName}, " +
                $"{ReadInt("BaudRate", 19200)} baud, SlaveId={ReadByte("SlaveId", 1)}. " +
                "Không chạy vòng quét nền.");
        }

        public bool IsToolHolding(int tool)
        {
            return tool switch
            {
                1 => _tool1Holding,
                2 => _tool2Holding,
                3 => _tool3Holding,
                _ => false
            };
        }

        /// <summary>
        /// Đọc trực tiếp PLC tại thời điểm gọi, không dùng giá trị cache của vòng quét nền.
        /// </summary>
        public bool TryReadToolHoldingNow(int tool, out bool isHolding)
        {
            isHolding = false;
            if (tool < 1 || tool > 3)
                return false;

            try
            {
                lock (_sync)
                {
                    EnsureConnected();
                    ReadAndUpdateAllCoils();
                    isHolding = tool switch
                    {
                        1 => _tool1Holding,
                        2 => _tool2Holding,
                        3 => _tool3Holding,
                        _ => false
                    };
                }

                return true;
            }
            catch (Exception ex)
            {
                _communicationHealthy = false;
                Log.Warning("[MODBUS RTU] Direct tool sensor read error: {Message}", ex.Message);
                NotifyCommunicationFailure(ex.Message);
                return false;
            }
        }

        public bool TryReadAllNow()
        {
            try
            {
                lock (_sync)
                {
                    EnsureConnected();
                    ReadAndUpdateAllCoils();
                }

                return true;
            }
            catch (Exception ex)
            {
                _communicationHealthy = false;
                Log.Warning("[MODBUS RTU] On-demand read error: {Message}", ex.Message);
                NotifyCommunicationFailure(ex.Message);
                CloseConnection();
                return false;
            }
        }

        private void ReadAndUpdateAllCoils()
        {
            byte slaveId = ReadByte("SlaveId", 1);
            bool[] values = _master!.ReadCoils(
                slaveId,
                Machine1FullAddress,
                Tool3Address - Machine1FullAddress + 1);

            _machine1Full = values[0];
            _machine2Full = values[1];
            _basket1Ready = values[2];
            _basket2Ready = values[3];
            _airPressureReady = values[4];
            _tool1Holding = values[5];
            _tool2Holding = values[6];
            _tool3Holding = values[7];
            _communicationHealthy = true;
            NotifyCommunicationRestored();
        }

        private async Task ReadLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    lock (_sync)
                    {
                        EnsureConnected();
                        ReadAndUpdateAllCoils();
                    }
                    await Task.Delay(50, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ClearSensorValues();
                    Log.Warning("[MODBUS RTU] Read error: {Message}", ex.Message);
                    NotifyCommunicationFailure(ex.Message);
                    CloseConnection();

                    try
                    {
                        await Task.Delay(1000, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            CloseConnection();
        }

        private void NotifyCommunicationRestored()
        {
            if (_connectionFailureNotified)
            {
                _connectionFailureNotified = false;
                _hasConnectedSuccessfully = true;
                ConnectionStatusChanged?.Invoke(
                    "[MODBUS RTU] Đã kết nối lại PLC thành công.");
                return;
            }

            if (!_hasConnectedSuccessfully)
            {
                _hasConnectedSuccessfully = true;
                ConnectionStatusChanged?.Invoke(
                    "[MODBUS RTU] Kết nối PLC thành công.");
            }
        }

        private void NotifyCommunicationFailure(string error)
        {
            if (_connectionFailureNotified)
                return;

            _connectionFailureNotified = true;
            string status = _hasConnectedSuccessfully
                ? "Mất kết nối PLC"
                : "Không thể kết nối PLC";
            ConnectionStatusChanged?.Invoke(
                $"[MODBUS RTU] {status}: {error}. Đang tự động kết nối lại...");
        }

        private void EnsureConnected()
        {
            if (_serialPort != null && _serialPort.IsOpen && _master != null)
                return;

            string portName = _ini.Read("Name", "MobusRTU").Trim();
            if (string.IsNullOrWhiteSpace(portName))
                portName = "COM1";

            int baudRate = ReadInt("BaudRate", 19200);
            int dataBits = ReadInt("DataBits", 8);
            int stopBitsValue = ReadInt("StopBits", 1);
            int timeout = ReadInt("TimeOut", 1000);
            bool evenParity = ReadInt("EVenParity", 0) == 1;

            _serialPort = new SerialPort(
                portName,
                baudRate,
                evenParity ? Parity.Even : Parity.None,
                dataBits,
                stopBitsValue == 2 ? StopBits.Two : StopBits.One)
            {
                ReadTimeout = timeout,
                WriteTimeout = timeout
            };
            _serialPort.Open();

            var factory = new ModbusFactory();
            _master = factory.CreateRtuMaster(new SerialPortStreamResource(_serialPort));
            _master.Transport.ReadTimeout = timeout;
            _master.Transport.WriteTimeout = timeout;
            _master.Transport.Retries = 0;

            Log.Information(
                "[MODBUS RTU] Connected {Port}, {BaudRate},{DataBits},{Parity},{StopBits}; SlaveId={SlaveId}; coils={Start}-{End}; function=01.",
                portName, baudRate, dataBits, evenParity ? "E" : "N", stopBitsValue,
                ReadByte("SlaveId", 1), Machine1FullAddress, Tool3Address);
        }

        private int ReadInt(string key, int defaultValue)
        {
            return int.TryParse(_ini.Read(key, "MobusRTU").Trim(), out int value)
                ? value
                : defaultValue;
        }

        private byte ReadByte(string key, byte defaultValue)
        {
            return byte.TryParse(_ini.Read(key, "MobusRTU").Trim(), out byte value)
                ? value
                : defaultValue;
        }

        private void ClearSensorValues()
        {
            _communicationHealthy = false;
            _machine1Full = false;
            _machine2Full = false;
            _basket1Ready = false;
            _basket2Ready = false;
            _airPressureReady = false;
            _tool1Holding = false;
            _tool2Holding = false;
            _tool3Holding = false;
        }

        private void CloseConnection()
        {
            _master?.Dispose();
            _master = null;

            if (_serialPort != null)
            {
                try
                {
                    if (_serialPort.IsOpen)
                        _serialPort.Close();
                }
                catch
                {
                    // The port may already be unavailable after a cable/device error.
                }

                _serialPort.Dispose();
                _serialPort = null;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _cts?.Cancel();
            try
            {
                _readTask?.Wait(1500);
            }
            catch (AggregateException)
            {
            }

            CloseConnection();
            _cts?.Dispose();
        }

        // This adapter is called by NModbus. Mark it as infrastructure code so
        // Visual Studio "Just My Code" does not pause here for an expected COM
        // timeout before ReadLoopAsync gets a chance to handle and reconnect.
        [System.Diagnostics.DebuggerNonUserCode]
        private sealed class SerialPortStreamResource : IStreamResource
        {
            private readonly SerialPort _port;

            public SerialPortStreamResource(SerialPort port)
            {
                _port = port;
            }

            public int InfiniteTimeout => SerialPort.InfiniteTimeout;

            public int ReadTimeout
            {
                get => _port.ReadTimeout;
                set => _port.ReadTimeout = value;
            }

            public int WriteTimeout
            {
                get => _port.WriteTimeout;
                set => _port.WriteTimeout = value;
            }

            public void DiscardInBuffer() => _port.DiscardInBuffer();

            public int Read(byte[] buffer, int offset, int count) =>
                _port.Read(buffer, offset, count);

            public void Write(byte[] buffer, int offset, int count) =>
                _port.Write(buffer, offset, count);

            public void Dispose()
            {
                // The owning service closes and disposes the SerialPort.
            }
        }
    }
}
