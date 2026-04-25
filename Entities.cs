using System;
using System.Collections.Generic;
using System.Linq;
using MazeTD.Shared;

namespace MazeTD.GameServer.Entity
{
    // ── 特殊效果枚举 ─────────────────────────────────────────────

    public enum SpecialEffect
    {
        None,
        Snipe,          // 狙击：暴击+超远射程
        Multishot,      // 速射：连射3箭穿透
        Knockback,      // 击退：命中击退怪物
        Cluster,        // 集束：同时攻击3个目标
        Freeze,         // 冰冻：完全停止移动
        Poison,         // 毒雾：DOT持续掉血
        ChainLightning, // 链式闪电：跳跃5个目标
        SoulHarvest,    // 灵魂收割：击杀回金币
        Lava,           // 岩浆：地面持续伤害区域
        Phoenix,        // 凤凰：召唤火焰鸟
        Amplify,        // 增幅：提升周围塔攻速
        GoldBoost,      // 金币：击杀额外掉落
    }

    // ── 分支定义 ─────────────────────────────────────────────────

    public record BranchDef(
        string Name,
        string Desc,
        int Cost,
        int Damage,
        float Range,
        float AttackInterval,
        SpecialEffect Effect
    );

    // ── 塔基础定义 ────────────────────────────────────────────────

    public record TowerBaseDef(
        int Cost,
        int Damage,
        float Range,
        float AttackInterval,
        string Name,
        string Desc,
        BranchDef BranchA,
        BranchDef BranchB,
        int Hp

    );
    

    // ── 塔配置表 ─────────────────────────────────────────────────

    public static class TowerConfig
    {
        public static readonly Dictionary<int, TowerBaseDef> Defs = new()
        {
            {
                0, new TowerBaseDef(
                    Cost: 50, Hp:300,Damage: 10, Range: 2.5f, AttackInterval: 1.0f,
                    Name: "箭塔", Desc: "基础远程攻击塔",
                    BranchA: new BranchDef("狙击手塔", "超远射程，50%概率暴击x2",
                        Cost: 100, Damage: 40, Range: 5.0f, AttackInterval: 2.0f,
                        Effect: SpecialEffect.Snipe),
                    BranchB: new BranchDef("速射塔", "连射3箭，箭矢穿透敌人",
                        Cost: 100, Damage: 15, Range: 2.5f, AttackInterval: 0.4f,
                        Effect: SpecialEffect.Multishot)
                )
            },
            {
                1, new TowerBaseDef(
                    Cost: 100, Hp:300,Damage: 35, Range: 2.0f, AttackInterval: 2.0f,
                    Name: "炮塔", Desc: "范围爆炸伤害塔",
                    BranchA: new BranchDef("重炮塔", "超高单体伤害，命中击退怪物",
                        Cost: 200, Damage: 150, Range: 2.0f, AttackInterval: 3.0f,
                        Effect: SpecialEffect.Knockback),
                    BranchB: new BranchDef("集束炮塔", "同时攻击3个目标",
                        Cost: 200, Damage: 50, Range: 2.5f, AttackInterval: 1.5f,
                        Effect: SpecialEffect.Cluster)
                )
            },
            {
                2, new TowerBaseDef(
                    Cost: 80, Hp:300,Damage: 5, Range: 2.5f, AttackInterval: 0.8f,
                    Name: "减速塔", Desc: "减慢怪物移动速度",
                    BranchA: new BranchDef("冰冻塔", "完全冰冻怪物，冰冻期间受击伤害翻倍",
                        Cost: 160, Damage: 8, Range: 2.5f, AttackInterval: 3.0f,
                        Effect: SpecialEffect.Freeze),
                    BranchB: new BranchDef("毒雾塔", "持续减速+毒素DOT，范围更大",
                        Cost: 160, Damage: 3, Range: 3.0f, AttackInterval: 0.5f,
                        Effect: SpecialEffect.Poison)
                )
            },
            {
                3, new TowerBaseDef(
                    Cost: 120, Hp:300,Damage: 20, Range: 3.0f, AttackInterval: 1.5f,
                    Name: "魔法塔", Desc: "魔法攻击，无视部分护甲",
                    BranchA: new BranchDef("闪电塔", "链式闪电跳跃5个目标，伤害递减70%",
                        Cost: 240, Damage: 30, Range: 3.0f, AttackInterval: 2.0f,
                        Effect: SpecialEffect.ChainLightning),
                    BranchB: new BranchDef("灵魂收割塔", "击杀怪物额外回50%金币",
                        Cost: 240, Damage: 25, Range: 3.0f, AttackInterval: 1.0f,
                        Effect: SpecialEffect.SoulHarvest)
                )
            },
            {
                4, new TowerBaseDef(
                    Cost: 110, Hp:300,Damage: 15, Range: 2.0f, AttackInterval: 0.6f,
                    Name: "火焰塔", Desc: "持续喷射火焰",
                    BranchA: new BranchDef("熔岩塔", "命中时在地面留下岩浆区域",
                        Cost: 220, Damage: 10, Range: 2.0f, AttackInterval: 0.5f,
                        Effect: SpecialEffect.Lava),
                    BranchB: new BranchDef("凤凰塔", "召唤火焰鸟绕圈攻击",
                        Cost: 220, Damage: 20, Range: 3.5f, AttackInterval: 0.8f,
                        Effect: SpecialEffect.Phoenix)
                )
            },
            {
                5, new TowerBaseDef(
                    Cost: 150, Hp:300,Damage: 0, Range: 3.0f, AttackInterval: 999f,
                    Name: "支援塔", Desc: "提升周围防御塔效果",
                    BranchA: new BranchDef("增幅塔", "周围塔攻速+30%，射程+0.5",
                        Cost: 300, Damage: 0, Range: 3.5f, AttackInterval: 999f,
                        Effect: SpecialEffect.Amplify),
                    BranchB: new BranchDef("金币塔", "全图击杀怪物金币+50%",
                        Cost: 300, Damage: 0, Range: 3.5f, AttackInterval: 999f,
                        Effect: SpecialEffect.GoldBoost)
                )
            },
        };

