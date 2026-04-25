using System.Net;
using System.Net.Sockets;
using MazeTD.Shared;

namespace MazeTD.GameServer
{
    class Program
    {
        private static readonly int PORT = 7777;
        private static GameRoom? _room;
        private static int _nextPlayerId = 0;
        private static readonly object _idLock = new();

        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("MazeTD GameServer v1.0");
            Console.WriteLine($"监听端口: {PORT}");
            Console.WriteLine("[Server] 等待玩家连接，模式由玩家选择...");

            // 不预先创建房间，等第一个玩家连接时创建
            var acceptThread = new Thread(() => AcceptLoop(PORT))
            {
                IsBackground = true,
                Name = "AcceptThread"
            };
            acceptThread.Start();

            GameLoop();
        }

        static void AcceptLoop(int port)
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"[Server] TcpListener 已启动，等待连接...");

            while (true)
            {
                try
                {
                    var client = listener.AcceptTcpClient();
                    client.NoDelay = true;
                    var t = new Thread(() => HandleNewClient(client))
                    {
                        IsBackground = true
                    };
                    t.Start();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Server] AcceptLoop异常: {ex.Message}");
                }
            }
        }
        static void HandleNewClient(TcpClient client)
        {
            int pid;
            lock (_idLock)
            {
                if (_nextPlayerId >= 2)
                {
                    var err = PacketHelper.Pack(PacketType.S2C_JoinAck,
                        new S2C_JoinAckPayload { success = false, reason = "房间已满" });
                    try { client.GetStream().Write(err, 0, err.Length); } catch { }
                    client.Close();
                    return;
                }
                pid = _nextPlayerId++;
            }

            Console.WriteLine($"[Server] 客户端连接 PlayerId={pid}");

            // 第一个玩家连接时创建房间（模式先默认Alliance，后面根据准备包更新）
            if (pid == 0)
            {
                _room = new GameRoom(GameMode.Alliance);
                Console.WriteLine("[Server] 房间已创建，等待玩家选择模式...");
            }

            var stream = client.GetStream();
            var ackData = PacketHelper.Pack(PacketType.S2C_JoinAck,
                new S2C_JoinAckPayload { success = true, playerId = pid });
            stream.Write(ackData, 0, ackData.Length);

            var session = new ClientSession(
     pid, client,
     onPacket: cmd => _room?.EnqueueCommand(cmd),
     onDisconnect: id =>
     {
         _room?.OnPlayerDisconnect(id);
         lock (_idLock)
         {
             _nextPlayerId--;
             Console.WriteLine($"[Server] P{id} 断线，nextPlayerId={_nextPlayerId}");
             if (_nextPlayerId <= 0)
             {
                 _nextPlayerId = 0;
                 _room = null;
                 Console.WriteLine("[Server] 房间已重置，等待新玩家...");
             }
         }
     }
 );

            _room?.TryAddPlayer(session);
            session.StartReceiving();
        }
        static void GameLoop()
        {
            const float TARGET_DT = 1f / 20f;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double lastTime = 0;

            Console.WriteLine("[Server] Game Loop 已启动 (20 tick/s)");

            while (true)
            {
                double now = sw.Elapsed.TotalSeconds;
                float dt = (float)(now - lastTime);

                if (dt >= TARGET_DT)
                {
                    lastTime = now;
                    _room?.Tick(dt);
                }
                else
                {
                    int sleepMs = (int)((TARGET_DT - dt) * 1000) - 1;
                    if (sleepMs > 0) Thread.Sleep(sleepMs);
                }
            }
        }
    }
}