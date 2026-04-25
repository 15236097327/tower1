using System.Collections.Generic;
using UnityEngine;
using MazeTD.Shared;

namespace MazeTD.Client.Game
{
    /// <summary>
    /// 3D子弹渲染器。
    ///
    /// 设计：
    /// - 服务端每帧广播子弹列表（生存1帧后清除）
    /// - 客户端收到后在塔→怪物之间插值绘制子弹飞行动画
    /// - 3种塔类型对应3种视觉效果：
    ///   0=箭塔：细长圆柱（箭矢）
    ///   1=炮塔：球体（炮弹）+抛物线
    ///   2=减速塔：冰晶（蓝色菱形）+拖尾
    /// </summary>
    public class BulletRenderer3D : MonoBehaviour
    {
        [Header("子弹Prefabs - 箭塔")]
        public GameObject BulletPrefab_Arrow;          // 箭塔基础
        public GameObject BulletPrefab_Arrow_Sniper;   // 狙击手塔
        public GameObject BulletPrefab_Arrow_Multishot;// 速射塔

        [Header("子弹Prefabs - 炮塔")]
        public GameObject BulletPrefab_Cannon;         // 炮塔基础
        public GameObject BulletPrefab_Cannon_Heavy;   // 重炮塔
        public GameObject BulletPrefab_Cannon_Cluster; // 集束炮塔

        [Header("子弹Prefabs - 减速塔")]
        public GameObject BulletPrefab_Ice;            // 减速塔基础
        public GameObject BulletPrefab_Ice_Freeze;     // 冰冻塔
        public GameObject BulletPrefab_Ice_Poison;     // 毒雾塔

        [Header("子弹Prefabs - 魔法塔")]
        public GameObject BulletPrefab_Magic;          // 魔法塔基础
        public GameObject BulletPrefab_Magic_Lightning;// 闪电塔
        public GameObject BulletPrefab_Magic_Soul;     // 灵魂收割塔

        [Header("子弹Prefabs - 火焰塔")]
        public GameObject BulletPrefab_Fire;           // 火焰塔基础
        public GameObject BulletPrefab_Fire_Lava;      // 熔岩塔
        public GameObject BulletPrefab_Fire_Phoenix;   // 凤凰塔

        [Header("子弹Prefabs - 支援塔")]
        public GameObject BulletPrefab_Support_Gold;   // 金币塔击杀特效

        [Header("参数")]
        public float BulletSpeed = 15f;
        public float BulletLifetime = 0.5f;
        public float ArcHeight = 1.5f;
        [Header("命中特效")]
        public GameObject HitEffectPrefab_Arrow;
        public GameObject HitEffectPrefab_Fire;
        public GameObject HitEffectPrefab_Explosion;
        private readonly Dictionary<int, BulletView3D> _activeBullets = new();
        private readonly Queue<int> _toRemove = new();
        private GridRenderer3D _grid;
        private MonsterController3D _monsters;

        private void Start()
        {
            _grid = FindObjectOfType<GridRenderer3D>();
            _monsters = FindObjectOfType<MonsterController3D>();
        }

        /// <summary>
        /// 从StateSync接收新子弹数据
        /// </summary>
        public void ApplyBullets(List<BulletState> bullets)
        {
            foreach (var b in bullets)
            {
                if (_activeBullets.ContainsKey(b.id)) continue;
                int towerX = Mathf.RoundToInt(b.x - 0.5f);
                int towerY = Mathf.RoundToInt(b.y - 0.5f);
                _grid?.TriggerTowerAttack(towerX, towerY, b.towerType);
                // 子弹起点（塔位置）
                Vector3 startPos = _grid != null
                    ? _grid.ServerToWorld(b.x, b.y) + Vector3.up * 0.6f
                    : new Vector3(b.x, 0.6f, b.y);

                var view = SpawnBullet(b, startPos);
                if (view != null)
                    _activeBullets[b.id] = view;
            }
        }

        private void Update()
        {
            _toRemove.Clear();

            foreach (var kv in _activeBullets)
            {
                var view = kv.Value;
                view.Elapsed += Time.deltaTime;

                if (view.Elapsed >= BulletLifetime)
                {
                    _toRemove.Enqueue(kv.Key);
                    continue;
                }

                // 插值飞行
                float t = view.Elapsed / BulletLifetime;
                Vector3 pos = Vector3.Lerp(view.StartPos, view.TargetPos, t);

                // 炮弹抛物线
                if (view.TowerType == 1)
                {
                    float arc = ArcHeight * 4f * t * (1f - t);
                    pos.y += arc;
                }

                // 面朝飞行方向
                Vector3 dir = (view.TargetPos - view.StartPos).normalized;
                if (dir.sqrMagnitude > 0.001f)
                    view.transform.rotation = Quaternion.LookRotation(dir);

                view.transform.position = pos;

                // 拖尾粒子
                if (view.Trail != null)
                    view.Trail.transform.position = pos;
            }

            while (_toRemove.Count > 0)
            {
                int id = _toRemove.Dequeue();
                if (_activeBullets.TryGetValue(id, out var v))
                {
                    // 命中特效
                    SpawnImpactEffect(v.TargetPos, v.TowerType, v.BranchType);
                    Destroy(v.gameObject);
                    _activeBullets.Remove(id);
                }
            }
        }