        public static TowerBaseDef Get(int type) =>
            Defs.TryGetValue(type, out var d) ? d : Defs[0];

        public static int GetUpgradeCost(int type, int currentLevel)
        {
            var def = Get(type);
            return currentLevel switch
            {
                1 => def.Cost,
                _ => 0
            };
        }

        public static int GetBranchCost(int type, int branch)
        {
            var def = Get(type);
            return branch == 1 ? def.BranchA.Cost : def.BranchB.Cost;
        }

        public static BranchDef? GetBranch(int type, int branch)
        {
            var def = Get(type);
            if (branch == 1) return def.BranchA;
            if (branch == 2) return def.BranchB;
            return null;
        }
    }

    // ── 怪物配置表 ────────────────────────────────────────────────

    public static class MonsterConfig
    {
        public record MonsterDef(int MaxHp, float Speed, int Reward, int Damage, string Name);

        public static readonly Dictionary<int, MonsterDef> Defs = new()
        {
            { 0, new MonsterDef(100,  2.0f, 10,  10,  "普通兵") },
            { 1, new MonsterDef(300,  1.5f, 30,  25,  "精英兵") },
            { 2, new MonsterDef(800,  1.0f, 100, 100, "BOSS")   },
            { 3, new MonsterDef(250,  1.8f, 20,  15,  "重甲兵") },
            { 4, new MonsterDef(150,  3.5f, 25,  20,  "速度兵") },
            { 5, new MonsterDef(500,  1.2f, 60,  50,  "召唤师") },
            { 6, new MonsterDef(200,  3.0f, 35,  25,  "刺客")   },
            { 7, new MonsterDef(1200, 0.8f, 200, 150, "泰坦巨人") },
            { 8, new MonsterDef(600,  1.5f, 120, 80,  "暗影领主") },
        };

        public static MonsterDef Get(int type) =>
            Defs.TryGetValue(type, out var d) ? d : Defs[0];
    }

    // ── 岩浆区域 ─────────────────────────────────────────────────

    public class LavaZone
    {
        public float X { get; }
        public float Y { get; }
        public float Radius { get; } = 1.5f;
        public float Duration { get; private set; } = 8f;
        public int DamagePerTick { get; } = 8;
        public bool IsExpired => Duration <= 0;

