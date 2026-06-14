using PickAndPlace.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace PickAndPlace.Controller.Robot
{
    public class DobotRobotClient : IDisposable
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private readonly string _ipAddress;
        private readonly int _port;
        private readonly int _timeoutMs;

        /// <summary>
        /// Khởi tạo kết nối. Theo tài liệu, IP mặc định cáp mạng là 192.168.5.1 [7] 
        /// và cổng mô phỏng thường là 8000 hoặc 6601 [6, 8].
        /// </summary>
        public DobotRobotClient(string ipAddress = "192.168.5.1", int port = 8000, int timeoutMs = 3000)
        {
            _ipAddress = ipAddress;
            _port = port;
            _timeoutMs = timeoutMs;
        }

        // 1. KẾT NỐI VÀ CHECK KẾT NỐI THỜI GIAN THỰC
        public async Task<bool> ConnectAsync()
        {
            try
            {
                _client = new TcpClient();
                _client.ReceiveTimeout = _timeoutMs;
                _client.SendTimeout = _timeoutMs;

                await _client.ConnectAsync(_ipAddress, _port);
                _stream = _client.GetStream();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Không thể kết nối tới Robot: {ex.Message}");
                return false;
            }
        }

        public bool IsConnected()
        {
            if (_client == null || _client.Client == null) return false;
            // Kiểm tra trạng thái socket thực tế
            bool part1 = _client.Client.Poll(1000, SelectMode.SelectRead);
            bool part2 = (_client.Client.Available == 0);
            if (part1 && part2) return false;
            return true;
        }

        // GỬI LỆNH VÀ ĐỌC PHẢN HỒI CHUNG
        private async Task<string> SendCommandAsync(string command)
        {
            if (!IsConnected() || _client == null || _client.Client == null) throw new InvalidOperationException("Robot không được kết nối.");

            try
            {
                // Gửi dữ liệu theo chuẩn phân tách bằng dấu phẩy theo hướng dẫn của Dobot [6, 9]
                byte[] dataToSend = Encoding.ASCII.GetBytes(command + "\n");
                await _stream.WriteAsync(dataToSend, 0, dataToSend.Length);

                // Đọc phản hồi
                byte[] buffer = new byte[1024];
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                return Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Lỗi giao tiếp: {ex.Message}");
                return null;
            }
        }

        // 2. ĐỌC GIÁ TRỊ POSE
        public async Task<string> GetPoseAsync()
        {
            // Gửi lệnh tùy chỉnh yêu cầu Pose. Cần script Lua trên robot trả về tọa độ.
            string response = await SendCommandAsync("GET_POSE");
            return response; // Định dạng trả về có thể là "POSE,X,Y,Z,RX,RY,RZ"
        }

        // 3. GỬI LỆNH MOVE (Dịch chuyển đến X, Y)
        // Tài liệu mô tả định dạng gửi là "GO,x,y" [6]
        public async Task<bool> MoveAsync(double x, double y)
        {
            string command = $"MOVE {x} {y}";
            string response = await SendCommandAsync(command);

            // Robot sẽ trả về "GOOK" nếu thành công hoặc "GONG" nếu thất bại [6]
            return response == "MOVE_OK";
        }

        // 4. GỬI LỆNH PICK (Đến tọa độ X, Y và góc RZ)
        public async Task<bool> PickAsync(double x, double y, double rz)
        {
            // Mở rộng giao thức dựa trên cấu trúc phân tách mảng chuỗi [9]
            string command = $"PICK {x} {y} {rz}";
            string response = await SendCommandAsync(command);
            return response == "PICK_OK";
        }

        public void Dispose()
        {
            _stream?.Close();
            _client?.Close();
            _client?.Close();
        }
        private async Task SendOnlyAsync(string command)
        {
            if (!IsConnected()) throw new InvalidOperationException("Robot không được kết nối.");

            byte[] dataToSend = Encoding.ASCII.GetBytes(command + "\n");
            await _stream.WriteAsync(dataToSend, 0, dataToSend.Length);
        }
        public async Task<bool> CheckTriggerAsync()
        {
            if (_stream == null || !IsConnected())
                return false;

            try
            {
                // Không có dữ liệu robot gửi lên thì bỏ qua
                if (!_stream.DataAvailable)
                    return false;

                byte[] buffer = new byte[1024];
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);

                if (bytesRead <= 0)
                    return false;

                string message = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();

                AppLogger.Instance.Info($"[Robot -> PC] {message}", "ROBOT");

                if (message == "CHOOK")
                {
                    await SendOnlyAsync("READ_TRIGGER_OK");
                    AppLogger.Instance.Info("[PC -> Robot] READ_TRIGGER_OK", "ROBOT");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error($"[Error] Lỗi đọc trigger từ robot: {ex.Message}", "ROBOT");
                return false;
            }
        }
    }

}
