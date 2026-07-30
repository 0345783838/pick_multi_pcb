using PickAndPlace.Controllers;
using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PickAndPlace.Controller.Robot
{
    /// <summary>
    /// Client TCP/IP giao tiếp với robot Nachi CFDs.
    ///
    /// API được tổ chức tương tự DobotRobotClient, nhưng vẫn giữ đúng giao thức Nachi:
    /// - Robot/controller đóng vai trò TCP Server.
    /// - PC đóng vai trò TCP Client.
    /// - Robot gửi về CSV: X,Y,Z,Roll,Pitch,Yaw,IO,
    /// - PC gửi 6 trường fixed-width, mỗi trường 8 ký tự và thêm null terminator:
    ///   dX, dY, dZ, dRoll, dPitch, dYaw.
    ///
    /// Lưu ý: các giá trị Move của Nachi là dữ liệu ghi vào shift register R1,
    /// không mặc định là tọa độ tuyệt đối như một số API robot khác.
    /// </summary>
    public class NachiRobotClient : IDisposable
    {
        public const int DefaultPort = 10030;
        public const int DefaultTimeoutMs = 10000;

        private const int FieldWidth = 8;
        private const int NumberOfFields = 6;
        private const int SendPayloadLength = FieldWidth * NumberOfFields; // 48 byte

        // 6 trường pose, mỗi trường tối đa 12 ký tự; IO 3 ký tự; 7 dấu phẩy.
        private const int ReceiveBufferLength = (12 * 6) + 3 + 7;

        private readonly string _ipAddress;
        private readonly int _port;
        private readonly int _timeoutMs;
        private readonly SemaphoreSlim _communicationLock = new SemaphoreSlim(1, 1);

        private TcpClient _client;
        private NetworkStream _stream;
        private bool _disposed;

        /// <summary>Z offset gửi xuống khi thực hiện bước hạ Z để pick.</summary>
        public double PickZOffset { get; set; }

        /// <summary>Z offset gửi xuống khi thực hiện bước hạ Z để place.</summary>
        public double PlaceZOffset { get; set; }

        /// <summary>Khoảng chờ giữa các bước chuyển động liên tiếp.</summary>
        public int MotionDelayMs { get; set; }

        /// <summary>
        /// Giá trị IO được xem là trigger.
        /// Đặt null/rỗng nếu muốn coi mọi giá trị IO khác 0 là trigger.
        /// </summary>
        public string TriggerIOValue { get; set; }

        public double CurrentX { get; private set; }
        public double CurrentY { get; private set; }
        public double CurrentZ { get; private set; }
        public double CurrentRoll { get; private set; }
        public double CurrentPitch { get; private set; }
        public double CurrentYaw { get; private set; }
        public string CurrentIO { get; private set; }

        public NachiRobotClient(
            string ipAddress,
            int port = DefaultPort,
            int timeoutMs = DefaultTimeoutMs)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                throw new ArgumentException("Địa chỉ IP robot không hợp lệ.", "ipAddress");

            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException("port");

            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException("timeoutMs");

            _ipAddress = ipAddress;
            _port = port;
            _timeoutMs = timeoutMs;

            PickZOffset = -50.0;
            PlaceZOffset = -50.0;
            MotionDelayMs = 300;
            TriggerIOValue = "001";
            CurrentIO = "000";
        }

        // =====================================================================
        // 1. KẾT NỐI VÀ CHECK KẾT NỐI
        // =====================================================================

        public async Task<bool> ConnectAsync()
        {
            ThrowIfDisposed();

            await _communicationLock.WaitAsync();
            try
            {
                CleanupSocket();

                _client = new TcpClient();
                _client.ReceiveTimeout = _timeoutMs;
                _client.SendTimeout = _timeoutMs;

                Task connectTask = _client.ConnectAsync(_ipAddress, _port);
                Task completedTask = await Task.WhenAny(
                    connectTask,
                    Task.Delay(_timeoutMs));

                if (completedTask != connectTask)
                {
                    CleanupSocket();
                    LogError("Kết nối Nachi timeout sau " + _timeoutMs + " ms.");
                    return false;
                }

                // Quan sát exception của ConnectAsync nếu có.
                await connectTask;

                _stream = _client.GetStream();

                LogInfo("Đã kết nối Nachi " + _ipAddress + ":" + _port);

                // Theo chương trình socket hiện tại của Nachi, controller gửi pose + IO
                // ngay sau khi SOCKWAIT chấp nhận kết nối.
                string initialMessage = await ReadPositionMessageAsync(true);
                if (string.IsNullOrWhiteSpace(initialMessage))
                {
                    CleanupSocket();
                    LogError("Đã mở socket nhưng không nhận được pose ban đầu từ Nachi.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                CleanupSocket();
                LogError("Không thể kết nối tới Nachi: " + ex.Message);
                return false;
            }
            finally
            {
                _communicationLock.Release();
            }
        }

        public bool IsConnected()
        {
            if (_client == null ||
                _client.Client == null ||
                !_client.Connected ||
                _stream == null)
            {
                return false;
            }

            try
            {
                Socket socket = _client.Client;

                // Socket readable nhưng không còn byte nào => đầu bên kia đã đóng kết nối.
                bool readable = socket.Poll(1000, SelectMode.SelectRead);
                bool noData = socket.Available == 0;

                return !(readable && noData);
            }
            catch
            {
                return false;
            }
        }

        public void Disconnect()
        {
            CleanupSocket();
            LogInfo("Đã ngắt kết nối Nachi.");
        }

        // =====================================================================
        // 2. ĐỌC GIÁ TRỊ POSE
        // =====================================================================

        /// <summary>
        /// Trả về pose gần nhất theo format tương tự Dobot:
        /// POSE,X,Y,Z,ROLL,PITCH,YAW,IO
        ///
        /// Nachi protocol hiện tại không có lệnh GET_POSE riêng. Nếu socket đang có
        /// dữ liệu mới, hàm sẽ đọc và cập nhật pose; nếu không, trả về pose cache.
        /// </summary>
        public async Task<string> GetPoseAsync()
        {
            ThrowIfDisposed();

            if (!IsConnected())
                throw new InvalidOperationException("Robot Nachi chưa được kết nối.");

            await _communicationLock.WaitAsync();
            try
            {
                if (_stream.DataAvailable)
                    await ReadPositionMessageAsync(false);

                return BuildPoseResponse();
            }
            finally
            {
                _communicationLock.Release();
            }
        }

        public RobotPosition GetCurrentPosition()
        {
            return new RobotPosition(
                CurrentX,
                CurrentY,
                CurrentZ,
                CurrentRoll,
                CurrentPitch,
                CurrentYaw);
        }

        // =====================================================================
        // 3. GỬI LỆNH MOVE
        // =====================================================================

        /// <summary>
        /// Gửi shift X/Y vào thanh ghi R1 của Nachi.
        /// Đây là shift/correction, không đảm bảo là tọa độ tuyệt đối.
        /// </summary>
        public Task<bool> MoveAsync(double x, double y)
        {
            return SendShiftRegisterAsync(x, y, 0.0, 0.0, 0.0, 0.0);
        }

        /// <summary>Overload đầy đủ 6 bậc tự do của thanh ghi shift R1.</summary>
        public Task<bool> MoveAsync(
            double x,
            double y,
            double z,
            double roll,
            double pitch,
            double yaw)
        {
            return SendShiftRegisterAsync(x, y, z, roll, pitch, yaw);
        }

        // =====================================================================
        // 4. GỬI LỆNH PICK
        // =====================================================================

        /// <summary>
        /// Giữ lại logic Pick của class Nachi cũ:
        /// 1. Gửi X/Y/RZ tại độ cao approach.
        /// 2. Chờ MotionDelayMs.
        /// 3. Gửi lại X/Y/RZ kèm PickZOffset.
        ///
        /// Việc bật vacuum/gripper phải do chương trình robot xử lý hoặc được bổ sung
        /// bằng một giao thức IO riêng.
        /// </summary>
        public async Task<bool> PickAsync(double x, double y, double rz)
        {
            bool approachOk = await SendShiftRegisterAsync(
                x, y, 0.0, rz, 0.0, 0.0);

            if (!approachOk)
                return false;

            await Task.Delay(MotionDelayMs);

            return await SendShiftRegisterAsync(
                x, y, PickZOffset, rz, 0.0, 0.0);
        }

        /// <summary>
        /// Hàm bổ sung từ class Nachi cũ: approach -> hạ Z -> nâng Z.
        /// </summary>
        public async Task<bool> PlaceAsync(double x, double y, double rz)
        {
            bool approachOk = await SendShiftRegisterAsync(
                x, y, 0.0, rz, 0.0, 0.0);

            if (!approachOk)
                return false;

            await Task.Delay(MotionDelayMs);

            bool lowerOk = await SendShiftRegisterAsync(
                x, y, PlaceZOffset, rz, 0.0, 0.0);

            if (!lowerOk)
                return false;

            await Task.Delay(MotionDelayMs);

            return await SendShiftRegisterAsync(
                x, y, 0.0, rz, 0.0, 0.0);
        }

        // =====================================================================
        // 5. GỬI LỆNH TEST
        // =====================================================================

        /// <summary>
        /// Nachi không có command text "TEST". Hàm này chỉ gửi X/Y/RZ vào R1
        /// mà không thực hiện bước hạ Z.
        /// </summary>
        public Task<bool> TestAsync(double x, double y, double rz)
        {
            return SendShiftRegisterAsync(x, y, 0.0, rz, 0.0, 0.0);
        }

        // =====================================================================
        // 6. ĐỌC TRIGGER TỪ ROBOT
        // =====================================================================

        /// <summary>
        /// Đọc packet pose/IO mới nếu robot đang gửi dữ liệu.
        /// Trả về true khi IO khớp TriggerIOValue.
        ///
        /// Không gửi "READ_TRIGGER_OK" vì chuỗi đó không thuộc protocol fixed-width
        /// của Nachi và có thể làm sai packet 49 byte mà controller đang chờ.
        /// </summary>
        public async Task<bool> CheckTriggerAsync()
        {
            ThrowIfDisposed();

            if (_stream == null || !IsConnected())
                return false;

            await _communicationLock.WaitAsync();
            try
            {
                if (!_stream.DataAvailable)
                    return false;

                string message = await ReadPositionMessageAsync(false);
                if (string.IsNullOrWhiteSpace(message))
                    return false;

                bool triggered = IsTriggerIO(CurrentIO);

                if (triggered)
                {
                    LogInfo(
                        "Nhận trigger từ Nachi. IO=" + CurrentIO +
                        ", Pose=" + BuildPoseResponse());
                }

                return triggered;
            }
            catch (Exception ex)
            {
                LogError("Lỗi đọc trigger từ Nachi: " + ex.Message);
                return false;
            }
            finally
            {
                _communicationLock.Release();
            }
        }

        // =====================================================================
        // INTERNAL: GỬI FIXED-WIDTH SHIFT REGISTER
        // =====================================================================

        private async Task<bool> SendShiftRegisterAsync(
            double dx,
            double dy,
            double dz,
            double dRoll,
            double dPitch,
            double dYaw)
        {
            ThrowIfDisposed();

            if (!IsConnected())
                return false;

            string payload;

            try
            {
                payload =
                    FormatFixedWidthField(dx) +
                    FormatFixedWidthField(dy) +
                    FormatFixedWidthField(dz) +
                    FormatFixedWidthField(dRoll) +
                    FormatFixedWidthField(dPitch) +
                    FormatFixedWidthField(dYaw);
            }
            catch (Exception ex)
            {
                LogError("Dữ liệu shift Nachi không hợp lệ: " + ex.Message);
                return false;
            }

            byte[] sendBuffer = new byte[SendPayloadLength + 1];
            Encoding.ASCII.GetBytes(
                payload,
                0,
                SendPayloadLength,
                sendBuffer,
                0);

            // Null terminator giống sizeof(buffer) trong sample C/C++ của Nachi.
            sendBuffer[SendPayloadLength] = 0x00;

            await _communicationLock.WaitAsync();
            try
            {
                if (!IsConnected())
                    return false;

                await _stream.WriteAsync(sendBuffer, 0, sendBuffer.Length);
                await _stream.FlushAsync();

                LogInfo("[PC -> Nachi] " + payload);
                return true;
            }
            catch (Exception ex)
            {
                LogError("Lỗi gửi shift register tới Nachi: " + ex.Message);
                CleanupSocket();
                return false;
            }
            finally
            {
                _communicationLock.Release();
            }
        }

        /// <summary>
        /// Tạo đúng format tương đương %08.2f.
        /// Ví dụ:
        ///   50     -> "00050.00"
        ///   -50    -> "-0050.00"
        ///   1.25   -> "00001.25"
        /// </summary>
        private static string FormatFixedWidthField(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException("value", "Không hỗ trợ NaN/Infinity.");

            bool isNegative = value < 0.0;
            double absoluteValue = Math.Abs(value);

            string number = absoluteValue.ToString("F2", CultureInfo.InvariantCulture);
            int numberWidth = isNegative ? FieldWidth - 1 : FieldWidth;

            if (number.Length > numberWidth)
            {
                throw new ArgumentOutOfRangeException(
                    "value",
                    "Giá trị " + value.ToString(CultureInfo.InvariantCulture) +
                    " vượt quá field " + FieldWidth + " ký tự.");
            }

            number = number.PadLeft(numberWidth, '0');
            return isNegative ? "-" + number : number;
        }

        // =====================================================================
        // INTERNAL: ĐỌC VÀ PARSE POSE/IO
        // =====================================================================

        private async Task<string> ReadPositionMessageAsync(bool waitForData)
        {
            if (_stream == null)
                return null;

            if (!waitForData && !_stream.DataAvailable)
                return null;

            byte[] buffer = new byte[ReceiveBufferLength];
            int totalRead = 0;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(_timeoutMs);

            while (totalRead < buffer.Length)
            {
                int remainingMs = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remainingMs <= 0)
                    throw new TimeoutException("Timeout khi đọc pose/IO từ Nachi.");

                Task<int> readTask = _stream.ReadAsync(
                    buffer,
                    totalRead,
                    buffer.Length - totalRead);

                Task completedTask = await Task.WhenAny(
                    readTask,
                    Task.Delay(remainingMs));

                if (completedTask != readTask)
                {
                    CleanupSocket();
                    throw new TimeoutException("Timeout khi đọc pose/IO từ Nachi.");
                }

                int bytesRead = await readTask;
                if (bytesRead <= 0)
                {
                    CleanupSocket();
                    throw new IOException("Nachi đã đóng kết nối TCP.");
                }

                totalRead += bytesRead;

                if (IsCompletePositionMessage(buffer, totalRead))
                    break;
            }

            string raw = Encoding.ASCII
                .GetString(buffer, 0, totalRead)
                .Trim('\0', '\r', '\n', ' ');

            if (!TryParsePosition(raw))
                LogError("Sai format pose/IO từ Nachi: " + raw);
            else
                LogInfo("[Nachi -> PC] " + raw);

            return raw;
        }

        private static bool IsCompletePositionMessage(byte[] buffer, int length)
        {
            int commaCount = 0;

            for (int i = 0; i < length; i++)
            {
                if (buffer[i] == 0x00 || buffer[i] == (byte)'\n')
                    return true;

                if (buffer[i] == (byte)',')
                    commaCount++;
            }

            // X,Y,Z,Roll,Pitch,Yaw,IO, có tổng cộng 7 dấu phẩy.
            return commaCount >= 7;
        }

        private bool TryParsePosition(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string[] parts = raw.Split(',');
            if (parts.Length < 7)
                return false;

            double x = 0.0;
            double y = 0.0;
            double z = 0.0;
            double roll = 0.0;
            double pitch = 0.0;
            double yaw = 0.0;


            bool parseOk =
                double.TryParse(
                    parts[0].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out x) &&
                double.TryParse(
                    parts[1].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out y) &&
                double.TryParse(
                    parts[2].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out z) &&
                double.TryParse(
                    parts[3].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out roll) &&
                double.TryParse(
                    parts[4].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out pitch) &&
                double.TryParse(
                    parts[5].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out yaw);

            if (!parseOk)
                return false;

            CurrentX = x;
            CurrentY = y;
            CurrentZ = z;
            CurrentRoll = roll;
            CurrentPitch = pitch;
            CurrentYaw = yaw;
            CurrentIO = parts[6].Trim().Trim('\0');

            return true;
        }

        private string BuildPoseResponse()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "POSE,{0:F2},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2},{6}",
                CurrentX,
                CurrentY,
                CurrentZ,
                CurrentRoll,
                CurrentPitch,
                CurrentYaw,
                CurrentIO ?? string.Empty);
        }

        private bool IsTriggerIO(string ioValue)
        {
            if (string.IsNullOrWhiteSpace(ioValue))
                return false;

            string current = ioValue.Trim();

            if (string.IsNullOrWhiteSpace(TriggerIOValue))
            {
                int numericIO;
                if (int.TryParse(current, out numericIO))
                    return numericIO != 0;

                return current != "0" && current != "000";
            }

            string expected = TriggerIOValue.Trim();

            if (string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
                return true;

            // Cho phép "1" khớp với "001".
            int currentNumber;
            int expectedNumber;

            return int.TryParse(current, out currentNumber) &&
                   int.TryParse(expected, out expectedNumber) &&
                   currentNumber == expectedNumber;
        }

        // =====================================================================
        // CLEANUP / LOGGING
        // =====================================================================

        private void CleanupSocket()
        {
            if (_stream != null)
            {
                try { _stream.Close(); }
                catch { }
                _stream = null;
            }

            if (_client != null)
            {
                try { _client.Close(); }
                catch { }
                _client = null;
            }
        }

        private static void LogInfo(string message)
        {
            try
            {
                AppLogger.Instance.Info(message, "ROBOT");
            }
            catch
            {
                Console.WriteLine("[Nachi] " + message);
            }
        }

        private static void LogError(string message)
        {
            try
            {
                AppLogger.Instance.Error(message, "ROBOT");
            }
            catch
            {
                Console.WriteLine("[Nachi][Error] " + message);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException("NachiRobotClient");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            CleanupSocket();
            _communicationLock.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    public struct RobotPosition
    {
        public double X { get; private set; }
        public double Y { get; private set; }
        public double Z { get; private set; }
        public double Roll { get; private set; }
        public double Pitch { get; private set; }
        public double Yaw { get; private set; }

        public RobotPosition(
            double x,
            double y,
            double z,
            double roll,
            double pitch,
            double yaw)
        {
            X = x;
            Y = y;
            Z = z;
            Roll = roll;
            Pitch = pitch;
            Yaw = yaw;
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "X={0:F2}, Y={1:F2}, Z={2:F2}, Roll={3:F2}, Pitch={4:F2}, Yaw={5:F2}",
                X,
                Y,
                Z,
                Roll,
                Pitch,
                Yaw);
        }
    }
}