        private float _tickAccum = 0f;

        public LavaZone(float x, float y) { X = x; Y = y; }

        public void Update(float dt, List<Monster> monsters)
        {
            Duration -= dt;
            _tickAccum += dt;

            if (_tickAccum < 0.5f) return;
            _tickAccum = 0f;

            foreach (var m in monsters)
            {
                if (m.IsDead) continue;
                float dx = m.X - X, dy = m.Y - Y;
                if (MathF.Sqrt(dx * dx + dy * dy) <= Radius)
                    m.TakeDamage(DamagePerTick);
            }
        }
    }

    // ── 塔 ───────────────────────────────────────────────────────

    public class Tower
    {
        public int GridX { get; }
        public int GridY { get; }
        public int Type { get; }
        public int OwnerId { get; }
        public int Level { get; private set; } = 1;
        public int Branch { get; private set; } = 0;

        private float _attackCooldown = 0f;
        private readonly Random _rng = new();

        public bool IsAmplifier =>
            Branch == 1 && TowerConfig.GetBranch(Type, 1)?.Effect == SpecialEffect.Amplify;

        public bool HasGoldBoost =>
            Branch == 2 && TowerConfig.GetBranch(Type, 2)?.Effect == SpecialEffect.GoldBoost;

        public float AmplifyRange =>
            IsAmplifier ? TowerConfig.GetBranch(Type, 1)!.Range : 0f;
        public int Hp { get; private set; }
        public int MaxHp { get; private set; }
        public bool IsDead => Hp <= 0;

        public Tower(int x, int y, int type, int ownerId)
        {
            GridX = x; GridY = y; Type = type; OwnerId = ownerId;
            MaxHp = TowerConfig.Get(type).Hp;
            Hp = MaxHp;
        }

        public void TakeDamage(int dmg)
        {
            Hp = Math.Max(0, Hp - dmg);
        }
        /*public Tower(int x, int y, int type, int ownerId)
        {
            GridX = x; GridY = y; Type = type; OwnerId = ownerId;
        }*/

        public TowerState ToState() => new()
        {
            gridX = GridX,
            gridY = GridY,
            type = Type,
            level = Level,
            ownerId = OwnerId,
            branch = Branch,
                hp = Hp,
                maxHp = MaxHp,  
        };

        public void Upgrade()
        {
            if (Level < 2) Level = 2;
        }

        public bool ChooseBranch(int branch)
        {
            if (Level != 2 || Branch != 0) return false;
            if (branch != 1 && branch != 2) return false;
            Branch = branch;
            Level = 3;
            return true;
        }

        private (int dmg, float range, float interval) GetEffectiveStats()
        {
            var def = TowerConfig.Get(Type);
            if (Branch == 0)
                return (def.Damage * Level, def.Range, def.AttackInterval);
            var b = TowerConfig.GetBranch(Type, Branch)!;
            return (b.Damage, b.Range, b.AttackInterval);
        }

        private SpecialEffect GetCurrentEffect()
        {
            if (Branch == 0) return SpecialEffect.None;
            return TowerConfig.GetBranch(Type, Branch)?.Effect ?? SpecialEffect.None;
        }