        private BulletView3D SpawnBullet(BulletState b, Vector3 startPos)
        {
            // 目标位置：用targetId找怪物当前位置
            // 简单方案：用子弹自身坐标（服务端已计算目标方向）
            // 由于子弹存在1帧，目标位置需要估算
            Vector3 targetPos = startPos + Vector3.forward * 2f; // 默认

            // 尝试查找目标怪物位置（通过遍历MonsterView3D）
            var monsterViews = FindObjectsOfType<MonsterView3D>();
            foreach (var mv in monsterViews)
            {
                if (mv.gameObject.name == $"Monster_{b.targetId}")
                {
                    targetPos = mv.TargetPosition;
                    break;
                }
            }
            var prefab = GetBulletPrefab(b.towerType, b.branch);



            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, startPos, Quaternion.identity, transform);
               // go.transform.localScale = Vector3.one * 0.15f;
                go.transform.localScale = b.towerType switch
                {
                    1 => Vector3.one * 0.25f,  // 炮弹稍大
                    2 => Vector3.one * 0.12f,  // 冰晶小
                    _ => Vector3.one * 0.15f,  // 其他
                };
            }
            else
            {
                go = CreateDefaultBullet(b.towerType, b.branch);
                go.transform.SetParent(transform);
                go.transform.position = startPos;
            }

            go.name = $"Bullet_{b.id}";

            var view = go.AddComponent<BulletView3D>();
            view.StartPos = startPos;
            view.TargetPos = targetPos;
            view.TowerType = b.towerType;
            view.BranchType = b.branch;
            view.Elapsed = 0f;

            return view;
        }
        private GameObject GetBulletPrefab(int towerType, int branch)
        {
            return towerType switch
            {
                0 => branch switch
                {
                    1 => BulletPrefab_Arrow_Sniper,
                    2 => BulletPrefab_Arrow_Multishot,
                    _ => BulletPrefab_Arrow,
                },
                1 => branch switch
                {
                    1 => BulletPrefab_Cannon_Heavy,
                    2 => BulletPrefab_Cannon_Cluster,
                    _ => BulletPrefab_Cannon,
                },
                2 => branch switch
                {
                    1 => BulletPrefab_Ice_Freeze,
                    2 => BulletPrefab_Ice_Poison,
                    _ => BulletPrefab_Ice,
                },
                3 => branch switch
                {
                    1 => BulletPrefab_Magic_Lightning,
                    2 => BulletPrefab_Magic_Soul,
                    _ => BulletPrefab_Magic,
                },
                4 => branch switch
                {
                    1 => BulletPrefab_Fire_Lava,
                    2 => BulletPrefab_Fire_Phoenix,
                    _ => BulletPrefab_Fire,
                },
                5 => BulletPrefab_Support_Gold,
                _ => BulletPrefab_Arrow,
            };
        }
        private void SpawnImpactEffect(Vector3 pos, int towerType, int branch)
        {
            GameObject prefab = towerType switch
            {
                1 => HitEffectPrefab_Explosion,  // 炮塔
                4 => HitEffectPrefab_Fire,       // 火焰塔
                _ => HitEffectPrefab_Arrow,      // 其他
            };

            if (prefab != null)
            {
                var fx = Instantiate(prefab, pos, Quaternion.identity);
                Destroy(fx, 1f);
            }
            
        }

        private GameObject CreateDefaultBullet(int towerType, int branch)
        {
            GameObject go;
            switch (towerType)
            {
                case 1: // 炮弹
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.transform.localScale = Vector3.one * (branch == 1 ? 0.3f : 0.2f);
                    go.GetComponent<MeshRenderer>().material.color =
                        branch == 1 ? new Color(0.5f, 0.1f, 0.1f) : new Color(0.3f, 0.3f, 0.3f);
                    break;

                case 2: // 冰晶/毒雾
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.transform.localScale = Vector3.one * 0.12f;
                    go.GetComponent<MeshRenderer>().material.color =
                        branch == 2 ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.4f, 0.7f, 1f);
                    go.AddComponent<SpinEffect>();
                    break;

                case 3: // 魔法
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.transform.localScale = Vector3.one * 0.18f;
                    go.GetComponent<MeshRenderer>().material.color =
                        branch == 1 ? new Color(0.3f, 0.5f, 1f) : new Color(0.6f, 0.2f, 0.9f);
                    break;

                case 4: // 火焰
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.transform.localScale = Vector3.one * 0.15f;
                    go.GetComponent<MeshRenderer>().material.color =
                        new Color(1f, 0.4f, 0.1f);
                    break;

                default: // 箭矢
                    go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    go.transform.localScale = new Vector3(0.04f, 0.15f, 0.04f);
                    go.GetComponent<MeshRenderer>().material.color =
                        branch == 1 ? new Color(1f, 0.8f, 0.1f) : new Color(0.8f, 0.7f, 0.3f);
                    break;
            }

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            return go;
        }
    }

    public class BulletView3D : MonoBehaviour
    {
        public Vector3 StartPos;
        public Vector3 TargetPos;
        public int TowerType;
        public int BranchType;
        public float Elapsed;
        
        public GameObject Trail;
    }

    /// <summary>冰晶自旋效果</summary>
    public class SpinEffect : MonoBehaviour
    {
        public float Speed = 360f;
        private void Update()
        {
            transform.Rotate(Vector3.up, Speed * Time.deltaTime);
        }
    }
}
