using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PickAndPlace.Controller.Robot
{
    /// <summary>
    /// High-level TCP/IP client for the Nachi CFDs robot controller.
    /// Based on: CFDs CONTROLLER INSTRUCTION MANUAL - Socket Communication (CFDs-EN-127-001A)
    /// Compatible with C# 7.3
    ///
    /// Protocol summary (from manual):
    ///   - Socket type    : TCP (SOCKCREATE param = 0)
    ///   - Controller role: SERVER  (SOCKBIND -> SOCKWAIT -> SOCKSEND/SOCKRECV)
    ///   - PC role        : CLIENT  (connect -> recv position -> send shift register)
    ///
    /// Payload exchanged (matches Ch.5 sample programs):
    ///   Receive from controller : CSV "X,Y,Z,Roll,Pitch,Yaw,IO,"  (12 chars per float field)
    ///   Send to controller      : 6 x "%08.2f" ASCII floats = 48 bytes + null terminator
    ///                             order: dX, dY, dZ, dRoll(thetaZ), dPitch(thetaY), dYaw(thetaX)
    ///
    /// Shift register R1 layout (manual Sec 5.1.3 / 5.1.6):
    ///   R1 = (dX, dY, dZ, dRoll, dPitch, dYaw)
    ///        (x,   y,   z, theta_z, theta_y, theta_x)
    /// </summary>
    public class NachiRobotClient : IDisposable
    {
        // -----------------------------------------------------------------------
        // Constants - adjust to match your cell
        // -----------------------------------------------------------------------

        /// <summary>Default TCP port used in Ch.5 sample programs.</summary>
        public const int DefaultPort = 10030;

        /// <summary>Socket timeout in milliseconds.</summary>
        public const int DefaultTimeoutMs = 10000;

        /// <summary>Each float field sent to the controller is 8 ASCII chars ("%08.2f").</summary>
        private const int FieldWidth = 8;

        /// <summary>Number of DOF fields in the shift-register payload.</summary>
        private const int NumFields = 6;

        /// <summary>Total bytes in the payload sent to the controller (48 bytes + 1 null).</summary>
        private const int SendBufferLen = FieldWidth * NumFields; // 48

        /// <summary>Chars per received position field.</summary>
        private const int RecDataSize = 12;

        /// <summary>Chars for the IO field.</summary>
        private const int RecIoDataSize = 3;

        /// <summary>Max bytes to read from the controller's position message.</summary>
        private const int RecBufferLen = RecDataSize * 6 + RecIoDataSize + 7;

        // -----------------------------------------------------------------------
        // Public configurable properties
        // -----------------------------------------------------------------------

        /// <summary>Z offset (mm) applied when lowering to pick a part.</summary>
        public double PickZOffset { get; set; }

        /// <summary>Z offset (mm) applied when lowering to place a part.</summary>
        public double PlaceZOffset { get; set; }

        /// <summary>Delay (ms) between sequential shift-register commands in Pick/Place.</summary>
        public int MotionDelayMs { get; set; }

        // -----------------------------------------------------------------------
        // Public state (updated after every connect / position read)
        // -----------------------------------------------------------------------
        public double CurrentX { get; private set; }
        public double CurrentY { get; private set; }
        public double CurrentZ { get; private set; }
        public double CurrentRoll { get; private set; }
        public double CurrentPitch { get; private set; }
        public double CurrentYaw { get; private set; }
        public string CurrentIO { get; private set; }

        // -----------------------------------------------------------------------
        // Private fields
        // -----------------------------------------------------------------------
        private readonly string _host;
        private readonly int _port;
        private readonly int _timeoutMs;
        private TcpClient _client;   // no nullable ? in C# 7.3
        private NetworkStream _stream;
        private bool _disposed;

        // -----------------------------------------------------------------------
        // Constructor
        // -----------------------------------------------------------------------

        /// <param name="host">IP address of the Nachi controller (e.g. "192.168.1.1").</param>
        /// <param name="port">TCP port the controller listens on (default 10030).</param>
        /// <param name="timeoutMs">Socket read/write timeout in milliseconds.</param>
        public NachiRobotClient(string host, int port = DefaultPort, int timeoutMs = DefaultTimeoutMs)
        {
            if (host == null) throw new ArgumentNullException("host");
            _host = host;
            _port = port;
            _timeoutMs = timeoutMs;

            // Initialise public properties (no property initialisers in fields for C# 7.3 auto-props)
            PickZOffset = -50.0;
            PlaceZOffset = -50.0;
            MotionDelayMs = 300;
            CurrentIO = "000";
        }

        // -----------------------------------------------------------------------
        // Connection management
        // -----------------------------------------------------------------------

        /// <summary>
        /// Open a TCP connection to the Nachi controller.
        /// The controller's User Task (SOCKWAIT) must already be running before
        /// calling this method.  On success the controller sends current position
        /// data which is parsed and cached in Current* properties.
        /// </summary>
        /// <returns>True on success, false on failure.</returns>
        public bool Connect()
        {
            try
            {
                _client = new TcpClient();
                _client.SendTimeout = _timeoutMs;
                _client.ReceiveTimeout = _timeoutMs;

                // Synchronous connect - controller must already be in SOCKWAIT
                _client.Connect(_host, _port);
                _stream = _client.GetStream();

                Console.WriteLine("[Nachi] Connected to " + _host + ":" + _port);

                // After accepting the connection, the controller immediately sends
                // current TCP position + IO state (see section 5.1.4 sample)
                ReadPositionFromController();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Nachi] Connect() failed: " + ex.Message);
                CleanupSocket();
                return false;
            }
        }

        /// <summary>Async version of Connect().</summary>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                _client = new TcpClient();
                _client.SendTimeout = _timeoutMs;
                _client.ReceiveTimeout = _timeoutMs;

                // ConnectAsync(string, int) is available since .NET Framework 4.5
                await _client.ConnectAsync(_host, _port);
                _stream = _client.GetStream();

                Console.WriteLine("[Nachi] Connected to " + _host + ":" + _port);
                await ReadPositionFromControllerAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Nachi] ConnectAsync() failed: " + ex.Message);
                CleanupSocket();
                return false;
            }
        }

        /// <summary>Close the socket gracefully.</summary>
        public void Disconnect()
        {
            CleanupSocket();
            Console.WriteLine("[Nachi] Disconnected.");
        }

        /// <summary>
        /// Return true if the underlying socket is open and appears reachable.
        /// Uses Socket.Poll() - does NOT perform a full round-trip to the robot.
        /// </summary>
        public bool CheckConnection()
        {
            if (_client == null || !_client.Connected || _stream == null)
                return false;

            try
            {
                Socket sock = _client.Client;
                bool readable = sock.Poll(0, SelectMode.SelectRead);
                bool noData = (sock.Available == 0);

                if (readable && noData)
                {
                    Console.WriteLine("[Nachi] CheckConnection(): remote end closed the socket.");
                    CleanupSocket();
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Nachi] CheckConnection() exception: " + ex.Message);
                CleanupSocket();
                return false;
            }
        }

        /// <summary>
        /// Attempt to reconnect up to <paramref name="retries"/> times
        /// with <paramref name="delayMs"/> milliseconds between attempts.
        /// </summary>
        public bool Reconnect(int retries = 3, int delayMs = 2000)
        {
            Disconnect();
            for (int attempt = 1; attempt <= retries; attempt++)
            {
                Console.WriteLine("[Nachi] Reconnect attempt " + attempt + "/" + retries + " ...");
                if (Connect()) return true;
                Thread.Sleep(delayMs);
            }
            Console.WriteLine("[Nachi] All reconnect attempts failed.");
            return false;
        }

        // -----------------------------------------------------------------------
        // Position query
        // -----------------------------------------------------------------------

        /// <summary>
        /// Return the last cached position as a RobotPosition struct.
        /// Call Connect() to refresh.
        /// </summary>
        public RobotPosition GetCurrentPosition()
        {
            return new RobotPosition(CurrentX, CurrentY, CurrentZ,
                                     CurrentRoll, CurrentPitch, CurrentYaw);
        }

        // -----------------------------------------------------------------------
        // High-level motion commands
        // -----------------------------------------------------------------------

        /// <summary>
        /// Move the robot by writing the 6-DOF shift register R1.
        /// All linear values in mm; all rotational values in degrees.
        ///
        /// Shift register mapping (manual Sec 5.1.3):
        ///   R1 = (dX, dY, dZ, dRoll(thetaZ), dPitch(thetaY), dYaw(thetaX))
        ///
        /// IMPORTANT: The controller robot program must contain a SHIFT instruction
        /// that reads R1 to physically move the arm.
        /// See instruction manual "SHIFT FUNCTIONS" (CFDs-EN-162).
        /// </summary>
        /// <param name="x">X shift [mm]</param>
        /// <param name="y">Y shift [mm]</param>
        /// <param name="z">Z shift [mm]</param>
        /// <param name="roll">Z-axis rotation shift dThetaZ [deg]</param>
        /// <param name="pitch">Y-axis rotation shift dThetaY [deg]</param>
        /// <param name="yaw">X-axis rotation shift dThetaX [deg]</param>
        public bool Move(double x, double y, double z = 0.0,
                         double roll = 0.0, double pitch = 0.0, double yaw = 0.0)
        {
            Console.WriteLine(
                "[Nachi] Move -> dX=" + x.ToString("F2") +
                "  dY=" + y.ToString("F2") +
                "  dZ=" + z.ToString("F2") +
                "  Roll=" + roll.ToString("F2") +
                "  Pitch=" + pitch.ToString("F2") +
                "  Yaw=" + yaw.ToString("F2"));

            return SendShiftRegister(x, y, z, roll, pitch, yaw);
        }

        /// <summary>Convenience overload: move only in the XY plane.</summary>
        public bool MoveXY(double x, double y)
        {
            return Move(x, y);
        }

        /// <summary>
        /// Move to (x, y) with Z-axis rotation rz, then lower the Z axis by
        /// PickZOffset to grasp the part.
        /// </summary>
        /// <param name="x">X target shift [mm]</param>
        /// <param name="y">Y target shift [mm]</param>
        /// <param name="rz">Z-axis rotation (Roll) [deg]</param>
        public bool Pick(double x, double y, double rz = 0.0)
        {
            Console.WriteLine("[Nachi] Pick -> x=" + x.ToString("F2") +
                              "  y=" + y.ToString("F2") +
                              "  rz=" + rz.ToString("F2") + " deg");

            // Step 1 - approach above pick point
            if (!Move(x, y, 0.0, rz))
            {
                Console.WriteLine("[Nachi] Pick: positioning step failed.");
                return false;
            }

            Thread.Sleep(MotionDelayMs);

            // Step 2 - lower to grasp
            if (!Move(x, y, PickZOffset, rz))
            {
                Console.WriteLine("[Nachi] Pick: lower-Z step failed.");
                return false;
            }

            Console.WriteLine("[Nachi] Pick: grasping at Z offset " + PickZOffset.ToString("F2") + " mm.");
            return true;
        }

        /// <summary>
        /// Move to (x, y) with Z-axis rotation rz, lower to deposit the part,
        /// then raise back to approach height.
        /// </summary>
        /// <param name="x">X target shift [mm]</param>
        /// <param name="y">Y target shift [mm]</param>
        /// <param name="rz">Z-axis rotation (Roll) [deg]</param>
        public bool Place(double x, double y, double rz = 0.0)
        {
            Console.WriteLine("[Nachi] Place -> x=" + x.ToString("F2") +
                              "  y=" + y.ToString("F2") +
                              "  rz=" + rz.ToString("F2") + " deg");

            // Step 1 - approach above place point
            if (!Move(x, y, 0.0, rz))
            {
                Console.WriteLine("[Nachi] Place: positioning step failed.");
                return false;
            }

            Thread.Sleep(MotionDelayMs);

            // Step 2 - lower to deposit
            if (!Move(x, y, PlaceZOffset, rz))
            {
                Console.WriteLine("[Nachi] Place: lower-Z step failed.");
                return false;
            }

            Thread.Sleep(MotionDelayMs);

            // Step 3 - raise back to approach height
            if (!Move(x, y, 0.0, rz))
            {
                Console.WriteLine("[Nachi] Place: raise-Z step failed.");
                return false;
            }

            Console.WriteLine("[Nachi] Place: part deposited at (" +
                              x.ToString("F2") + ", " + y.ToString("F2") + ").");
            return true;
        }

        // -----------------------------------------------------------------------
        // Internal helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Build and transmit the 48+1 byte ASCII payload to the controller.
        ///
        /// Format: six "%08.2f" fields concatenated (no separators), followed by
        /// one null byte - matching the C++ sample in section 5.1.5 / 5.1.7:
        ///   sprintf_s(buffer, "%s%s%s%s%s%s", shift_x, shift_y, ...)
        ///   send(sock, buffer, sizeof(buffer), 0)   // sizeof includes '\0'
        /// </summary>
        private bool SendShiftRegister(double dx, double dy, double dz,
                                       double dRoll, double dPitch, double dYaw)
        {
            if (!EnsureConnected()) return false;

            // Build exactly SendBufferLen chars then append null terminator
            string payload =
                dx.ToString("00000.00") +
                dy.ToString("00000.00") +
                dz.ToString("00000.00") +
                dRoll.ToString("00000.00") +
                dPitch.ToString("00000.00") +
                dYaw.ToString("00000.00");

            // Pad or truncate to exactly SendBufferLen characters
            if (payload.Length < SendBufferLen)
                payload = payload.PadRight(SendBufferLen);
            else if (payload.Length > SendBufferLen)
                payload = payload.Substring(0, SendBufferLen);  // no range [..] in C# 7.3

            // Append null terminator (matches C++ sizeof(buffer) = SendBufferLen + 1)
            byte[] bytes = new byte[SendBufferLen + 1];
            Encoding.ASCII.GetBytes(payload, 0, SendBufferLen, bytes, 0);
            bytes[SendBufferLen] = 0x00;

            try
            {
                _stream.Write(bytes, 0, bytes.Length);   // no ! null-forgiving in C# 7.3
                _stream.Flush();
                Console.WriteLine("[Nachi] Sent: " + payload);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Nachi] SendShiftRegister() failed: " + ex.Message);
                CleanupSocket();
                return false;
            }
        }

        /// <summary>
        /// Read and parse the CSV position string the controller sends immediately
        /// after accepting our connection (see section 5.1.4 server sample).
        ///
        /// Expected format:  "X_val,Y_val,Z_val,Roll_val,Pitch_val,Yaw_val,IO_val,"
        /// </summary>
        private void ReadPositionFromController()
        {
            try
            {
                byte[] buf = new byte[RecBufferLen];
                int read = _stream.Read(buf, 0, buf.Length);
                ParsePositionBuffer(buf, read);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Nachi] ReadPositionFromController() failed: " + ex.Message);
            }
        }

        private async Task ReadPositionFromControllerAsync()
        {
            try
            {
                byte[] buf = new byte[RecBufferLen];
                // ReadAsync(byte[], int, int) - available since .NET 4.5, no Memory<T> needed
                int read = await _stream.ReadAsync(buf, 0, buf.Length);
                ParsePositionBuffer(buf, read);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Nachi] ReadPositionFromControllerAsync() failed: " + ex.Message);
            }
        }

        private void ParsePositionBuffer(byte[] buf, int length)
        {
            string raw = Encoding.ASCII.GetString(buf, 0, length).Trim();
            Console.WriteLine("[Nachi] Received: " + raw);

            string[] parts = raw.Split(',');

            double x, y, z, roll, pitch, yaw;

            if (parts.Length >= 6 &&
                double.TryParse(parts[0].Trim(), out x) &&
                double.TryParse(parts[1].Trim(), out y) &&
                double.TryParse(parts[2].Trim(), out z) &&
                double.TryParse(parts[3].Trim(), out roll) &&
                double.TryParse(parts[4].Trim(), out pitch) &&
                double.TryParse(parts[5].Trim(), out yaw))
            {
                CurrentX = x;
                CurrentY = y;
                CurrentZ = z;
                CurrentRoll = roll;
                CurrentPitch = pitch;
                CurrentYaw = yaw;

                if (parts.Length >= 7)
                    CurrentIO = parts[6].Trim();

                Console.WriteLine(
                    "[Nachi] Position -> X=" + CurrentX.ToString("F2") +
                    "  Y=" + CurrentY.ToString("F2") +
                    "  Z=" + CurrentZ.ToString("F2") +
                    "  Roll=" + CurrentRoll.ToString("F2") +
                    "  Pitch=" + CurrentPitch.ToString("F2") +
                    "  Yaw=" + CurrentYaw.ToString("F2") +
                    "  IO=" + CurrentIO);
            }
            else
            {
                Console.WriteLine("[Nachi] WARNING: unexpected position format: " + raw);
            }
        }

        private bool EnsureConnected()
        {
            if (_client == null || !_client.Connected || _stream == null)
            {
                Console.WriteLine("[Nachi] EnsureConnected(): no active connection - call Connect() first.");
                return false;
            }
            return true;
        }

        private void CleanupSocket()
        {
            if (_stream != null)
            {
                try { _stream.Close(); } catch { /* ignore */ }
                _stream = null;
            }

            if (_client != null)
            {
                try { _client.Close(); } catch { /* ignore */ }
                _client = null;
            }
        }

        // -----------------------------------------------------------------------
        // IDisposable
        // -----------------------------------------------------------------------

        public void Dispose()
        {
            if (_disposed) return;
            CleanupSocket();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        ~NachiRobotClient()
        {
            Dispose();
        }

        // -----------------------------------------------------------------------
        // ToString
        // -----------------------------------------------------------------------

        public override string ToString()
        {
            string status = (_client != null && _client.Connected) ? "connected" : "disconnected";
            return "NachiRobotClient(host=" + _host + ", port=" + _port + ", status=" + status + ")";
        }
    }


    // ===========================================================================
    // Simple struct to return position without ValueTuple deconstruct issues
    // ===========================================================================
    public struct RobotPosition
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double Roll { get; }
        public double Pitch { get; }
        public double Yaw { get; }

        public RobotPosition(double x, double y, double z,
                             double roll, double pitch, double yaw)
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
            return "X=" + X.ToString("F2") +
                   "  Y=" + Y.ToString("F2") +
                   "  Z=" + Z.ToString("F2") +
                   "  Roll=" + Roll.ToString("F2") +
                   "  Pitch=" + Pitch.ToString("F2") +
                   "  Yaw=" + Yaw.ToString("F2");
        }
    }


    //// ===========================================================================
    //// Quick usage example (remove or move to your own Program.cs)
    //// ===========================================================================
    //internal static class Program
    //{
    //    private static void Main()
    //    {
    //        const string ControllerIp = "192.168.1.1";
    //        const int ControllerPort = 10030;

    //        // C# 7.3: use explicit using block, not "using var"
    //        using (NachiRobotClient robot = new NachiRobotClient(ControllerIp, ControllerPort))
    //        {
    //            // ── Connect ──────────────────────────────────────────────────────
    //            if (!robot.Connect())
    //            {
    //                Console.WriteLine("Cannot connect. Check network and User Task on controller.");
    //                return;
    //            }

    //            Console.WriteLine(robot.ToString());

    //            // ── Check connection ──────────────────────────────────────────────
    //            Console.WriteLine("Connection alive: " + robot.CheckConnection());

    //            // ── Read cached position ──────────────────────────────────────────
    //            RobotPosition pos = robot.GetCurrentPosition();
    //            Console.WriteLine("Position: " + pos.ToString());

    //            // ── Move XY only ──────────────────────────────────────────────────
    //            robot.MoveXY(100.0, 50.0);

    //            // ── Move with full 6-DOF ──────────────────────────────────────────
    //            robot.Move(x: 100.0, y: 50.0, z: 0.0, roll: 45.0, pitch: 0.0, yaw: 0.0);

    //            // ── Pick at (200, 150) with 30 deg Z-rotation ─────────────────────
    //            robot.Pick(200.0, 150.0, rz: 30.0);

    //            // ── Place at (300, 100) with 0 deg Z-rotation ─────────────────────
    //            robot.Place(300.0, 100.0, rz: 0.0);

    //        } // Dispose/Disconnect called automatically here

    //        Console.WriteLine("Done.");
    //    }
    //}
}