        public List<Monster> TryAttack(
            List<Monster> monsters,
            float deltaTime,
            List<LavaZone> lavaZones,
            int field = -1,
            float attackIntervalMult = 1f)
        {
            _attackCooldown -= deltaTime;
            var result = new List<Monster>();

            if (Type == 5) return result;
            if (_attackCooldown > 0) return result;

            var (dmg, range, interval) = GetEffectiveStats();
            interval *= attackIntervalMult;

            float tx = GridX + 0.5f, ty = GridY + 0.5f;

            var inRange = monsters
                .Where(m => !m.IsDead &&
                            (field < 0 || m.Field == field) &&
                            MathF.Sqrt(MathF.Pow(m.X - tx, 2) + MathF.Pow(m.Y - ty, 2)) <= range)
                .OrderBy(m => MathF.Sqrt(MathF.Pow(m.X - tx, 2) + MathF.Pow(m.Y - ty, 2)))
                .ToList();

            if (inRange.Count == 0) return result;

            _attackCooldown = interval;
            var effect = GetCurrentEffect();

            switch (effect)
            {
                case SpecialEffect.Snipe:
                    var snipeTarget = inRange[0];
                    int sniperDmg = _rng.NextDouble() < 0.5 ? dmg * 2 : dmg;
                    snipeTarget.TakeDamage(sniperDmg);
                    result.Add(snipeTarget);
                    break;

                case SpecialEffect.Multishot:
                case SpecialEffect.Cluster:
                    foreach (var m in inRange.Take(3))
                    { m.TakeDamage(dmg); result.Add(m); }
                    break;

                case SpecialEffect.ChainLightning:
                    int chainDmg = dmg;
                    foreach (var m in inRange.Take(5))
                    {
                        m.TakeDamage(chainDmg);
                        result.Add(m);
                        chainDmg = (int)(chainDmg * 0.7f);
                    }
                    break;

                case SpecialEffect.Freeze:
                    var freezeTarget = inRange[0];
                    freezeTarget.ApplyFreeze(3.0f);
                    freezeTarget.TakeDamage(dmg);
                    result.Add(freezeTarget);
                    break;

                case SpecialEffect.Poison:
                    foreach (var m in inRange.Take(5))
                    { m.ApplyPoison(dmg, 4.0f); result.Add(m); }
                    break;

                case SpecialEffect.Knockback:
                    var kbTarget = inRange[0];
                    kbTarget.TakeDamage(dmg);
                    kbTarget.ApplyKnockback(0.8f);
                    result.Add(kbTarget);
                    break;

                case SpecialEffect.Lava:
                    var lavaTarget = inRange[0];
                    lavaTarget.TakeDamage(dmg);
                    result.Add(lavaTarget);
                    lavaZones.Add(new LavaZone(lavaTarget.X, lavaTarget.Y));
                    break;

                default:
                    var target = inRange[0];
                    if (Type == 2 && Branch == 0)
                    {
                        float slowDur = Level switch { 2 => 3.0f, _ => 2.0f };
                        float slowStr = Level switch { 2 => 0.3f, _ => 0.4f };
                        target.ApplySlow(slowDur, slowStr);
                    }
                    target.TakeDamage(dmg);
                    result.Add(target);
                    break;
            }

            return result;
        }

        public int GetKillBonus(Monster m)
        {
            if (GetCurrentEffect() != SpecialEffect.SoulHarvest) return 0;
            return MonsterConfig.Get(m.Type).Reward / 2;
        }
    }

    // ── 怪物 ─────────────────────────────────────────────────────

    public class Monster
    {
        private static int _nextId = 1;
        public static void ResetIdCounter() => _nextId = 1;

        public int Id { get; }
        public int Type { get; }
        public float X { get; private set; }
        public float Y { get; private set; }
        public int Hp { get; private set; }
        public int MaxHp { get; }
        public bool IsDead => Hp <= 0;
        public bool Reached { get; private set; }
        public int Field { get; }

        private List<Vec2Int> _path;
        private int _pathIndex = 0;
        private float _slowTimer = 0f;
        private float _slowStrength = 1.0f;
        private float _freezeTimer = 0f;
        private float _poisonTimer = 0f;
        private int _poisonDmg = 0;
        private float _poisonTick = 0f;
        private float _knockbackTimer = 0f;
        private readonly MonsterConfig.MonsterDef _cfg;
        public bool IsBlocked { get; private set; } = false;
        private float _attackTowerCooldown = 0f;
        private const float ATTACK_TOWER_INTERVAL = 1.0f;
        private const float ATTACK_TOWER_RANGE = 1.5f;
        public Monster(int type, List<Vec2Int> path, int field = 0)
        {
            Id = _nextId++;
            Type = type;
            Field = field;
            _cfg = MonsterConfig.Get(type);
            MaxHp = _cfg.MaxHp;
            Hp = MaxHp;
            _path = path;

            if (path.Count > 0)
            {
                var rng = new Random(Id);
                float offX = (float)(rng.NextDouble() - 0.5) * 0.6f;
                float offY = (float)(rng.NextDouble() - 0.5) * 0.6f;
                X = path[0].x + 0.5f + offX;
                Y = path[0].y + 0.5f + offY;
            }
        }

