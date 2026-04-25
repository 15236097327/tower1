using System.Net.Sockets;
using MazeTD.Shared;

namespace MazeTD.GameServer
{
    /// <summary>
    /// 代表服务端视角的一个客户端连接。
    ///
    /// 设计要点：
    /// - 每个Session持有独立接收线程，避免阻塞主Game Loop
    /// - 收到的指令包封装进IncomingCommand投入线程安全队列
    /// - 发送通过锁保护，支持多线程并发调用Send
    /// </summary>
    public class ClientSession
    {
        public int    PlayerId   { get; }
        public string PlayerName { get; set; } = "";
        public bool   IsAlive    { get; private set; } = true;

        private readonly TcpClient              _client;
        private readonly NetworkStream          _stream;
        private readonly PacketHelper.ReceiveBuffer _recvBuf = new();
        private readonly object                 _sendLock   = new();
        private readonly Action<IncomingCommand> _onPacket;
        private readonly Action<int>            _onDisconnect;

        public ClientSession(int playerId, TcpClient client,
                             Action<IncomingCommand> onPacket,
                             Action<int> onDisconnect)
        {
            PlayerId      = playerId;
            _client       = client;
            _stream       = client.GetStream();
            _onPacket     = onPacket;
            _onDisconnect = onDisconnect;
        }

        /// <summary>启动独立接收线程</summary>
        public void StartReceiving()
        {
            var thread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = $"RecvThread-P{PlayerId}"
            };
            thread.Start();
        }

        private void ReceiveLoop()
        {
            byte[] buf = new byte[4096];
            try
            {
                while (IsAlive)
                {
                    int n = _stream.Read(buf, 0, buf.Length);
                    if (n == 0) break; // 连接已关闭

                    _recvBuf.Append(buf, 0, n);

                    // 从缓冲区循环取完整帧（解决粘包）
                    while (_recvBuf.TryDequeue(out var type, out var payload))
                    {
                        _onPacket(new IncomingCommand(PlayerId, type, payload));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                // 正常断线或网络错误
            }
            finally
            {
                Disconnect();
            }
        }

        /// <summary>线程安全发送：可被Game Loop主线程和其他线程并发调用</summary>
        public void Send(byte[] data)
        {
            if (!IsAlive) return;
            try
            {
                lock (_sendLock)
                {
                    _stream.Write(data, 0, data.Length);
                }
            }
            catch
            {
                Disconnect();
            }
        }

        public void Disconnect()
        {
            if (!IsAlive) return;
            IsAlive = false;
            try { _client.Close(); } catch { }
            _onDisconnect(PlayerId);
        }
    }

    /// <summary>从网络层传递给主逻辑层的命令包装</summary>
    public class IncomingCommand
    {
        public int        PlayerId { get; }
        public PacketType Type     { get; }
        public byte[]     Payload  { get; }

        public IncomingCommand(int playerId, PacketType type, byte[] payload)
        {
            PlayerId = playerId;
            Type     = type;
            Payload  = payload;
        }
    }
}
