using System.Collections.Generic;

namespace MazeTD.Shared
{
    public class Vec2Int
    {
        public int x;
        public int y;
        public Vec2Int() { }
        public Vec2Int(int x, int y) { this.x = x; this.y = y; }
        public override string ToString() => $"({x},{y})";
    }

    // ── C2S Payloads ─────────────────────────────────────

    public class C2S_JoinRoomPayload
    {
        public string playerName = "";
        public int mode = 0;
        public int role = -1; // 加这行
    }

    public class C2S_PlaceTowerPayload
    {
        public int gridX;
        public int gridY;
        public int towerType;
    }

    public class C2S_RemoveTowerPayload
    {
        public int gridX;
        public int gridY;
    }

    
    public class C2S_SendTroopsPayload
    {
        public int troopType;
        public int count;
        public int direction; // 0=下 1=上 2=左 3=右
    }

    public class C2S_UpgradeTowerPayload
    {
        public int gridX;
        public int gridY;
    }

    public class C2S_ChatPayload
    {
        public string text = "";
    }

    // ── S2C Payloads ─────────────────────────────────────

    public class S2C_JoinAckPayload
    {
        public bool success;
        public int playerId;
        public string reason = "";
    }

    public class S2C_GameStartPayload
    {
        public int mapWidth;
        public int mapHeight;
        public Vec2Int spawnPos;
        public Vec2Int basePos;
        public int initGold;
        public int mode;
        public Vec2Int[] initPath;
    }

    public class MonsterState
    {
        public int id;
        public int type;
        public float x;
        public float y;
        public int hp;
        public int maxHp;
        public float speed;
        public int pathIndex;
        public int field;
        public int reward;
    }

    public class TowerState
    {
        public int gridX;
        public int gridY;
        public int type;
        public int level;
        public int ownerId;
        public int branch;
        public int hp;      // 新增
        public int maxHp;   // 新增
    }

    public class BulletState
    {
        public int id;
        public float x;
        public float y;
        public int targetId;
        public int towerType;
        public int branch;
    }

    public class S2C_StateSyncPayload
    {
        public long tick;
        public List<MonsterState> monsters = new();
        public List<TowerState> towers = new();
        public List<BulletState> bullets = new();
    }

    public class S2C_PlaceAckPayload
    {
        public bool success;
        public int gridX;
        public int gridY;
        public string reason = "";
        public int newGold;
    }

    public class S2C_PathUpdatePayload
    {
        public Vec2Int[] path;
        public int field;
    }

    public class S2C_ResourceUpdatePayload
    {
        public int playerId;
        public int gold;
    }

    public class S2C_BaseHpUpdatePayload
    {
        public int field;
        public int hp;
        public int maxHp;
    }

    public class S2C_WaveStartPayload
    {
        public int wave;
        public int totalMonsters;
    }

    public class S2C_GameOverPayload
    {
        public bool win;
        public string reason = "";
        public int finalWave;
    }

    public class S2C_ChatPayload
    {
        public int fromId;
        public string fromName = "";
        public string text = "";
    }
    public class PlayerSlot
    {
        public int playerId;
        public string playerName = "";
        public int role = -1;
        public bool ready;
        public int mode;
    }
    public class S2C_ErrorPayload
    {
        public int code;
        public string msg = "";
    }
    public class S2C_TowerDestroyedPayload
    {
        public int gridX;
        public int gridY;
    }
    public class C2S_SelectRolePayload
    {
        public int role;
        public int mode;  // 改成public
    }


    public class S2C_RoomStatePayload
    {
        public List<PlayerSlot> players = new List<PlayerSlot>();
        public int mode;
        public int roomState;
    }
    public class C2S_ChooseBranchPayload
    {
        public int gridX;
        public int gridY;
        public int branch;  // 1=A分支 2=B分支
    }

    public class S2C_BranchAckPayload
    {
        public bool success;
        public int gridX;
        public int gridY;
        public int branch;
        public string reason = "";
        public int newGold;
    }
    // 解锁兵种请求
    public class C2S_UnlockTroopPayload
    {
        public int branch;  // 0=重甲路线 1=速攻路线
        public int tier;    // 0=初级 1=精英 2=BOSS
    }

    // 解锁回包
    public class S2C_UnlockTroopAckPayload
    {
        public bool success;
        public int branch;
        public int tier;
        public string reason = "";
        public int newGold;
    }

    // 广播进攻方解锁状态（防守方也能看到）
    public class S2C_TroopTreePayload
    {
        public int playerId;
        public bool[] unlocked = new bool[6]; // 6个节点的解锁状态
    }
}