        public void TakeDamage(int dmg)
        {
            if (_freezeTimer > 0) dmg *= 2;
            Hp = Math.Max(0, Hp - dmg);
        }

        public void ApplySlow(float duration, float strength = 0.4f)
        {
            if (duration > _slowTimer)
            { _slowTimer = duration; _slowStrength = strength; }
        }

        public void ApplyFreeze(float duration) =>
            _freezeTimer = Math.Max(_freezeTimer, duration);

        public void ApplyPoison(int dmgPerTick, float duration)
        {
            _poisonTimer = Math.Max(_poisonTimer, duration);
            _poisonDmg = Math.Max(_poisonDmg, dmgPerTick);
        }

        public void ApplyKnockback(float duration) =>
            _knockbackTimer = duration;
        private int _debugCount = 0;
        public void Update(float deltaTime)
        {
            if (IsDead || Reached) return;

            if (IsBlocked) return;

            // 毒素
            if (_poisonTimer > 0)
            {
                _poisonTimer -= deltaTime;
                _poisonTick += deltaTime;
                if (_poisonTick >= 0.5f)
                {
                    _poisonTick = 0f;
                    Hp = Math.Max(0, Hp - _poisonDmg);
                    if (IsDead) return;
                }
            }

            // 冰冻：停止移动
            if (_freezeTimer > 0)
            { _freezeTimer -= deltaTime; return; }

            // 击退：倒退
            if (_knockbackTimer > 0)
            {
                _knockbackTimer -= deltaTime;
                if (_pathIndex > 0)
                {
                    var prev = _path[_pathIndex];
                    float dx = prev.x + 0.5f - X;
                    float dy = prev.y + 0.5f - Y;
                    float d = MathF.Sqrt(dx * dx + dy * dy);
                    if (d > 0.01f)
                    { X -= dx / d * _cfg.Speed * deltaTime; Y -= dy / d * _cfg.Speed * deltaTime; }
                }
                return;
            }

            // 减速
            float effectiveDt = deltaTime;
            if (_slowTimer > 0)
            { _slowTimer -= deltaTime; effectiveDt *= _slowStrength; }
            else
            { _slowStrength = 1.0f; }

            // 移动
            if (_pathIndex >= _path.Count - 1)
            {
                Console.WriteLine($"[Monster] id={Id} 到达终点 pathIndex={_pathIndex} pathCount={_path.Count}");
                Reached = true;
                return;
            }

            var node = _path[_pathIndex + 1];
            float tx = node.x + 0.5f, ty = node.y + 0.5f;
            float ddx = tx - X, ddy = ty - Y;
            float dist = MathF.Sqrt(ddx * ddx + ddy * ddy);
            float move = _cfg.Speed * effectiveDt;

            if (move >= dist)
            {
                X = tx; Y = ty;
                _pathIndex++;
                Console.WriteLine($"[Monster] id={Id} pathIndex推进到{_pathIndex} 路径总长={_path.Count}");
            }
            else
            {
                X += ddx / dist * move;
                Y += ddy / dist * move;
            }
        }
        public Tower TryAttackTower(List<Tower> towers, float deltaTime)
        {
            if (!IsBlocked) return null;

            _attackTowerCooldown -= deltaTime;
            if (_attackTowerCooldown > 0) return null;

            // 找最近的塔
            Tower nearest = null;
            float minDist = float.MaxValue;
            foreach (var t in towers)
            {
                if (t.IsDead) continue;
                float dx = t.GridX + 0.5f - X;
                float dy = t.GridY + 0.5f - Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist <= ATTACK_TOWER_RANGE && dist < minDist)
                {
                    minDist = dist;
                    nearest = t;
                }
            }

            if (nearest == null) return null;

