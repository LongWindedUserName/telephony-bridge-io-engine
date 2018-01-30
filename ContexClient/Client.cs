using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Timers.Timer;

namespace ConnectionEngine
{
    public class RTBIConnectionClient
    {

        // Sentinel / Sentinel Server
        //private const int TcpPort = 7030;

        // Apex
        private const int TcpPort = 7040;

        private bool _connected;
        private bool _connecting;
        private readonly object _lockObj = new object(); // Used to synchronize any multi-threaded calls
        private readonly Timer _keepAliveTimer;
        private NetworkStream _stream;

        public RTBIConnectionClient()
        {
            _keepAliveTimer = new Timer(10000);
            _keepAliveTimer.Elapsed += (_, __) => KeepAliveTick(); // Fire a keep-alive every 10 seconds
            _keepAliveTimer.Start();
        }

        public event Action<string> MessageReceived;
        private void OnMessageReceived(string message)
        {
            var handler = MessageReceived;

            if (handler != null)
                handler(message);
        }

        public async Task Connect(string hostname)
        {
            lock (_lockObj)
            {
                if (_connected || _connecting) return;
                _connecting = true;
            }
            try
            {
                var client = new TcpClient();
                var timeoutTask = Task.Delay(10000);
                var connectTask = client.ConnectAsync(hostname, TcpPort);

                await Task.WhenAny(timeoutTask, connectTask);

                if (!connectTask.IsCompleted && timeoutTask.IsCompleted)
                {
                    _connecting = false;
                    client.Close();
                    Logger.Log($"*-Timed out connecting to {hostname}. [Client.cs]");
                    return;
                }

                await connectTask;

                try { _stream = client.GetStream(); }
                catch
                {
                    _connecting = false;
                    throw;
                }

                BeginReadLoop();

                lock (_lockObj)
                {
                    _connected = true;
                    _connecting = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"*-Couldn't connect to bridge {hostname}. Failed connection attempt. [Client.cs]");
                Logger.Log(ex.ToString());
                return;
            }
        }

        public void Disconnect()
        {
            OnDisconnect();
        }

        private void BeginReadLoop()
        {
            Task.Run(() => ReadLoop());
        }

        private async Task ReadLoop()
        {
            var readBuffer = new byte[256];
            var messageStream = new MessageStream();
            while (_connected)
            {
                try
                {
                    var bytesRead = await _stream.ReadAsync(readBuffer, 0, readBuffer.Length);
                    if (bytesRead == 0)
                    {
                        OnDisconnect();
                        break;
                    }

                    messageStream.AddBytes(readBuffer, bytesRead);
                    foreach (var msg in messageStream.ReadAll())
                    {
                        var strMsg = Encoding.ASCII.GetString(msg);
                        if (strMsg == "LT") continue; // Don't tell the user about keep-alives
                        OnMessageReceived(strMsg);
                    }
                }
                catch
                {
                    OnDisconnect();
                    break;
                }
            }

        }

        private void OnDisconnect()
        {
            lock (_lockObj)
            {
                _connected = false;
                try { _stream.Close(); } catch { }
            }
        }

        /// <summary>
        /// <paramref name="message"/> may only contain ASCII characters, per CPX API documentation
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public async Task SendMessage(string message)
        {
            if (!_connected) return;

            var bytes = PackMessage(message);
            await _stream.WriteAsync(bytes, 0, bytes.Length);
        }

        private const byte StartTextChar = 0x2;
        private const byte EndTextChar = 0x3;
        private byte[] PackMessage(string message)
        {
            // Follows along with 3 - Message Packet Specification from API spec
            var msgBytes = Encoding.ASCII.GetBytes(message);

            var toSendBytes = new byte[msgBytes.Length + 6];
            toSendBytes[0] = StartTextChar;
            Array.Copy(msgBytes, 0, toSendBytes, 1, msgBytes.Length);
            toSendBytes[toSendBytes.Length - 5] = EndTextChar;

            var checksumLength = msgBytes.Length + 2; // +2 because of start and end characters
            var xorSumBytes = XorSum(toSendBytes, checksumLength);
            var byteSumBytes = ByteSum(toSendBytes, checksumLength);
            Array.Copy(xorSumBytes, 0, toSendBytes, toSendBytes.Length - 4, 2);
            Array.Copy(byteSumBytes, 0, toSendBytes, toSendBytes.Length - 2, 2);

            return toSendBytes;
        }

        private byte[] XorSum(byte[] input, int length)
        {
            var xorSumValue = input.Take(length).Aggregate((b1, b2) => (byte)(b1 ^ b2)); // Calculate XORSUM
            return ToByteArrayHexRepresentation(xorSumValue);
        }

        private byte[] ByteSum(byte[] input, int length)
        {
            var byteSumValue = input.Take(length).Aggregate((b1, b2) => (byte)(b1 + b2));
            return ToByteArrayHexRepresentation(byteSumValue);
        }

        // Checksum
        private byte[] ToByteArrayHexRepresentation(byte val)
        {
            var hex = val.ToString("X2");
            return Encoding.ASCII.GetBytes(hex);
        }

        private async void KeepAliveTick()
        {
            await SendMessage("LT");
        }
    }
}
