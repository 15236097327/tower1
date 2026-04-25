using System.Collections.Concurrent;
using MazeTD.GameServer.Entity;
using MazeTD.Shared;

namespace MazeTD.GameServer
{
    public enum GameMode { Alliance = 0, Legion = 1 }
  

    /// <summary>
    /// 权威游戏房间。
    ///
    /// 职责：
    /// 1. 持有所有游戏状态（Grid/Monster/Tower/Bullet/Gold/BaseHp）
    /// 2. 运行固定频率的Game Loop（20 tick/s）
    /// 3. 处理来自ClientSession的网络指令（通过ConcurrentQueue）
    /// 4. 计算A*路径、合法性校验、经济系统
    /// 5. 每帧广播全量StateSync
    /// </summary>
    public class GameRoom
    {
        // ── 配置常量 ─────────────────────────────────────────────
        private const int   MAP_W        = 40;
        private const int   MAP_H        = 30;
        private const int   INIT_GOLD    = 2000;
        private const int   BASE_MAX_HP  = 500;
        private const float TICK_RATE    = 0.05f;  // 20 tick/s
        private const float SYNC_INTERVAL= 0.05f;  // 每帧广播
        private const int   MAX_WAVE     = 10;

        // ── 状态 ─────────────────────────────────────────────────
        public  RoomState State { get; private set; } = RoomState.WaitingPlayers;
        public  GameMode  Mode  { get;  set; }
        public enum RoomState { WaitingPlayers, Selecting, Running, GameOver }
        private  GameGrid           _grid;
        private readonly AStarPathfinder    _pathfinder = new();
        private List<Vec2Int>               _path       = new();   // Alliance共用 / Legion field0
        private List<Vec2Int>               _pathField1 = new();   // Legion field1（mirror地图）

        private readonly List<Monster>      _monsters   = new();
        private readonly List<Tower>        _towers     = new();
        private readonly List<Bullet>       _bullets    = new();

        private readonly int[]              _gold       = new int[2]; // 每个玩家金币
        private readonly int[]              _baseHp     = new int[2]; // field0,field1基地血量

        private int   _currentWave  = 0;
        private float _waveTimer    = 5f;   // 第一波延迟5秒
        private int   _monstersLeftInWave = 0;
        private float _spawnInterval     = 0f;
        private float _spawnAccum        = 0f;
        private bool  _spawning          = false;
        private int   _spawnType         = 0;
        private int   _spawnField        = 0;
        private int _attackerPlayerId = 1;
        private int _defenderPlayerId = 0;
        // ── 网络 ──────────────────────────────────────────────────
        private readonly Dictionary<int, ClientSession>    _sessions  = new();
        private readonly ConcurrentQueue<IncomingCommand>  _cmdQueue  = new();
        private readonly object _sessionLock = new();

        private long _tick = 0;

        // 在已有常量下面加
        private const int LEGION_DURATION = 300;  // 5分钟
        private const float INCOME_INTERVAL = 10f;  // 每10秒
        private const int INCOME_AMOUNT = 20;   // 每次20金币
                                                // 在类字段里加
        private readonly Queue<(int type, int field)> _spawnQueue = new Queue<(int, int)>();
        private float _spawnQueueInterval = 0.8f; // 每只怪物间隔0.8秒
        private float _spawnQueueAccum = 0f;
        private float _legionTimer = LEGION_DURATION; // 倒计时
        private float _incomeAccum = 0f;              // 收入计时器

        // 玩家槽位
        private readonly Dictionary<int, PlayerSlot> _playerSlots = new();

        // 进攻方兵种解锁状态
        // index: 0=重甲兵 1=速度兵 2=召唤师 3=刺客 4=泰坦 5=暗影领主
        private readonly bool[] _attackerUnlocked = new bool[6];

        // 解锁费用
        private static readonly int[] UNLOCK_COSTS = { 100, 100, 200, 200, 400, 400 };

        // 解锁前置条件（需要先解锁哪个节点）
        // -1=无前置 直接可解锁


        // 对应的怪物type
        //  0=重甲→type3  1=召唤→type5  2=泰坦→type7
        // 3=速度→type4  4=刺客→type6  5=暗影→type8
        //private static readonly int[] UNLOCK_TROOP_TYPE = { 3, 4, 5, 6, 7, 8 };
        private static readonly int[] UNLOCK_PREREQ = { -1, 0, 1, -1, 3, 4 };
        private static readonly int[] UNLOCK_TROOP_TYPE = { 3, 5, 7, 4, 6, 8 };
        //private static readonly int[] UNLOCK_PREREQ = { -1, 0, 1, -1, 3, 4 };
        public GameRoom(GameMode mode)
        {
            Mode = mode;
            // 两个模式都用中心基地，四角/四边出生
            _grid = new GameGrid(MAP_W, MAP_H, centerBase: true);
            _gold[0] = _gold[1] = INIT_GOLD;
            _baseHp[0] = _baseHp[1] = BASE_MAX_HP;
        }

        // ── 玩家加入 ──────────────────────────────────────────────