            _attackTowerCooldown = ATTACK_TOWER_INTERVAL;
            nearest.TakeDamage(_cfg.Damage);
            return nearest;
        }
        public void SetBlocked(bool blocked)
        {
            IsBlocked = blocked;
        }
        /* public void UpdatePath(List<Vec2Int> newPath)
         {
             _path = newPath;

             Console.WriteLine($"[Monster] id={Id} 路径更新 新路径长={newPath.Count} 当前位置=({X:F1},{Y:F1})");

         float minDist = float.MaxValue;
             int bestIndex = 0;

             for (int i = 0; i < newPath.Count - 1; i++)  // 注意：-1，不取最后一个节点
             {
                 float d = MathF.Sqrt(
                     MathF.Pow(newPath[i].x + 0.5f - X, 2) +
                     MathF.Pow(newPath[i].y + 0.5f - Y, 2));
                 if (d < minDist) { minDist = d; bestIndex = i; }
             }

             _pathIndex = bestIndex;
         }*/
        /* public void UpdatePath(List<Vec2Int> newPath)
         {
             _path = newPath;
             float minDist = float.MaxValue;
             int bestIndex = 0;

             for (int i = 0; i < newPath.Count - 1; i++)
             {
                 float nx = newPath[i].x + 0.5f;
                 float ny = newPath[i].y + 0.5f;
                 float d = MathF.Sqrt(MathF.Pow(nx - X, 2) + MathF.Pow(ny - Y, 2));

                 if (d < minDist)
                 {
                     minDist = d;
                     bestIndex = i;
                 }
             }

             // 如果找到的节点太近，往前推一个节点
             // 确保怪物不会往回走
             if (bestIndex > 0)
             {
                 float prevX = newPath[bestIndex - 1].x + 0.5f;
                 float prevY = newPath[bestIndex - 1].y + 0.5f;
                 float nextX = newPath[bestIndex + 1 < newPath.Count ? bestIndex + 1 : bestIndex].x + 0.5f;
                 float nextY = newPath[bestIndex + 1 < newPath.Count ? bestIndex + 1 : bestIndex].y + 0.5f;

                 // 计算怪物前进方向
                 float dirX = nextX - prevX;
                 float dirY = nextY - prevY;

                 // 计算怪物到当前节点的方向
                 float toNodeX = newPath[bestIndex].x + 0.5f - X;
                 float toNodeY = newPath[bestIndex].y + 0.5f - Y;

                 // 如果节点在怪物后方，往前推
                 float dot = dirX * toNodeX + dirY * toNodeY;
                 if (dot < 0 && bestIndex + 1 < newPath.Count - 1)
                     bestIndex++;
             }

             _pathIndex = bestIndex;
             Console.WriteLine($"[Monster] id={Id} 路径更新 新路径长={newPath.Count} pathIndex={_pathIndex} 位置=({X:F1},{Y:F1})");
         }*/
        public void UpdatePath(List<Vec2Int> newPath)
        {
            _path = newPath;
            float minDist = float.MaxValue;
            int bestIndex = 0;

            for (int i = 0; i < newPath.Count - 1; i++)  // 注意：-1，不取最后一个节点
            {
                float d = MathF.Sqrt(
                    MathF.Pow(newPath[i].x + 0.5f - X, 2) +
                    MathF.Pow(newPath[i].y + 0.5f - Y, 2));
                if (d < minDist) { minDist = d; bestIndex = i; }
            }

            _pathIndex = bestIndex;
        }
        public MonsterState ToState() => new()
         {
             id = Id,
             type = Type,
             x = X,
             y = Y,
             hp = Hp,
             maxHp = MaxHp,
             speed = _cfg.Speed,
             pathIndex = _pathIndex,
             field = Field,
             reward = _cfg.Reward,
         };
        
    }

    // ── 子弹 ─────────────────────────────────────────────────────

    public class Bullet
    {
        private static int _nextId = 1;
        public static void ResetIdCounter() => _nextId = 1;

        public int Id { get; }
        public float X { get; set; }
        public float Y { get; set; }
        public int TargetId { get; }
        public int TowerType { get; }
        public int Branch { get; }

        public Bullet(float x, float y, int targetId, int towerType = 0, int branch = 0)
        {
            Id = _nextId++;
            X = x; Y = y;
            TargetId = targetId;
            TowerType = towerType;
            Branch = branch;
        }

        public BulletState ToState() => new()
        {
            id = Id,
            x = X,
            y = Y,
            targetId = TargetId,
            towerType = TowerType,
            branch = Branch,
        };
    }
}