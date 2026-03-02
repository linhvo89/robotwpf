using NModbus;
using Serilog;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace WpfCompanyApp.Services
{
    public class ModbusService
    {
        private static readonly Lazy<ModbusService> _instance = new(() => new ModbusService());
        public static ModbusService Instance => _instance.Value;
        private ModbusService() { }

        private TcpClient? _client;
        private IModbusMaster? _master;
        private CancellationTokenSource? _cts;

        private string _ip = string.Empty;
        private int _port;
        private ushort _startAddress;
        private ushort _numRegisters;
        private Action<ushort[]>? _onDataReceived;

        public Action<string>? OnLog;

        private readonly object _modbusLock = new(); // ✅ khóa dùng chung cho đọc/ghi

        public bool IsConnected => _client != null && _client.Connected;

        public async Task StartReadingAsync(string ip, int port, ushort startAddress, ushort numRegisters, Action<ushort[]> onDataReceived)
        {
            _ip = ip;
            _port = port;
            _startAddress = startAddress;
            _numRegisters = numRegisters;
            _onDataReceived = onDataReceived;

            _cts = new CancellationTokenSource();
            OnLog?.Invoke($"📡 Bắt đầu đọc Modbus TCP: IP={ip}, Port={port}, Start={startAddress}, Count={numRegisters}");
            Log.Information("📡 Bắt đầu đọc Modbus TCP: IP={Ip}, Port={Port}, Start={Start}, Count={Count}",
               ip, port, startAddress, numRegisters);

            await Task.Run(async () => await ReadLoop(_cts.Token));
        }

        private async Task ReadLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_client == null || !_client.Connected)
                    {
                        Log.Information("🔄 Đang kết nối tới Modbus server {Ip}:{Port}...", _ip, _port);
                        OnLog?.Invoke($"🔄 Đang kết nối tới Modbus server {_ip}:{_port}");

                        _client = new TcpClient();
                        await _client.ConnectAsync(_ip, _port);
                        var factory = new ModbusFactory();
                        _master = factory.CreateMaster(_client);

                        Log.Information("✅ Kết nối Modbus thành công.");
                        OnLog?.Invoke("✅ Kết nối Modbus thành công.");
                    }

                    ushort[] registers;
                    lock (_modbusLock) // ✅ đảm bảo chỉ 1 luồng thao tác với _master
                    {
                        registers = _master!.ReadHoldingRegisters(1, _startAddress, _numRegisters);
                    }

                    _onDataReceived?.Invoke(registers);
                    Log.Debug("📗 Đọc thành công {Count} thanh ghi: {Data}", _numRegisters, string.Join(", ", registers));
                    OnLog?.Invoke($"📗 Đọc thành công {_numRegisters} thanh ghi: {string.Join(", ", registers)}");
                }
                catch (Exception ex)
                {
                    Log.Warning("⚠️ Lỗi Modbus: {Message}", ex.Message);
                    OnLog?.Invoke($"⚠️ Lỗi Modbus: {ex.Message}");
                    _client?.Close();
                    _client = null;
                    await Task.Delay(3000, token);
                    continue;
                }

                await Task.Delay(500, token); // đọc mỗi 0.5 giây
            }
        }

        public void StopReading()
        {
            _cts?.Cancel();
            _client?.Close();
            _client = null;
            Log.Information("🛑 Dừng đọc Modbus TCP.");
        }

        // ✅ Ghi 1 thanh ghi, có khóa tránh xung đột với đọc
        public void WriteSingleRegister(byte slaveId, ushort address, ushort value)
        {
            if (_master == null || _client == null || !_client.Connected)
            {
                OnLog?.Invoke("⚠️ Không có kết nối Modbus để ghi dữ liệu!");
                Log.Warning("⚠️ Không có kết nối Modbus để ghi dữ liệu!");
                return;
            }

            try
            {
                lock (_modbusLock) // ✅ ngăn vòng đọc chạy trong lúc ghi
                {
                    _master.WriteSingleRegister(slaveId, address, value);
                }

                OnLog?.Invoke($"✏️ Đã ghi thanh ghi {address} = {value}");
                Log.Information("✏️ Đã ghi thanh ghi {Address} = {Value}", address, value);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"❌ Lỗi khi ghi thanh ghi {address}: {ex.Message}");
                Log.Error("❌ Lỗi khi ghi thanh ghi {Address}: {Message}", address, ex.Message);
            }
        }
    }
}