        public bool TryAddPlayer(ClientSession session)
        {
            lock (_sessionLock)
            {
                if (_sessions.Count >= 2) return false;
                _sessions[session.PlayerId] = session;

                _playerSlots[session.PlayerId] = new PlayerSlot
                {
                    playerId = session.PlayerId,
                    playerName = $"Player{session.PlayerId}",
                    role = -1,
                    ready = false,
                };

                BroadcastRoomState();

                if (_sessions.Count == 2)
                {
                    State = RoomState.Selecting;
                    Console.WriteLine("[Room] 双方已就位，进入角色选择阶段");
                    BroadcastRoomState();
                }

                return true;
            }
        }
        private void HandleSelectRole(IncomingCommand cmd)
        {
            if (State != RoomState.Selecting) return;

            var req = PacketHelper.Deserialize<C2S_SelectRolePayload>(cmd.Payload);
            int pid = cmd.PlayerId;

            Console.WriteLine($"[Room] P{pid} 选择 mode={req.mode} role={req.role}");

            if (!_playerSlots.ContainsKey(pid)) return;
            if (_playerSlots[pid].ready) return;

            // 更新模式和角色到槽位
            _playerSlots[pid].mode = req.mode;

            // Legion模式才处理角色
            if (req.mode == 1 && req.role >= 0)
            {
                bool roleTaken = _playerSlots.Values
                    .Any(s => s.playerId != pid && s.role == req.role && s.mode == 1);

                if (roleTaken)
                {
                    SendTo(pid, PacketHelper.Pack(PacketType.S2C_Error,
                        new S2C_ErrorPayload
                        {
                            code = 3,
                            msg = req.role == 0 ? "防守方已被对方选择" : "进攻方已被对方选择"
                        }));
                    return;
                }
                _playerSlots[pid].role = req.role;
            }
            else if (req.mode == 0)
            {
                // 切换到合作模式时清空角色选择
                _playerSlots[pid].role = -1;
            }
            else if (req.role == -1)
            {
                // 只改了模式没选角色
                _playerSlots[pid].role = -1;
            }

            BroadcastRoomState();
        }
        private void BroadcastRoomState()
        {
            var payload = new S2C_RoomStatePayload
            {
                players = _playerSlots.Values.ToList(),
                mode = (int)Mode,
                roomState = State switch
                {
                    RoomState.WaitingPlayers => 0,
                    RoomState.Selecting => 1,
                    _ => 2,
                },
            };
            Broadcast(PacketHelper.Pack(PacketType.S2C_RoomState, payload));
        }
        public void SetMode(GameMode mode)
        {
            if (State != RoomState.WaitingPlayers && State != RoomState.Selecting) return;
            Mode = mode;
            _grid = new GameGrid(MAP_W, MAP_H, centerBase: true);
            Console.WriteLine($"[Room] 模式已设置为: {mode}");
        }
        private void HandleReadyConfirm(IncomingCommand cmd)
        {
            if (State != RoomState.Selecting) return;

            int pid = cmd.PlayerId;
            if (!_playerSlots.ContainsKey(pid)) return;

            var req = PacketHelper.Deserialize<C2S_SelectRolePayload>(cmd.Payload);

            Console.WriteLine($"[Room] P{pid} 准备 mode={req.mode} role={req.role}");

            // 更新该玩家的模式和角色
            _playerSlots[pid].mode = req.mode;
            _playerSlots[pid].role = req.role;
            _playerSlots[pid].ready = true;

            BroadcastRoomState();
            CheckAllReady();
        }

