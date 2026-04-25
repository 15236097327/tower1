using System.Collections.Generic;
using UnityEngine;
using MazeTD.Client.Network;
using MazeTD.Shared;

namespace MazeTD.Client.Game
{
    /// <summary>
    /// 3D版客户端中心调度器。
    ///
    /// 相比2D版改动：
    /// - 引用3D版子系统（GridRenderer3D, MonsterController3D, TowerPlacer3D）
    /// - 新增BulletRenderer3D和CameraController3D
    /// - StateSync中增加塔同步和子弹同步
    /// - 相机初始化
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("3D子系统引用")]
        public GridRenderer3D       GridRenderer;
        public MonsterController3D  MonsterController;
        public TowerPlacer3D        TowerPlacer;
        public BulletRenderer3D     BulletRenderer;
        public UIManager            UIManager;
        public CameraController3D   CameraController;

        // 本地状态
        public int  LocalPlayerId   { get; private set; } = -1;
        public int  LocalGold       { get; private set; } = 0;
        public int  GameMode        { get; private set; } = 0;
        public bool GameStarted     { get; private set; } = false;

        // 路径缓存
        public List<Vec2Int> CurrentPath  { get; private set; } = new();
        public List<Vec2Int> CurrentPath1 { get; private set; } = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (NetworkManager.Instance == null)
            {
                Debug.LogError("[GameManager] NetworkManager 不存在！");
                return;
            }
            LocalPlayerId = NetworkManager.Instance.LocalPlayerId;  // 加这行
            NetworkManager.Instance.OnPacketReceived += HandlePacket;
        }

        private void Start()
        {
           

           

            InvokeRepeating(nameof(SendHeartbeat), 5f, 5f);
        }

        // ── 包分发 ──────────────────────────────────────────────

        private void HandlePacket(PacketType type, byte[] payload)
        {
            Debug.Log($"[GameManager] HandlePacket 收到 {type}"); // 加这行
            switch (type)
            {
                case PacketType.S2C_JoinAck:         OnJoinAck(payload);         break;
                case PacketType.S2C_GameStart:        OnGameStart(payload);       break;
                case PacketType.S2C_StateSync:        OnStateSync(payload);       break;
                case PacketType.S2C_PlaceAck:         OnPlaceAck(payload);        break;
                case PacketType.S2C_PathUpdate:       OnPathUpdate(payload);      break;
                case PacketType.S2C_ResourceUpdate:   OnResourceUpdate(payload);  break;
                case PacketType.S2C_BaseHpUpdate:     OnBaseHpUpdate(payload);    break;
                case PacketType.S2C_WaveStart:        OnWaveStart(payload);       break;
                case PacketType.S2C_GameOver:         OnGameOver(payload);        break;
                case PacketType.S2C_ChatMessage:      OnChatMessage(payload);     break;
                case PacketType.S2C_PlayerDisconnect: OnPlayerDisconnect(payload);break;
                case PacketType.S2C_Heartbeat:        break;
                case PacketType.S2C_BranchAck: OnBranchAck(payload); break;
                case PacketType.S2C_UnlockTroopAck: OnUnlockTroopAck(payload); break;
                case PacketType.S2C_TroopTree: OnTroopTree(payload); break;
                case PacketType.S2C_TowerDestroyed: OnTowerDestroyed(payload); break;
                case PacketType.S2C_Error:
                    var err = PacketHelper.Deserialize<S2C_ErrorPayload>(payload);
                    UIManager?.ShowError(err.msg);
                    Debug.LogWarning($"[GameManager] 服务端错误 code={err.code} msg={err.msg}");
                    break;
                default:
                    Debug.LogWarning($"[GameManager] 未处理的包类型: {type}");
                    break;
            }
        }

        // ── 各包处理 ─────────────────────────────────────────────
        private void OnTowerDestroyed(byte[] payload)
        {
            var data = PacketHelper.Deserialize<S2C_TowerDestroyedPayload>(payload);
            UIManager?.ShowMessage($"塔({data.gridX},{data.gridY})被怪物摧毁！");
            // GridRenderer 会通过 StateSync 自动更新塔的显示
        }
        private void OnUnlockTroopAck(byte[] payload)
        {
            var ack = PacketHelper.Deserialize<S2C_UnlockTroopAckPayload>(payload);
            if (!ack.success)
            {
                UIManager?.OnUnlockFailed(ack.reason);
                return;
            }
            LocalGold = ack.newGold;
            UIManager?.UpdateGold(LocalGold);
        }

        private void OnTroopTree(byte[] payload)
        {
            var data = PacketHelper.Deserialize<S2C_TroopTreePayload>(payload);
            UIManager?.OnTroopTreeUpdate(data.unlocked);
        }

        public void RequestUnlockTroop(int branch, int tier)
        {
            NetworkManager.Instance.Send(PacketType.C2S_UnlockTroop,
                new C2S_UnlockTroopPayload { branch = branch, tier = tier });
        }
        private void OnBranchAck(byte[] payload)
        {
            var ack = PacketHelper.Deserialize<S2C_BranchAckPayload>(payload);
            if (!ack.success)
            {
                UIManager?.ShowError($"分支选择失败：{ack.reason}");
                return;
            }
            LocalGold = ack.newGold;
            UIManager?.UpdateGold(LocalGold);
        }
        public void RequestChooseBranch(int x, int y, int branch)
        {
            NetworkManager.Instance.Send(PacketType.C2S_ChooseBranch,
                new C2S_ChooseBranchPayload { gridX = x, gridY = y, branch = branch });
        }
        private void OnJoinAck(byte[] payload)
        {
            var ack = PacketHelper.Deserialize<S2C_JoinAckPayload>(payload);
            if (!ack.success)
            {
                Debug.LogError($"[GameManager] 加入失败: {ack.reason}");
                UIManager?.ShowError($"加入失败：{ack.reason}");
                return;
            }
            LocalPlayerId = ack.playerId;
            Debug.Log($"[GameManager] 加入成功！PlayerId={LocalPlayerId}");
            UIManager?.ShowMessage($"已加入房间，等待对方玩家...");
        }

        private void OnGameStart(byte[] payload)
        {
            var data = PacketHelper.Deserialize<S2C_GameStartPayload>(payload);
            GameMode = data.mode;
            LocalGold = data.initGold;
            GameStarted = true;
            CurrentPath = new List<Vec2Int>(data.initPath);

            int localRole = NetworkManager.Instance.LocalRole;
            int localMode = NetworkManager.Instance.LocalMode;

            GridRenderer?.InitGrid(data);

            if (CameraController != null)
                CameraController.SetMapBounds(data.mapWidth, data.mapHeight, GridRenderer?.CellSize ?? 1f);

            UIManager?.OnGameStart(data, LocalPlayerId, localRole);
        }

        private void OnStateSync(byte[] payload)
        {
            if (!GameStarted) return;
            var state = PacketHelper.Deserialize<S2C_StateSyncPayload>(payload);
            //Debug.Log($"[GameManager] StateSync monsters={state.monsters.Count}");
            var m1 = state.monsters.Find(m => m.id == 1);
            foreach (var m in state.monsters)
               // Debug.Log($"[StateSync] 怪物id={m.id} 位置=({m.x:F1},{m.y:F1})");
            // 怪物同步
            MonsterController?.ApplyState(state.monsters);

            // 塔同步（3D新增：实时更新塔模型）
            GridRenderer?.UpdateTowers(state.towers);
            UIManager?.UpdateTowerCache(state.towers);
            // 子弹同步（3D新增：子弹飞行动画）
            if (state.bullets != null && state.bullets.Count > 0)
                BulletRenderer?.ApplyBullets(state.bullets);
        }

        private void OnPlaceAck(byte[] payload)
        {
            var ack = PacketHelper.Deserialize<S2C_PlaceAckPayload>(payload);
            if (!ack.success)
            {
                UIManager?.ShowError($"建塔失败：{ack.reason}");
                TowerPlacer?.OnPlaceFailed(ack.gridX, ack.gridY);
            }
            else
            {
                LocalGold = ack.newGold;
                UIManager?.UpdateGold(LocalGold);
            }
        }

        private void OnPathUpdate(byte[] payload)
        {
            var data = PacketHelper.Deserialize<S2C_PathUpdatePayload>(payload);
            if (data.field == 0)
                CurrentPath = new List<Vec2Int>(data.path);
            else
                CurrentPath1 = new List<Vec2Int>(data.path);

            GridRenderer?.DrawPath(
                data.field == 0 ? CurrentPath : CurrentPath1,
                data.field);
        }

        private void OnResourceUpdate(byte[] payload)
        {
            var data = PacketHelper.Deserialize<S2C_ResourceUpdatePayload>(payload);
            Debug.Log($"[GameManager] ResourceUpdate playerId={data.playerId} gold={data.gold} LocalPlayerId={LocalPlayerId}");
            if (data.playerId == LocalPlayerId)
            {
                LocalGold = data.gold;
                UIManager?.UpdateGold(LocalGold);
            }
        }

        private void OnBaseHpUpdate(byte[] payload)
        {
            var data = PacketHelper.Deserialize<S2C_BaseHpUpdatePayload>(payload);
            GridRenderer?.UpdateBaseHpBar(data.hp, data.maxHp);  // 改这里
            
        }

        private void OnWaveStart(byte[] payload)
        {
            var data = PacketHelper.Deserialize<S2C_WaveStartPayload>(payload);
            if(data.totalMonsters==-1)
            {
                UIManager?.ShowLegionTimer(data.wave);
            }
            else
            {
               UIManager?.ShowWave(data.wave, data.totalMonsters);
            }
           
        }

        private void OnGameOver(byte[] payload)
        {
            var data = PacketHelper.Deserialize<S2C_GameOverPayload>(payload);
            UIManager?.ShowGameOver(data.win, data.reason, data.finalWave);
            GameStarted = false;
        }

        private void OnChatMessage(byte[] payload)
        {
            var data = PacketHelper.Deserialize<S2C_ChatPayload>(payload);
            UIManager?.AddChatMessage(data.fromName, data.text);
        }

        private void OnPlayerDisconnect(byte[] payload)
        {
            UIManager?.ShowError("对方玩家已断线，游戏结束。");
        }

        // ── 发包接口 ────────────────────────────────────────────

        public void RequestPlaceTower(int x, int y, int towerType)
        {
            NetworkManager.Instance.Send(PacketType.C2S_PlaceTower,
                new C2S_PlaceTowerPayload { gridX=x, gridY=y, towerType=towerType });
        }

        public void RequestRemoveTower(int x, int y)
        {
            NetworkManager.Instance.Send(PacketType.C2S_RemoveTower,
                new C2S_RemoveTowerPayload { gridX=x, gridY=y });
        }

        public void RequestSendTroops(int troopType, int count, int direction)
        {
            if (GameMode != 1) return;
            NetworkManager.Instance.Send(PacketType.C2S_SendTroops,
                new C2S_SendTroopsPayload
                {
                    troopType = troopType,
                    count = count,
                    direction = direction   // 传方向给服务端
                });
        }

        public void RequestUpgradeTower(int x, int y)
        {
            NetworkManager.Instance.Send(PacketType.C2S_UpgradeTower,
                new C2S_UpgradeTowerPayload { gridX=x, gridY=y });
        }

        public void SendChat(string text)
        {
            NetworkManager.Instance.Send(PacketType.C2S_ChatMessage,
                new C2S_ChatPayload { text=text });
        }

        private void SendHeartbeat()
        {
            if (NetworkManager.Instance.IsConnected)
                NetworkManager.Instance.Send(PacketType.C2S_Heartbeat);
        }

        private void OnDestroy()
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnPacketReceived -= HandlePacket;
        }
    }
}
