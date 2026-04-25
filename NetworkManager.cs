using MazeTD.Shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace MazeTD.Client.Network
{
    /// <summary>
    /// Unity客户端网络管理器（单例）
    ///
    /// 设计要点：
    /// - 独立接收线程处理TCP数据，避免Unity主线程阻塞
    /// - 使用ConcurrentQueue将收到的包安全传递给主线程
    /// - PacketHelper.ReceiveBuffer处理粘包/拆包
    /// - 主线程在Update()中消费队列，驱动GameManager
    /// </summary>
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; }

        [Header("连接配置")]
        public string ServerIP   = "127.0.0.1";
        public int    ServerPort = 7777;

        // 收到的包队列（接收线程 → 主线程）
        private readonly ConcurrentQueue<(PacketType type, byte[] payload)> _inQueue = new();

        private TcpClient                   _client;
        private NetworkStream               _stream;
        private PacketHelper.ReceiveBuffer  _recvBuf = new();
        private Thread                      _recvThread;
        private readonly object             _sendLock = new();

        public bool IsConnected { get; private set; }
        public bool CacheGamePackets = false;
        // 事件：主线程注册后收到包时触发
        public event Action<PacketType, byte[]> OnPacketReceived;
        // 跨场景传递的角色信息
        public int LocalRole = -1;   // 0=防守方 1=进攻方 -1=合作模式
        public int LocalMode = 0;    // 0=Alliance 1=Legion
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        public int LocalPlayerId = -1;
        // ── 连接 ─────────────────────────────────────────────────

        public void Connect(string ip, int port)
        {
            try
            {
                _client         = new TcpClient();
                _client.NoDelay = true;
                _client.Connect(ip, port);
                _stream         = _client.GetStream();
                IsConnected     = true;

                _recvThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "ClientRecvThread"
                };
                _recvThread.Start();

                Debug.Log($"[Network] 已连接到 {ip}:{port}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Network] 连接失败: {ex.Message}");
                IsConnected = false;
            }
        }

        // ── 接收线程 ─────────────────────────────────────────────

        private void ReceiveLoop()
        {
            byte[] buf = new byte[8192];
            try
            {
                while (IsConnected)
                {
                    int n = _stream.Read(buf, 0, buf.Length);
                    if (n == 0) break;

                    _recvBuf.Append(buf, 0, n);

                    while (_recvBuf.TryDequeue(out var type, out var payload))
                        _inQueue.Enqueue((type, payload));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Network] 接收中断: {ex.Message}");
            }
            finally
            {
                IsConnected = false;
                Debug.Log("[Network] 连接已断开");
            }
        }
        public void Disconnect()
        {
            IsConnected = false;
            try { _client?.Close(); } catch { }
            while (_inQueue.TryDequeue(out _)) { }  // 清空队列
        }
        // ── 主线程 Update：消费队列 ──────────────────────────────

        // 加一个字段缓存未消费的关键包
        private readonly List<(PacketType type, byte[] payload)> _pendingQueue = new();

        private void Update()
        {
            while (_inQueue.TryDequeue(out var item))
            {
                if (item.type == PacketType.S2C_StateSync)
                {
                    var sync = PacketHelper.Deserialize<S2C_StateSyncPayload>(item.payload);
                    Debug.Log($"[Network] 派发StateSync monsters={sync.monsters.Count}");
                }
                try { OnPacketReceived?.Invoke(item.type, item.payload); }
                catch (Exception ex)
                {
                    Debug.LogError($"[Network] 处理包异常 {item.type}: {ex.Message}");
                }
            }
        }
        public void FlushPending()
        {
            Debug.Log($"[Network] FlushPending 共 {_pendingQueue.Count} 个包");  // 加这行
            foreach (var item in _pendingQueue)
            {
                Debug.Log($"[Network] 补发缓存包 {item.type}");
                OnPacketReceived?.Invoke(item.type, item.payload);
            }
            _pendingQueue.Clear();
        }
        // ── 发送（线程安全）─────────────────────────────────────

        public void Send(byte[] data)
        {
            if (!IsConnected) return;
            try
            {
                lock (_sendLock)
                    _stream.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Network] 发送失败: {ex.Message}");
                IsConnected = false;
            }
        }

        public void Send<T>(PacketType type, T payload) => Send(PacketHelper.Pack(type, payload));
        public void Send(PacketType type)               => Send(PacketHelper.Pack(type));

        private void OnDestroy()
        {
            IsConnected = false;
            try { _client?.Close(); } catch { }
        }
        public void ResetGameState()
        {
            LocalPlayerId = -1;
            LocalRole = -1;
            LocalMode = 0;
            CacheGamePackets = false;
            while (_inQueue.TryDequeue(out _)) { }
        }
    }
}
