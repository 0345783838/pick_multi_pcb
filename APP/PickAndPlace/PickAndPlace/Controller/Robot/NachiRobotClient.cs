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
    public class NachiRobotClient : IDisposable
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private StreamReader _reader;
        private StreamWriter _writer;

        private readonly string _ipAddress;
        private readonly int _port;
        private readonly int _timeoutMs;

        /*
         * Đảm bảo tại một thời điểm chỉ có một hàm được phép
         * gửi/đọc dữ liệu trên socket.
         */
        private readonly SemaphoreSlim _ioLock =
            new SemaphoreSlim(1, 1);

        /*
         * Nếu CHOOK xuất hiện trong lúc SendCommandAsync đang chờ
         * response, trigger sẽ được lưu lại để CheckTriggerAsync
         * trả về true ở lần gọi tiếp theo.
         */
        private int _pendingTriggerCount;

        private bool _disposed;

        /// <summary>
        /// Khởi tạo TCP client giao tiếp với NACHI.
        ///
        /// IP 192.168.1.1 và port 10030 là giá trị mẫu
        /// trong tài liệu NACHI, cần thay bằng cấu hình thực tế.
        /// </summary>
        public NachiRobotClient(
            string ipAddress = "192.168.1.1",
            int port = 10030,
            int timeoutMs = 3000)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                throw new ArgumentException(
                    "IP address không được để trống.",
                    nameof(ipAddress));
            }

            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(port),
                    "Port phải nằm trong khoảng 1 đến 65535.");
            }

            if (timeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeoutMs),
                    "Timeout phải lớn hơn 0.");
            }

            _ipAddress = ipAddress;
            _port = port;
            _timeoutMs = timeoutMs;
        }

        // ================================================================
        // 1. KẾT NỐI
        // ================================================================

        public async Task<bool> ConnectAsync()
        {
            ThrowIfDisposed();

            Disconnect();

            try
            {
                _client = new TcpClient
                {
                    NoDelay = true,
                    ReceiveTimeout = _timeoutMs,
                    SendTimeout = _timeoutMs
                };

                AppLogger.Instance.Info(
                    $"[PC -> NACHI] Connecting {_ipAddress}:{_port}",
                    "ROBOT");

                Task connectTask =
                    _client.ConnectAsync(_ipAddress, _port);

                Task completedTask = await Task.WhenAny(
                    connectTask,
                    Task.Delay(_timeoutMs));

                if (completedTask != connectTask)
                {
                    Disconnect();

                    AppLogger.Instance.Error(
                        $"Kết nối NACHI timeout sau {_timeoutMs} ms.",
                        "ROBOT");

                    return false;
                }

                // Lấy exception của ConnectAsync nếu có.
                await connectTask;

                _stream = _client.GetStream();

                /*
                 * Dùng StreamReader để đọc nguyên một message
                 * kết thúc bằng ký tự xuống dòng.
                 */
                _reader = new StreamReader(
                    _stream,
                    Encoding.ASCII,
                    false,
                    1024,
                    true);

                _writer = new StreamWriter(
                    _stream,
                    Encoding.ASCII,
                    1024,
                    true)
                {
                    AutoFlush = true,

                    /*
                     * Giữ đúng định dạng giống Dobot:
                     * mỗi command kết thúc bằng "\n".
                     */
                    NewLine = "\n"
                };

                AppLogger.Instance.Info(
                    $"Đã kết nối NACHI {_ipAddress}:{_port}",
                    "ROBOT");

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(
                    $"Không thể kết nối tới robot NACHI: {ex.Message}",
                    "ROBOT");

                Disconnect();
                return false;
            }
        }

        public bool IsConnected()
        {
            if (_client == null ||
                _client.Client == null ||
                !_client.Connected)
            {
                return false;
            }

            try
            {
                Socket socket = _client.Client;

                /*
                 * SelectRead=true và Available=0 thường có nghĩa
                 * kết nối đã bị đóng từ phía robot.
                 */
                bool socketReadable =
                    socket.Poll(1000, SelectMode.SelectRead);

                bool noDataAvailable =
                    socket.Available == 0;

                if (socketReadable && noDataAvailable)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                _writer?.Dispose();
            }
            catch
            {
                // Bỏ qua lỗi khi đóng.
            }

            try
            {
                _reader?.Dispose();
            }
            catch
            {
                // Bỏ qua lỗi khi đóng.
            }

            try
            {
                _stream?.Close();
            }
            catch
            {
                // Bỏ qua lỗi khi đóng.
            }

            try
            {
                _client?.Close();
            }
            catch
            {
                // Bỏ qua lỗi khi đóng.
            }

            _writer = null;
            _reader = null;
            _stream = null;
            _client = null;

            Interlocked.Exchange(
                ref _pendingTriggerCount,
                0);
        }

        // ================================================================
        // 2. GỬI COMMAND VÀ ĐỌC RESPONSE
        // ================================================================

        private async Task<string> SendCommandAsync(string command)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException(
                    "Command không được để trống.",
                    nameof(command));
            }

            if (!IsConnected() ||
                _stream == null ||
                _reader == null ||
                _writer == null)
            {
                throw new InvalidOperationException(
                    "Robot NACHI chưa được kết nối.");
            }

            await _ioLock.WaitAsync();

            try
            {
                AppLogger.Instance.Info(
                    $"[PC -> Robot] {command}",
                    "ROBOT");

                await _writer.WriteLineAsync(command);

                /*
                 * Chờ response chính.
                 *
                 * Nếu robot gửi CHOOK trong thời gian chờ,
                 * client ACK trigger rồi tiếp tục chờ response.
                 */
                while (true)
                {
                    string response =
                        await ReadLineWithTimeoutAsync();

                    if (response == null)
                    {
                        throw new IOException(
                            "Robot đã đóng kết nối.");
                    }

                    response = response.Trim();

                    if (response.Length == 0)
                    {
                        continue;
                    }

                    AppLogger.Instance.Info(
                        $"[Robot -> PC] {response}",
                        "ROBOT");

                    if (string.Equals(
                        response,
                        "CHOOK",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        await SendTriggerAckCoreAsync();

                        Interlocked.Increment(
                            ref _pendingTriggerCount);

                        continue;
                    }

                    return response;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(
                    $"Lỗi giao tiếp NACHI: {ex.Message}",
                    "ROBOT");

                return null;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private async Task<string> ReadLineWithTimeoutAsync()
        {
            Task<string> readTask =
                _reader.ReadLineAsync();

            Task completedTask = await Task.WhenAny(
                readTask,
                Task.Delay(_timeoutMs));

            if (completedTask != readTask)
            {
                throw new TimeoutException(
                    $"Không nhận được phản hồi từ robot sau {_timeoutMs} ms.");
            }

            return await readTask;
        }

        private async Task SendTriggerAckCoreAsync()
        {
            await _writer.WriteLineAsync(
                "READ_TRIGGER_OK");

            AppLogger.Instance.Info(
                "[PC -> Robot] READ_TRIGGER_OK",
                "ROBOT");
        }

        // ================================================================
        // 3. GET POSE
        // ================================================================

        /// <summary>
        /// Gửi:
        /// GET_POSE
        ///
        /// Robot nên trả về:
        /// POSE,X,Y,Z,RX,RY,RZ
        ///
        /// Ví dụ:
        /// POSE,100.25,200.50,300.00,180.00,0.00,45.00
        /// </summary>
        public async Task<string> GetPoseAsync()
        {
            return await SendCommandAsync("GET_POSE");
        }

        // ================================================================
        // 4. MOVE
        // ================================================================

        /// <summary>
        /// Gửi:
        /// MOVE x y
        ///
        /// Robot trả:
        /// MOVE_OK
        /// hoặc MOVE_NG
        /// </summary>
        public async Task<bool> MoveAsync(
            double x,
            double y)
        {
            string command = string.Format(
                CultureInfo.InvariantCulture,
                "MOVE {0:0.###} {1:0.###}",
                x,
                y);

            string response =
                await SendCommandAsync(command);

            return string.Equals(
                response,
                "MOVE_OK",
                StringComparison.OrdinalIgnoreCase);
        }

        // ================================================================
        // 5. PICK
        // ================================================================

        /// <summary>
        /// Gửi:
        /// PICK x y rz
        ///
        /// Robot trả:
        /// PICK_OK
        /// hoặc PICK_NG
        /// </summary>
        public async Task<bool> PickAsync(
            double x,
            double y,
            double rz)
        {
            string command = string.Format(
                CultureInfo.InvariantCulture,
                "PICK {0:0.###} {1:0.###} {2:0.###}",
                x,
                y,
                rz);

            string response =
                await SendCommandAsync(command);

            return string.Equals(
                response,
                "PICK_OK",
                StringComparison.OrdinalIgnoreCase);
        }

        // ================================================================
        // 6. TEST
        // ================================================================

        /// <summary>
        /// Gửi:
        /// TEST x y rz
        ///
        /// Robot trả:
        /// TEST_OK
        /// hoặc TEST_NG
        /// </summary>
        public async Task<bool> TestAsync(
            double x,
            double y,
            double rz)
        {
            string command = string.Format(
                CultureInfo.InvariantCulture,
                "TEST {0:0.###} {1:0.###} {2:0.###}",
                x,
                y,
                rz);

            string response =
                await SendCommandAsync(command);

            return string.Equals(
                response,
                "TEST_OK",
                StringComparison.OrdinalIgnoreCase);
        }

        // ================================================================
        // 7. TRIGGER ROBOT -> PC
        // ================================================================

        /// <summary>
        /// Kiểm tra robot có gửi CHOOK hay không.
        ///
        /// Khi nhận CHOOK, PC tự động gửi:
        /// READ_TRIGGER_OK
        /// </summary>
        public async Task<bool> CheckTriggerAsync()
        {
            ThrowIfDisposed();

            /*
             * Trigger có thể đã được nhận trong lúc một command
             * khác đang chờ response.
             */
            if (TryConsumePendingTrigger())
            {
                return true;
            }

            if (_stream == null ||
                _reader == null ||
                _writer == null ||
                !IsConnected())
            {
                return false;
            }

            await _ioLock.WaitAsync();

            try
            {
                /*
                 * Kiểm tra lại sau khi lấy lock,
                 * vì trigger có thể vừa được command khác nhận.
                 */
                if (TryConsumePendingTrigger())
                {
                    return true;
                }

                if (!_stream.DataAvailable)
                {
                    return false;
                }

                string message =
                    await ReadLineWithTimeoutAsync();

                if (string.IsNullOrWhiteSpace(message))
                {
                    return false;
                }

                message = message.Trim();

                AppLogger.Instance.Info(
                    $"[Robot -> PC] {message}",
                    "ROBOT");

                if (!string.Equals(
                    message,
                    "CHOOK",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                await SendTriggerAckCoreAsync();

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(
                    $"Lỗi đọc trigger NACHI: {ex.Message}",
                    "ROBOT");

                return false;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private bool TryConsumePendingTrigger()
        {
            while (true)
            {
                int currentValue =
                    Volatile.Read(ref _pendingTriggerCount);

                if (currentValue <= 0)
                {
                    return false;
                }

                int originalValue =
                    Interlocked.CompareExchange(
                        ref _pendingTriggerCount,
                        currentValue - 1,
                        currentValue);

                if (originalValue == currentValue)
                {
                    return true;
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    nameof(NachiRobotClient));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            Disconnect();

            _ioLock.Dispose();
        }
    }
}