        private void CheckAllReady()
        {
            if (_playerSlots.Count < 2) return;
            if (!_playerSlots.Values.All(s => s.ready)) return;

            // 检查双方模式是否一致
            var modes = _playerSlots.Values.Select(s => s.mode).Distinct().ToList();
            if (modes.Count > 1)
            {
                Broadcast(PacketHelper.Pack(PacketType.S2C_Error,
                    new S2C_ErrorPayload { code = 7, msg = "双方选择的模式不一致！" }));
                foreach (var s in _playerSlots.Values) s.ready = false;
                BroadcastRoomState();
                return;
            }

            // 用双方一致的模式
            var finalMode = modes[0] == 1 ? GameMode.Legion : GameMode.Alliance;
            if (Mode != finalMode)
            {
                Mode = finalMode;
                _grid = new GameGrid(MAP_W, MAP_H, centerBase: true);
            }

            Console.WriteLine($"[Room] 双方都准备好了！最终模式={Mode}");

            if (Mode == GameMode.Legion)
            {
                // 检查角色
                var roles = _playerSlots.Values.Select(s => s.role).ToList();
                if (roles.Any(r => r < 0))
                {
                    Broadcast(PacketHelper.Pack(PacketType.S2C_Error,
                        new S2C_ErrorPayload { code = 4, msg = "Legion模式请先选择角色" }));
                    foreach (var s in _playerSlots.Values) s.ready = false;
                    BroadcastRoomState();
                    return;
                }
                if (roles.Distinct().Count() != 2)
                {
                    Broadcast(PacketHelper.Pack(PacketType.S2C_Error,
                        new S2C_ErrorPayload { code = 5, msg = "角色冲突！请重新选择" }));
                    foreach (var s in _playerSlots.Values) s.ready = false;
                    BroadcastRoomState();
                    return;
                }

                _defenderPlayerId = -1;
                _attackerPlayerId = -1;
                foreach (var slot in _playerSlots.Values)
                {
                    if (slot.role == 0) _defenderPlayerId = slot.playerId;
                    if (slot.role == 1) _attackerPlayerId = slot.playerId;
                }
                Console.WriteLine($"[Room] 防守方=P{_defenderPlayerId} 进攻方=P{_attackerPlayerId}");
            }

            StartGame();
        }
        private List<List<Vec2Int>> _paths = new List<List<Vec2Int>>();
        private void StartGame()
        {
            State = RoomState.Running;
            BroadcastRoomState();

            // 路径计算必须在 Task.Delay 之前
            _paths.Clear();
            foreach (var spawn in _grid.SpawnPositions)
            {
                var found = _pathfinder.FindPath(_grid, spawn, _grid.BasePos);
                List<Vec2Int> path;
                if (found != null && found.Count > 0)
                    path = found;
                else
                    path = new List<Vec2Int> { spawn, _grid.BasePos };
                _paths.Add(path);
                Console.WriteLine($"[Room] 路径{_paths.Count - 1} 出生点=({spawn.x},{spawn.y}) 节点数={path.Count}");
            }
            _path = _paths[0];

            var payloadToSend = new S2C_GameStartPayload
            {
                mapWidth = MAP_W,
                mapHeight = MAP_H,
                spawnPos = _grid.SpawnPos,
                basePos = _grid.BasePos,
                initGold = Mode == GameMode.Legion ? 2000 : INIT_GOLD,
                mode = (int)Mode,
                initPath = _path.ToArray(),
            };

            var pathsSnapshot = _paths.Select(p => p.ToArray()).ToList();

            Console.WriteLine($"[Room] 游戏开始！模式={Mode} 路径数={pathsSnapshot.Count}");

            Task.Delay(800).ContinueWith(_ =>
            {
                Broadcast(PacketHelper.Pack(PacketType.S2C_GameStart, payloadToSend));

                for (int i = 0; i < pathsSnapshot.Count; i++)
                {
                    Console.WriteLine($"[Room] 广播路径{i} 节点数={pathsSnapshot[i].Length}");
                    Broadcast(PacketHelper.Pack(PacketType.S2C_PathUpdate,
                        new S2C_PathUpdatePayload
                        {
                            path = pathsSnapshot[i],
                            field = i,
                        }));
                }

                Console.WriteLine($"[Room] 广播GameStart mode={Mode}");
            });
        }

        // ── 接收指令入队（被ClientSession的接收线程调用）──────────

        public void EnqueueCommand(IncomingCommand cmd) => _cmdQueue.Enqueue(cmd);

        // ── 主Game Loop（由外部定时器驱动）───────────────────────

        private bool _needExtraBroadcast = false;

        public void Tick(float deltaTime)
        {
            if (State == RoomState.Selecting) { ProcessCommands(); return; }
            if (State != RoomState.Running) return;

            // 上一帧需要额外广播
            if (_needExtraBroadcast)
            {
                _needExtraBroadcast = false;
                BroadcastState();
            }

            ProcessCommands();
            UpdateMonsters(deltaTime);

            int countBefore = _monsters.Count;
            CleanupDeadMonsters();
            int countAfter = _monsters.Count;

            if (countAfter < countBefore)
                _needExtraBroadcast = true;  // 标记下一帧额外广播

            UpdateTowers(deltaTime);
            UpdateWave(deltaTime);
            ProcessSpawnQueue(deltaTime);
            UpdateIncome(deltaTime);

            if (Mode == GameMode.Legion)
                UpdateLegionTimer(deltaTime);

            BroadcastState();
            _tick++;
        }
        private void ProcessSpawnQueue(float deltaTime)
        {
            if (_spawnQueue.Count == 0) return;

            _spawnQueueAccum += deltaTime;
            if (_spawnQueueAccum < _spawnQueueInterval) return;

            _spawnQueueAccum = 0f;

            var (type, dirIdx) = _spawnQueue.Dequeue();
            var path = dirIdx < _paths.Count ? _paths[dirIdx] : _path;
            _monsters.Add(new Monster(type, new List<Vec2Int>(path), 0));
        }
        private void UpdateLegionTimer(float deltaTime)
        {
            _legionTimer -= deltaTime;

            // 每秒广播一次倒计时
            if (_tick % 20 == 0)
            {
                Broadcast(PacketHelper.Pack(PacketType.S2C_WaveStart,
                    new S2C_WaveStartPayload
                    {
                        wave = (int)_legionTimer,  // 借用wave字段传倒计时秒数
                        totalMonsters = -1,                  // -1表示这是倒计时包
                    }));
            }

            // 倒计时结束，防守方胜利
            if (_legionTimer <= 0)
            {
                EndGame(true, $"防守成功！基地剩余血量 {_baseHp[0]}");
            }
        }

