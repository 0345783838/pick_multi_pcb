using PickAndPlace.Controllers;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace PickAndPlace.Controller.Robot
{
    public class EpsonRobotClient : IDisposable
    {
        private readonly string _ip;

        private const int COMMAND_PORT = 5000;
        private const int EXEC_PORT = 2000;

        private TcpClient _commandClient;
        private NetworkStream _commandStream;
        private StreamReader _commandReader;
        private StreamWriter _commandWriter;

        private bool _isLoggedIn = false;

        private readonly object _lock = new object();

        private const string END = "\r\n";

        public EpsonRobotClient(string ip)
        {
            _ip = ip;
        }

        #region Connect / Login

        //public void Connect()
        //{
        //    if (_commandClient != null && _commandClient.Connected)
        //        return;

        //    _commandClient = new TcpClient();
        //    _commandClient.Connect(_ip, COMMAND_PORT);

        //    _commandStream = _commandClient.GetStream();

        //    _commandReader = new StreamReader(_commandStream, Encoding.ASCII);
        //    _commandWriter = new StreamWriter(_commandStream, Encoding.ASCII)
        //    {
        //        AutoFlush = true,
        //    };

        //    Login();
        //}
        public bool Connect(int timeout = 3000)
        {
            if (_commandClient != null && _commandClient.Connected)
                return true;

            _commandClient = new TcpClient();
            var task = _commandClient.ConnectAsync(_ip, COMMAND_PORT);

            if (!task.Wait(timeout))
            {
                AppLogger.Instance.Error("Robot connect timeout", "ROBOT_CONNECT_TIMEOUT");
                return false;
            }
              


            _commandStream = _commandClient.GetStream();
            _commandStream.ReadTimeout = timeout;
            _commandStream.WriteTimeout = timeout;

            _commandReader = new StreamReader(_commandStream, Encoding.ASCII);
            _commandWriter = new StreamWriter(_commandStream, Encoding.ASCII)
            {
                AutoFlush = true,
            };

            Login();
            return _isLoggedIn;
        }

        private void Login()
        {
            if (_isLoggedIn)
                return;

            var loginRes = SendCommandPort("$Login,");
            if (loginRes.StartsWith("#Login,0"))
                _isLoggedIn = true;
            else
            {
                _isLoggedIn = false;
            }
        }

        #endregion

        #region Command Port

        //private string SendCommandPort(string cmd)
        //{
        //    _commandWriter.WriteLine(cmd);

        //    string response = _commandReader.ReadLine();

        //    if (response == null)
        //        throw new Exception("Robot command port disconnected");

        //    if (response.StartsWith("ERROR"))
        //        throw new Exception($"Robot error: {response}");

        //    return response;
        //}
        private string SendCommandPort(string cmd)
        {
            try
            {
                _commandWriter.WriteLine(cmd);

                string response = _commandReader.ReadLine();

                if (response == null)
                    throw new Exception("Robot command port disconnected");

                if (response.StartsWith("ERROR"))
                    throw new Exception($"Robot error: {response}");

                return response;
            }
            catch (IOException)
            {
                AppLogger.Instance.Error("Robot command timeout", "ROBOT_COMMAND_TIMEOUT");
                return string.Empty;
            }
        }

        #endregion

        #region Execution

        //private string ExecuteRobotCommand(string command)
        //{
        //    lock (_lock)
        //    {
        //        if (_commandClient == null || !_commandClient.Connected)
        //        {
        //            Connect();
        //        }

        //        // yêu cầu robot mở port 2000
        //        var resStart = SendCommandPort("$Start,0");
        //        if (resStart.Contains("!"))
        //        {
        //            SendCommandPort("$Stop,0");
        //            var res2 = SendCommandPort("$Start,0");
        //            if (res2.Contains("!"))
        //            {
        //                throw new Exception($"Cannot Start Robot Command!:");
        //            }
        //        }

        //        // robot cần chút thời gian mở port
        //        Thread.Sleep(100);

        //        using (TcpClient execClient = new TcpClient())
        //        {
        //            execClient.Connect(_ip, EXEC_PORT);

        //            NetworkStream stream = execClient.GetStream();
        //            StreamReader reader = new StreamReader(stream, Encoding.ASCII);
        //            StreamWriter writer = new StreamWriter(stream, Encoding.ASCII)
        //            {
        //                AutoFlush = true
        //            };


        //            writer.WriteLine(command);

        //            string response = reader.ReadLine();

        //            if (response == null)
        //                throw new Exception("Robot execution disconnected");

        //            if (response.StartsWith("ERROR"))
        //                throw new Exception($"Robot error: {response}");

        //            stream.Close();
        //            reader.Close();
        //            writer.Close();

        //            return response;

        //        }
        //    }
        //}
        private string ExecuteRobotCommand(string command, int writeTimeout = 3000, int readTimeout = 3000)
        {
            lock (_lock)
            {
                if (_commandClient == null || !_commandClient.Connected)
                {
                    Connect();
                }

                // yêu cầu robot mở port 2000
                var resStart = SendCommandPort("$Start,0");
                if (resStart.Contains("!"))
                {
                    SendCommandPort("$Stop,0");
                    var res2 = SendCommandPort("$Start,0");
                    if (res2.Contains("!") || res2 == string.Empty)
                    {
                        AppLogger.Instance.Error($"Cannot Send/Timeout Start Robot PORT {EXEC_PORT}!", "ROBOT_START_COMMAND_ERROR");
                    }
                }

                // robot cần chút thời gian mở port
                Thread.Sleep(100);

                using (TcpClient execClient = new TcpClient())
                {
                    var task = execClient.ConnectAsync(_ip, EXEC_PORT);
                    if (!task.Wait(writeTimeout))
                        AppLogger.Instance.Error($"Connect to robot PORT {EXEC_PORT} error!", "ROBOT_EXEC_CONNECT_TIMEOUT");

                    NetworkStream stream = execClient.GetStream();
                    stream.ReadTimeout = readTimeout;
                    stream.WriteTimeout = writeTimeout;
                    StreamReader reader = new StreamReader(stream, Encoding.ASCII);
                    StreamWriter writer = new StreamWriter(stream, Encoding.ASCII)
                    {
                        AutoFlush = true
                    };


                    writer.WriteLine(command);

                    string response = string.Empty;

                    try
                    {
                        response = reader.ReadLine();
                    }
                    catch (IOException)
                    {
                        AppLogger.Instance.Error($"Read Robot timeout PORT {EXEC_PORT} - CMD: {command}", "ROBOT_READ_TIMEOUT");
                    }

                    if (response.StartsWith("ERROR"))
                    {
                        AppLogger.Instance.Error($"Read Robot error PORT {EXEC_PORT} - CMD: {command}", "ROBOT_READ_ERROR");
                    }
                        

                    stream.Close();
                    reader.Close();
                    writer.Close();

                    return response;

                }
            }
        }


        #endregion

        #region Robot Status

        public bool IsRobotReady()
        {
            // It should be checking connection via CMD status
            return _isLoggedIn;
        }

        #endregion

        #region Motion

        public bool MoveXY(double x, double y, int writeTimeout = 3000, int readTimeout = 3000)
        {
            string cmd = $"MOVE {x:F3} {y:F3}";
            try
            {
                var res = ExecuteRobotCommand(cmd, writeTimeout, readTimeout);
                if (res != null && res != string.Empty)
                    return true;

                return false;
            }
            catch (Exception e)
            {
                return false;
            }
            
        }

        public bool Pick(double x, double y, double w, int writeTimeout = 3000, int readTimeout = 3000)
        {
            string cmd = $"PICK {x:F3} {y:F3} {w:F3}";

            try
            {
                var res = ExecuteRobotCommand(cmd, writeTimeout, readTimeout);
                if (res != null && res != string.Empty)
                    return true;

                return false;
            }
            catch (Exception e)
            {
                return false;
            }
        }

        #endregion

        #region Position

        public (bool, RobotPose, string) GetCurrentPosition(int writeTimeout = 3000, int readTimeout = 3000)
        {
            try
            {
                string res = ExecuteRobotCommand("GET_POSE", writeTimeout, readTimeout);
                if (res != null && res != string.Empty)
                    return (true, ParsePose(res), "Success!");

                return (false, null, "Get current position error!");

            }
            catch (Exception e)
            {
                return (false, null, e.Message);
            }
            

            
        }

        private RobotPose ParsePose(string data)
        {
            RobotPose pose = new RobotPose();

            var parts = data.Split(' ');
            pose.X = double.Parse(parts[0]);
            pose.Y = double.Parse(parts[1]);
            pose.W = double.Parse(parts[2]);

            return pose;
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            try
            {
                _commandReader?.Close();
                _commandWriter?.Close();
                _commandStream?.Close();
                _commandClient?.Close();
            }
            catch
            {
            }
        }

        #endregion
    }

    public class RobotPose
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
    }
}