        private void UpdateIncome(float deltaTime)
        {
            _incomeAccum += deltaTime;
            if (_incomeAccum < INCOME_INTERVAL) return;
            _incomeAccum = 0;

            // 双方都获得时间收入
            _gold[0] += INCOME_AMOUNT;
            _gold[1] += INCOME_AMOUNT;

            BroadcastGold();
            Console.WriteLine($"[Room] 时间收入 +{INCOME_AMOUNT}G 防守:{_gold[0]} 进攻:{_gold[1]}");
        }
        // ── 指令处理 ─────────────────────────────────────────────

        private void ProcessCommands()
        {
            while (_cmdQueue.TryDequeue(out var cmd))
            {
                try
                {
                    switch (cmd.Type)
                    {
                        case PacketType.C2S_PlaceTower:   HandlePlaceTower(cmd);   break;
                        case PacketType.C2S_RemoveTower:  HandleRemoveTower(cmd);  break;
                        case PacketType.C2S_SendTroops:   HandleSendTroops(cmd);   break;
                        case PacketType.C2S_UpgradeTower: HandleUpgradeTower(cmd); break;
                        case PacketType.C2S_ChatMessage:  HandleChat(cmd);         break;
                        case PacketType.C2S_Heartbeat:    HandleHeartbeat(cmd);    break;
                        case PacketType.C2S_SelectRole: HandleSelectRole(cmd); break;
                        case PacketType.C2S_ReadyConfirm: HandleReadyConfirm(cmd); break;
                        case PacketType.C2S_JoinRoom: HandleJoinRoom(cmd); break;
                        case PacketType.C2S_CancelReady: HandleCancelReady(cmd); break;
                        case PacketType.C2S_ChooseBranch: HandleChooseBranch(cmd); break;
                        case PacketType.C2S_UnlockTroop: HandleUnlockTroop(cmd); break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Room] 处理指令异常 {cmd.Type}: {ex.Message}");
                }
            }
        }
        private void HandleUnlockTroop(IncomingCommand cmd)
        {
            int pid = cmd.PlayerId;

            if (pid != _attackerPlayerId)
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_UnlockTroopAck,
                    new S2C_UnlockTroopAckPayload { success = false, reason = "只有进攻方可以解锁兵种" }));
                return;
            }

            var req = PacketHelper.Deserialize<C2S_UnlockTroopPayload>(cmd.Payload);
            int idx = req.branch * 3 + req.tier;// 计算节点索引

            if (idx < 0 || idx >= 6)
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_UnlockTroopAck,
                    new S2C_UnlockTroopAckPayload { success = false, reason = "无效的解锁节点" }));
                return;
            }

            if (_attackerUnlocked[idx])
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_UnlockTroopAck,
                    new S2C_UnlockTroopAckPayload { success = false, reason = "已经解锁了", newGold = _gold[pid] }));
                return;
            }

            // 检查前置
            int prereq = UNLOCK_PREREQ[idx];
            if (prereq >= 0 && !_attackerUnlocked[prereq])
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_UnlockTroopAck,
                    new S2C_UnlockTroopAckPayload { success = false, reason = "需要先解锁前置兵种", newGold = _gold[pid] }));
                return;
            }

            int cost = UNLOCK_COSTS[idx];
            if (_gold[pid] < cost)
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_UnlockTroopAck,
                    new S2C_UnlockTroopAckPayload { success = false, reason = $"金币不足，需要{cost}G", newGold = _gold[pid] }));
                return;
            }

            _gold[pid] -= cost;
            _attackerUnlocked[idx] = true;
            BroadcastGold();

            SendTo(pid, PacketHelper.Pack(PacketType.S2C_UnlockTroopAck,
                new S2C_UnlockTroopAckPayload
                {
                    success = true,
                    branch = req.branch,
                    tier = req.tier,
                    newGold = _gold[pid]
                }));

            // 广播解锁树状态
            Broadcast(PacketHelper.Pack(PacketType.S2C_TroopTree,
                new S2C_TroopTreePayload
                {
                    playerId = pid,
                    unlocked = _attackerUnlocked.ToArray()
                }));

            Console.WriteLine($"[Room] 进攻方解锁兵种 idx={idx} 花费{cost}G");
        }
        private void HandleChooseBranch(IncomingCommand cmd)
        {
            int pid = cmd.PlayerId;
            var req = PacketHelper.Deserialize<C2S_ChooseBranchPayload>(cmd.Payload);

            // Legion模式进攻方不能升塔
            if (Mode == GameMode.Legion && pid == _attackerPlayerId)
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_BranchAck, new S2C_BranchAckPayload
                {
                    success = false,
                    reason = "进攻方不能升级防御塔",
                    newGold = _gold[pid]
                }));
                return;
            }

            var tower = _towers.Find(t => t.GridX == req.gridX && t.GridY == req.gridY);
            if (tower == null)
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_BranchAck, new S2C_BranchAckPayload
                {
                    success = false,
                    reason = "找不到该塔",
                    newGold = _gold[pid]
                }));
                return;
            }

            if (tower.Level != 2)
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_BranchAck, new S2C_BranchAckPayload
                {
                    success = false,
                    reason = "需要先升到2级",
                    newGold = _gold[pid]
                }));
                return;
            }

            int cost = TowerConfig.GetBranchCost(tower.Type, req.branch);
            if (_gold[pid] < cost)
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_BranchAck, new S2C_BranchAckPayload
                {
                    success = false,
                    reason = $"金币不足，需要{cost}G",
                    newGold = _gold[pid]
                }));
                return;
            }

            if (!tower.ChooseBranch(req.branch))
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_BranchAck, new S2C_BranchAckPayload
                {
                    success = false,
                    reason = "分支选择失败",
                    newGold = _gold[pid]
                }));
                return;
            }

            // 扣费
            if (Mode == GameMode.Alliance)
                _gold[0] = _gold[1] = _gold[0] - cost;
            else
                _gold[pid] -= cost;

            BroadcastGold();

            Broadcast(PacketHelper.Pack(PacketType.S2C_BranchAck, new S2C_BranchAckPayload
            {
                success = true,
                gridX = req.gridX,
                gridY = req.gridY,
                branch = req.branch,
                newGold = _gold[pid]
            }));

            Console.WriteLine($"[Room] P{pid} 塔({req.gridX},{req.gridY}) 选择分支{req.branch} 花费{cost}G");
        }
        
        private void HandleJoinRoom(IncomingCommand cmd)
        {
            var req = PacketHelper.Deserialize<C2S_JoinRoomPayload>(cmd.Payload);
            // 用第一个玩家发来的模式设置房间模式
            if (cmd.PlayerId == 0)
                SetMode(req.mode == 1 ? GameMode.Legion : GameMode.Alliance);

            Console.WriteLine($"[Room] P{cmd.PlayerId} 加入房间 模式={Mode}");
        }
        private void HandleCancelReady(IncomingCommand cmd)
        {
            if (State != RoomState.Selecting) return;
            int pid = cmd.PlayerId;
            if (!_playerSlots.ContainsKey(pid)) return;

            _playerSlots[pid].ready = false;
            Console.WriteLine($"[Room] P{pid} 取消准备");
            BroadcastRoomState();
        }
        private void HandlePlaceTower(IncomingCommand cmd)
        {
            int pid = cmd.PlayerId;
            Console.WriteLine($"[Room] 建塔请求 P{pid} 当前模式={Mode}");
            // Legion模式：进攻方（Player1）不能建塔
            if (Mode == GameMode.Legion && pid == _attackerPlayerId)
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_PlaceAck, new S2C_PlaceAckPayload
                { success = false, reason = "进攻方不能建塔", newGold = _gold[pid] }));
                return;
            }

            var req = PacketHelper.Deserialize<C2S_PlaceTowerPayload>(cmd.Payload);
            var cfg = TowerConfig.Get(req.towerType);

            if (_gold[pid] < cfg.Cost)
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_PlaceAck, new S2C_PlaceAckPayload
                {
                    success = false,
                    gridX = req.gridX,
                    gridY = req.gridY,
                    reason = "金币不足",
                    newGold = _gold[pid]
                }));
                return;
            }

            if (!_grid.TryPlaceTower(req.gridX, req.gridY, _pathfinder, _monsters))
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_PlaceAck, new S2C_PlaceAckPayload
                {
                    success = false,
                    gridX = req.gridX,
                    gridY = req.gridY,
                    reason = "位置非法或会封路",
                    newGold = _gold[pid]
                }));
                return;
            }

            if (Mode == GameMode.Alliance)
                _gold[0] = _gold[1] = _gold[0] - cfg.Cost; // 合作模式共享金币
            else
                _gold[pid] -= cfg.Cost; // 对抗模式各自扣费
            _towers.Add(new Tower(req.gridX, req.gridY, req.towerType, pid));
            Console.WriteLine($"[Room] 准备调用RefreshPath");
            RefreshPath(); Console.WriteLine($"[Room] RefreshPath调用完成");
            //Console.WriteLine($"[Room] 路径刷新完成 当前怪物数={_monsters.Count}");
            BroadcastGold();

            Broadcast(PacketHelper.Pack(PacketType.S2C_PlaceAck, new S2C_PlaceAckPayload
            { success = true, gridX = req.gridX, gridY = req.gridY, newGold = _gold[pid] }));
        }

        private void HandleRemoveTower(IncomingCommand cmd)
        {
            var req = PacketHelper.Deserialize<C2S_RemoveTowerPayload>(cmd.Payload);
            int pid = cmd.PlayerId;

            var tower = _towers.Find(t => t.GridX == req.gridX && t.GridY == req.gridY);
            if (tower == null) return;

            if (_grid.TryRemoveTower(req.gridX, req.gridY))
            {
                _towers.Remove(tower);
                // 退还50%金币
                int refund = TowerConfig.Get(tower.Type).Cost / 2;
                if (Mode == GameMode.Alliance)
                    _gold[0] = _gold[1] = _gold[0] + refund;
                else
                    _gold[pid] += refund;

                RefreshPath();
                BroadcastGold();
                Broadcast(PacketHelper.Pack(PacketType.S2C_RemoveAck,
                    new S2C_PlaceAckPayload { success=true, gridX=req.gridX, gridY=req.gridY, newGold=_gold[pid] }));
            }
        }

        private void HandleSendTroops(IncomingCommand cmd)
        {
            if (Mode != GameMode.Legion) return;

            int pid = cmd.PlayerId;
            if (pid == _defenderPlayerId) return;

            var req = PacketHelper.Deserialize<C2S_SendTroopsPayload>(cmd.Payload);

            // 检查高级兵种是否已解锁
            if (req.troopType >= 3)
            {
                int unlockIdx = req.troopType switch
                {
                    3 => 0,  // 重甲 → index0
                    5 => 1,  // 召唤 → index1
                    7 => 2,  // 泰坦 → index2
                    4 => 3,  // 速度 → index3
                    6 => 4,  // 刺客 → index4
                    8 => 5,  // 暗影 → index5
                    _ => -1
                };

                if (unlockIdx < 0 || !_attackerUnlocked[unlockIdx])
                {
                    SendTo(pid, PacketHelper.Pack(PacketType.S2C_Error,
                        new S2C_ErrorPayload { code = 8, msg = "该兵种尚未解锁" }));
                    return;
                }
            }

            int cost = req.troopType switch
            {
                1 => 120,   // 精英兵
                2 => 300,   // BOSS
                3 => 80,    // 重甲兵
                4 => 60,    // 速度兵
                5 => 150,   // 召唤师
                6 => 200,   // 刺客
                7 => 400,   // 泰坦
                8 => 350,   // 暗影领主
                _ => 50,    // 普通兵
            };

            int count = req.troopType switch
            {
                1 => 3,   // 精英兵
                2 => 1,   // BOSS
                3 => 4,   // 重甲兵
                4 => 6,   // 速度兵
                5 => 2,   // 召唤师
                6 => 2,   // 刺客
                7 => 1,   // 泰坦
                8 => 1,   // 暗影领主
                _ => 5,   // 普通兵
            };

            if (_gold[pid] < cost)
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_Error,
                    new S2C_ErrorPayload { code = 2, msg = $"金币不足，需要{cost}G" }));
                return;
            }

            _gold[pid] -= cost;
            BroadcastGold();

            int dirIdx = req.direction < _paths.Count ? req.direction : 0;

            for (int i = 0; i < count; i++)
                _spawnQueue.Enqueue((req.troopType, dirIdx));

            Console.WriteLine($"[Room] P{pid} 发兵 {count}x type{req.troopType} 方向={req.direction} 花费{cost}G");
        }

        private void HandleUpgradeTower(IncomingCommand cmd)
        {
            int pid = cmd.PlayerId;
            if (Mode == GameMode.Legion && pid == _attackerPlayerId)
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_Error,
                    new S2C_ErrorPayload { code = 7, msg = "进攻方不能升级防御塔" }));
                return;
            }
            var req = PacketHelper.Deserialize<C2S_UpgradeTowerPayload>(cmd.Payload);
            var tower = _towers.Find(t => t.GridX == req.gridX && t.GridY == req.gridY);
            if (tower == null) return;

           

            // 满级拒绝
            if (tower.Level >= 3)
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_PlaceAck, new S2C_PlaceAckPayload
                {
                    success = false,
                    gridX = req.gridX,
                    gridY = req.gridY,
                    reason = "已达最高等级",
                    newGold = _gold[pid]
                }));
                return;
            }

            int cost = TowerConfig.GetUpgradeCost(tower.Type, tower.Level);

            // 校验金币
            if (_gold[pid] < cost)
            {
                SendTo(pid, PacketHelper.Pack(PacketType.S2C_PlaceAck, new S2C_PlaceAckPayload
                {
                    success = false,
                    gridX = req.gridX,
                    gridY = req.gridY,
                    reason = $"金币不足，需要{cost}G",
                    newGold = _gold[pid]
                }));
                return;
            }

            // 扣费升级
            if (Mode == GameMode.Alliance)
                _gold[0] = _gold[1] = _gold[0] - cost;
            else
                _gold[pid] -= cost;

            tower.Upgrade();
            BroadcastGold();

            Console.WriteLine($"[Room] P{pid} 升级塔({req.gridX},{req.gridY}) -> Lv{tower.Level} 花费{cost}G");
        }

        private void HandleChat(IncomingCommand cmd)
        {
            var req = PacketHelper.Deserialize<C2S_ChatPayload>(cmd.Payload);
            string name = "";
            lock (_sessionLock)
            {
                if (_sessions.TryGetValue(cmd.PlayerId, out var s)) name = s.PlayerName;
            }
            Broadcast(PacketHelper.Pack(PacketType.S2C_ChatMessage,
                new S2C_ChatPayload { fromId=cmd.PlayerId, fromName=name, text=req.text }));
        }

        private void HandleHeartbeat(IncomingCommand cmd)
        {
            SendTo(cmd.PlayerId, PacketHelper.Pack(PacketType.S2C_Heartbeat));
        }

        // ── 怪物更新 ─────────────────────────────────────────────

        private void UpdateMonsters(float dt)
        {
            foreach (var m in _monsters)
            {
                if (m.IsDead) continue;
                m.Update(dt);

                // 被围住时攻击最近的塔


                if (m.Reached)
                {
                    int field = Mode == GameMode.Alliance ? 0 : m.Field;
                    var def = MonsterConfig.Get(m.Type);
                    _baseHp[field] = Math.Max(0, _baseHp[field] - def.Damage);

                    Broadcast(PacketHelper.Pack(PacketType.S2C_BaseHpUpdate,
                        new S2C_BaseHpUpdatePayload { field = field, hp = _baseHp[field], maxHp = BASE_MAX_HP }));

                    if (_baseHp[field] <= 0)
                    {
                        EndGame(false, "基地被攻破！");
                        return;
                    }
                }
            }
        }
        private void BroadcastTowerDestroyed(int x, int y)
        {
            Broadcast(PacketHelper.Pack(PacketType.S2C_TowerDestroyed,
                new S2C_TowerDestroyedPayload { gridX = x, gridY = y }));
        }
        // ── 塔攻击 ──────────────────────────────────────────────

        private readonly List<LavaZone> _lavaZones = new();

        private void UpdateTowers(float dt)
        {
            // 计算增幅塔的加速效果
            float attackMult = 1.0f;
            foreach (var t in _towers)
            {
                if (!t.IsAmplifier) continue;
                float ax = t.GridX + 0.5f, ay = t.GridY + 0.5f;
                foreach (var other in _towers)
                {
                    if (other == t) continue;
                    float dx = other.GridX + 0.5f - ax;
                    float dy = other.GridY + 0.5f - ay;
                    if (MathF.Sqrt(dx * dx + dy * dy) <= t.AmplifyRange)
                        attackMult = 0.7f; // 攻速提升30%（interval缩短）
                }
            }

            var toAdd = new List<Bullet>();
            foreach (var tower in _towers)
            {
                var hits = tower.TryAttack(_monsters, dt, _lavaZones, -1, attackMult);
                foreach (var hit in hits)
                {
                    toAdd.Add(new Bullet(
                        tower.GridX + 0.5f, tower.GridY + 0.5f,
                        hit.Id, tower.Type, tower.Branch));
                }
            }
            _bullets.AddRange(toAdd);

            // 更新岩浆区域
            foreach (var lava in _lavaZones)
                lava.Update(dt, _monsters);
            _lavaZones.RemoveAll(l => l.IsExpired);
        }

        // ── 波次 ─────────────────────────────────────────────────

        private void UpdateWave(float dt)
        {
            if (Mode == GameMode.Legion) return;
            if (_spawning)
            {
                _spawnAccum += dt;
                if (_spawnAccum >= _spawnInterval && _monstersLeftInWave > 0)
                {
                    _spawnAccum = 0;
                    _monstersLeftInWave--;

                    if (Mode == GameMode.Alliance)
                    {
                        // Alliance：随机出生点
                        SpawnOneMonster();
                    }
                    else
                    {
                        // Legion：固定出生点
                        _monsters.Add(new Monster(_spawnType, new List<Vec2Int>(_path), 0));
                    }

                    if (_monstersLeftInWave == 0) _spawning = false;
                }
                return;
            }

            _waveTimer -= dt;
            if (_waveTimer > 0) return;

            _currentWave++;
            if (_currentWave > MAX_WAVE)
            {
                EndGame(true, $"恭喜通过全部{MAX_WAVE}波！");
                return;
            }

            // 波次配置
            int count    = 5 + _currentWave * 3;
            int type     = _currentWave <= 3 ? 0 : (_currentWave <= 6 ? 1 : 2);
            _spawnInterval    = Math.Max(0.3f, 1.5f - _currentWave * 0.1f);
            _monstersLeftInWave = count;
            _spawnType   = type;
            _spawning    = true;
            _waveTimer   = 15f; // 下波间隔

            Broadcast(PacketHelper.Pack(PacketType.S2C_WaveStart,
                new S2C_WaveStartPayload { wave=_currentWave, totalMonsters=count }));

            Console.WriteLine($"[Room] Wave {_currentWave} 开始！type={type} count={count}");
        }

        // ── 清理死亡怪物 ─────────────────────────────────────────

        private void CleanupDeadMonsters()
        {
            var dead = _monsters.Where(m => m.IsDead || m.Reached).ToList();
            bool goldChanged = false;
            Console.WriteLine($"[Room] CleanupDead 检查 总数={_monsters.Count}");
            foreach (var m in dead)
            {
                if (m.IsDead)
                {
                    int reward = MonsterConfig.Get(m.Type).Reward;
                    if (Mode == GameMode.Alliance)
                        _gold[0] = _gold[1] = _gold[0] + reward;
                    else
                        _gold[_defenderPlayerId] += reward;
                    goldChanged = true;
                }
                Console.WriteLine($"  怪物{m.Id} IsDead={m.IsDead} Reached={m.Reached}");
            }

            // 一次性移除所有死亡怪物
            _monsters.RemoveAll(m => m.IsDead || m.Reached);

            if (goldChanged) BroadcastGold();
        }

        // ── 路径刷新 ─────────────────────────────────────────────
        private void SpawnOneMonster()
        {
            // 随机选一个出生点
            int spawnIdx = _random.Next(_paths.Count);
            var path = _paths[spawnIdx];
            _monsters.Add(new Monster(_spawnType, new List<Vec2Int>(path), 0));
        }

        private readonly Random _random = new Random();
        private void RefreshPath()
        {
            _paths.Clear();
            foreach (var spawn in _grid.SpawnPositions)
            {
                var found = _pathfinder.FindPath(_grid, spawn, _grid.BasePos);
                List<Vec2Int> path;
                if (found != null && found.Count > 0)
                    path = found;
                else if (_paths.Count > 0)
                    path = _paths[0];
                else
                    path = new List<Vec2Int> { spawn, _grid.BasePos };
                _paths.Add(path);
            }
            _path = _paths[0];

            foreach (var m in _monsters.Where(m => !m.IsDead && !m.Reached))
            {
                int pathIdx = m.Field < _paths.Count ? m.Field : 0;
                m.UpdatePath(new List<Vec2Int>(_paths[pathIdx]));
            }

            for (int i = 0; i < _paths.Count; i++)
            {
                Broadcast(PacketHelper.Pack(PacketType.S2C_PathUpdate,
                    new S2C_PathUpdatePayload
                    {
                        path = _paths[i].ToArray(),
                        field = i,
                    }));
            }
        }

        // ── 广播 ─────────────────────────────────────────────────

        private void BroadcastState()
        {
            var sync = new S2C_StateSyncPayload
            {
                tick     = _tick,
                monsters = _monsters.Select(m => m.ToState()).ToList(),
                towers   = _towers.Select(t => t.ToState()).ToList(),
                bullets  = _bullets.Select(b => b.ToState()).ToList(),
            }; 
           
            Broadcast(PacketHelper.Pack(PacketType.S2C_StateSync, sync));
            Console.WriteLine($"[Room] BroadcastState monsters={sync.monsters.Count} ids={string.Join(",", sync.monsters.Select(m => m.id))}");
            _bullets.Clear(); // 子弹一帧后清除
        }

        private void BroadcastGold()
        {
            lock (_sessionLock)
            {
                foreach (var kv in _sessions)
                {
                    int pid = kv.Key;
                    // 合作模式共享金币，都用_gold[0]
                    int gold = Mode == GameMode.Alliance ? _gold[0] : _gold[pid];
                    kv.Value.Send(PacketHelper.Pack(PacketType.S2C_ResourceUpdate,
                        new S2C_ResourceUpdatePayload { playerId = pid, gold = gold }));
                }
            }
        }

        private void Broadcast(byte[] data)
        {
            lock (_sessionLock)
            {
                foreach (var s in _sessions.Values)
                    s.Send(data);
            }
        }

        private void SendTo(int playerId, byte[] data)
        {
            lock (_sessionLock)
            {
                if (_sessions.TryGetValue(playerId, out var s))
                    s.Send(data);
            }
        }

        // ── 游戏结束 ─────────────────────────────────────────────

        private void EndGame(bool win, string reason)
        {
            if (State == RoomState.GameOver) return;
            State = RoomState.GameOver;
            Broadcast(PacketHelper.Pack(PacketType.S2C_GameOver,
                new S2C_GameOverPayload { win=win, reason=reason, finalWave=_currentWave }));
            Console.WriteLine($"[Room] 游戏结束 win={win} reason={reason}");
        }

        public void OnPlayerDisconnect(int playerId)
        {
            lock (_sessionLock) { _sessions.Remove(playerId); }

            if (State == RoomState.Running)
            {
                Broadcast(PacketHelper.Pack(PacketType.S2C_PlayerDisconnect,
                    new S2C_ErrorPayload { code = playerId, msg = $"Player{playerId}已断线，游戏中止。" }));
                EndGame(false, $"Player{playerId}已断线，游戏中止。");
            }
            else if (State == RoomState.Selecting)
            {
                // 选角阶段断线，通知另一方
                Broadcast(PacketHelper.Pack(PacketType.S2C_PlayerDisconnect,
                    new S2C_ErrorPayload { code = playerId, msg = $"Player{playerId}已断线。" }));
            }

            Console.WriteLine($"[Server] P{playerId} 断线，当前房间人数={_sessions.Count}");
        }
    }
}
