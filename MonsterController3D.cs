using MazeTD.Shared;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MazeTD.Client.Game
{
    /// <summary>
    /// 3D怪物控制器。
    ///
    /// 改造要点：
    /// - 怪物在 XZ 平面移动（Y=地面高度）
    /// - 面朝移动方向旋转（Y轴旋转）
    /// - 世界空间3D血条（Billboard面向摄像机）
    /// - 3D死亡/受击特效
    /// - 减速视觉反馈（材质变色/粒子）
    /// </summary>
    public class MonsterController3D : MonoBehaviour
    {
        [Header("怪物Prefabs")]
        public GameObject MonsterPrefab_Normal;      // 普通兵
        public GameObject MonsterPrefab_Elite;       // 精英兵
        public GameObject MonsterPrefab_Boss;        // BOSS
        public GameObject MonsterPrefab_HeavyArmor; // 重甲兵
        public GameObject MonsterPrefab_Swift;       // 速度兵
        public GameObject MonsterPrefab_Summoner;    // 召唤师
        public GameObject MonsterPrefab_Assassin;    // 刺客
        public GameObject MonsterPrefab_Titan;       // 泰坦巨人
        public GameObject MonsterPrefab_ShadowLord; // 暗影领主

        [Header("特效")]
        public GameObject DeathEffectPrefab_Normal;  // 普通怪物死亡
        public GameObject DeathEffectPrefab_Boss;    // Boss死亡
        public GameObject HitEffectPrefab_Arrow;     // 箭矢命中
        public GameObject HitEffectPrefab_Fire;      // 火焰命中
        public GameObject HitEffectPrefab_Explosion; // 爆炸命中
        public GameObject SlowEffectPrefab;          // 减速特效
        public GameObject LavaEffectPrefab;          // 岩浆区域

        [Header("血条")]
        public GameObject HpBarPrefab;
        public float HpBarHeight = 1.2f;

        [Header("插值")]
        public float LerpSpeed = 30f;
        public float RotateSpeed = 720f;

        [Header("地面高度")]
        public float GroundY = 0.25f;
        [Header("金币特效")]
        public GameObject GoldEffectPrefab;
        private readonly Dictionary<int, MonsterView3D> _views = new();
        private GridRenderer3D _grid;

        private void Start()
        {
            _grid = FindObjectOfType<GridRenderer3D>();
        }
        private void SpawnGoldEffect(Vector3 pos, int reward)
        {
            if (reward <= 0 || GoldEffectPrefab == null) return;

            var go = Instantiate(GoldEffectPrefab, pos + Vector3.up * 0.5f, Quaternion.identity);
            go.transform.localScale = Vector3.one * 0.5f;
            Destroy(go, 1.5f);
        }
        // ── 应用服务端状态 ────────────────────────────────────────

        public void ApplyState(List<MonsterState> states)
        {
            Debug.Log($"[Monster] ApplyState 收到 {states.Count} 个 views数={_views.Count}");
            var activeIds = new HashSet<int>();

            foreach (var s in states)
            {
                activeIds.Add(s.id);

                Vector3 worldPos = ServerToWorld(s.x, s.y);

                if (!_views.TryGetValue(s.id, out var view))
                {
                    view = SpawnMonster(s, worldPos);
                    _views[s.id] = view;
                }

                view.TargetPosition = worldPos;
                view.LastUpdateTime = Time.time;
                view.UpdateHpBar(s.hp, s.maxHp);
                view.UpdateSlowEffect(s.speed < 1.8f);
                view.Reward = s.reward;

                if (s.hp < view.LastHp && view.LastHp > 0)
                    SpawnHitEffect(view.transform.position);
                view.LastHp = s.hp;
            }

            // 移除不在activeIds里的怪物
            var toRemove = _views.Keys.Where(id => !activeIds.Contains(id)).ToList();
            foreach (var id in toRemove)
            {
                if (_views.TryGetValue(id, out var v))
                {
                    Debug.Log($"[Monster] 强制移除 id={id}");
                    SpawnDeathEffect(v.transform.position, v.MonsterType);
                    SpawnGoldEffect(v.transform.position, v.Reward);
                    DestroyImmediate(v.gameObject);
                    _views.Remove(id);
                }
            }

            // 强制清空：收到空列表时清掉所有残留怪物
            if (states.Count == 0 && _views.Count > 0)
            {
                Debug.Log($"[Monster] 收到空列表，强制清空{_views.Count}个残留怪物");
                var remaining = _views.Keys.ToList();
                foreach (var id in remaining)
                {
                    if (_views.TryGetValue(id, out var v))
                    {
                        SpawnDeathEffect(v.transform.position, v.MonsterType);
                        DestroyImmediate(v.gameObject);
                        _views.Remove(id);
                    }
                }
            }
        }

        private void Update()
        {
            var toRemove = new List<int>();

            foreach (var kv in _views)
            {
                var view = kv.Value;

                // 超时检测，超过1秒没收到服务端更新就移除
                /*if (Time.time - view.LastUpdateTime > 1.0f)
                {
                    toRemove.Add(kv.Key);
                    continue;
                }*/

                // 位置插值
                view.transform.position = Vector3.Lerp(
                    view.transform.position, view.TargetPosition,
                    Time.deltaTime * LerpSpeed);

                Vector3 moveDir = view.TargetPosition - view.transform.position;
                moveDir.y = 0;
                bool isMoving = moveDir.sqrMagnitude > 0.001f;
                view.SetMoving(isMoving);

                if (moveDir.sqrMagnitude > 0.0001f)
                {
                    float targetAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
                    Quaternion targetRot = Quaternion.Euler(0f, targetAngle, 0f);
                    view.transform.rotation = Quaternion.RotateTowards(
                        view.transform.rotation, targetRot,
                        RotateSpeed * Time.deltaTime);
                }

                view.UpdateHpBarOrientation();
            }

            // 移除超时怪物
            foreach (var id in toRemove)
            {
                if (_views.TryGetValue(id, out var v))
                {
                    SpawnDeathEffect(v.transform.position, v.MonsterType);
                    Destroy(v.gameObject);
                    _views.Remove(id);
                }
            }
        }

        // ── 生成怪物 ─────────────────────────────────────────────

        private MonsterView3D SpawnMonster(MonsterState s, Vector3 pos)
        {
            // 创建一个父容器，MonsterController3D 控制这个父容器的旋转
            var container = new GameObject($"Monster_{s.id}");
            container.transform.SetParent(transform);
            container.transform.position = pos;

            var prefab = GetMonsterPrefab(s.type);
            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, container.transform);
                // 在这里修正模型朝向，让模型正面对准 Z 轴正方向
                go.transform.localRotation = Quaternion.Euler(-180f, -90f, 180f);
                go.transform.localPosition = Vector3.zero;
            }
            else
            {
                go = CreateDefaultMonster3D(s.type);
                go.transform.SetParent(container.transform);
                go.transform.localPosition = Vector3.zero;
            }

            var view = container.AddComponent<MonsterView3D>();
            view.MonsterType = s.type;
            view.Init(s.maxHp, pos, HpBarPrefab, HpBarHeight, SlowEffectPrefab);
            return view;
        }
        private GameObject GetMonsterPrefab(int type)
        {
            return type switch
            {
                0 => MonsterPrefab_Normal,
                1 => MonsterPrefab_Elite,
                2 => MonsterPrefab_Boss,
                3 => MonsterPrefab_HeavyArmor,
                4 => MonsterPrefab_Swift,
                5 => MonsterPrefab_Summoner,
                6 => MonsterPrefab_Assassin,
                7 => MonsterPrefab_Titan,
                8 => MonsterPrefab_ShadowLord,
                _ => MonsterPrefab_Normal,
            };
        }
        // ── 坐标转换 ─────────────────────────────────────────────

        private Vector3 ServerToWorld(float sx, float sy)
        {
            if (_grid != null)
                return _grid.ServerToWorld(sx, sy) + Vector3.up * GroundY;
            // 回退：直接映射
            return new Vector3(sx, GroundY, sy);
        }

        // ── 特效 ─────────────────────────────────────────────────

        private void SpawnDeathEffect(Vector3 pos, int monsterType)
        {
            GameObject prefab = monsterType == 2 || monsterType == 7 || monsterType == 8
                ? DeathEffectPrefab_Boss    // Boss、泰坦、暗影用大特效
                : DeathEffectPrefab_Normal; // 其他用普通特效

            if (prefab != null)
            {
                var fx = Instantiate(prefab, pos, Quaternion.identity);
                Destroy(fx, 2f);
            }
            else
            {
                SpawnDefaultDeathParticle(pos);
            }
        }

        private void SpawnHitEffect(Vector3 pos)
        {
            if (HitEffectPrefab_Arrow != null)
            {
                var fx = Instantiate(HitEffectPrefab_Arrow, pos, Quaternion.identity);
                Destroy(fx, 0.5f);
            }
        }

        private void SpawnDefaultDeathParticle(Vector3 pos)
        {
            var go = new GameObject("DeathFX");
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 0.6f;
            main.startSpeed = 3f;
            main.startSize = 0.15f;
            main.startColor = new Color(1f, 0.3f, 0.1f);
            main.maxParticles = 20;
            //main.duration = 0.2f;
            main.loop = false;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 15) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;

            ps.Play();
            Destroy(go, 1.5f);
        }

        // ── 默认3D怪物 ──────────────────────────────────────────

        private GameObject CreateDefaultMonster3D(int type)
        {
            var go = new GameObject($"DefaultMonster3D_{type}");

            float bodyScale = type switch
            {
                2 => 0.45f,   // BOSS最大
                1 => 0.35f,   // 精英兵中等
                3 => 0.38f,   // 重甲兵偏大
                4 => 0.22f,   // 速度兵最小
                5 => 0.32f,   // 召唤师中等
                _ => 0.25f,   // 普通兵基础
            };

            // 身体（Capsule）
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(go.transform);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(bodyScale, bodyScale * 0.8f, bodyScale);

            // 移除Collider
            var col = body.GetComponent<CapsuleCollider>();
            if (col != null) Destroy(col);

            // 眼睛（两个小球）
            for (int i = -1; i <= 1; i += 2)
            {
                var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                eye.name = "Eye";
                eye.transform.SetParent(go.transform);
                eye.transform.localPosition = new Vector3(
                    i * bodyScale * 0.3f,
                    bodyScale * 0.3f,
                    bodyScale * 0.45f);
                eye.transform.localScale = Vector3.one * bodyScale * 0.25f;
                eye.GetComponent<MeshRenderer>().material.color = Color.white;
                var eyeCol = eye.GetComponent<SphereCollider>();
                if (eyeCol != null) Destroy(eyeCol);
            }

            // 颜色
            Color c = type switch
            {
                1 => new Color(0.9f, 0.5f, 0.1f),   // 精英-橙
                2 => new Color(0.6f, 0.1f, 0.7f),   // BOSS-紫
                3 => new Color(0.5f, 0.5f, 0.5f),   // 重甲兵-灰
                4 => new Color(0.9f, 0.9f, 0.1f),   // 速度兵-黄
                5 => new Color(0.1f, 0.7f, 0.7f),   // 召唤师-青
                _ => new Color(0.3f, 0.7f, 0.3f),   // 普通-绿
            };
            body.GetComponent<MeshRenderer>().material.color = c;

            return go;
        }
    }

    /// <summary>
    /// 3D怪物视图组件（挂在每个怪物GameObject上）
    /// </summary>
    public class MonsterView3D : MonoBehaviour
    {
        public Vector3 TargetPosition;
        public int LastHp;

        private Transform _hpBarRoot;
        private Transform _hpFill;
        private Transform _hpBg;
        private float _hpBarHeight;
        private GameObject _slowFxInstance;
        private bool _showingSlow;
        public int MonsterType;
        private Animator _animator;
        public float LastUpdateTime;  // 最后一次收到服务端更新的时间
        public void Init(int maxHp, Vector3 startPos, GameObject hpBarPrefab, float hpHeight, GameObject slowFxPrefab)
        {
            LastHp = maxHp;
            TargetPosition = startPos;
            _hpBarHeight = hpHeight;
            _animator = GetComponentInChildren<Animator>();
            Debug.Log($"[Monster] Animator={_animator}");
            if (_animator != null)
            {
                Debug.Log($"[Monster] 挂载物体={_animator.gameObject.name}");
                foreach (var clip in _animator.runtimeAnimatorController.animationClips)
                    Debug.Log($"[Monster] 动画片段={clip.name}");
                Debug.Log($"[Monster] 当前状态={_animator.GetCurrentAnimatorStateInfo(0).IsName("Walk")}");
            }

            if (hpBarPrefab != null)
            {
                var hpBar = Instantiate(hpBarPrefab, transform);
                hpBar.transform.localPosition = Vector3.up * hpHeight;
                _hpBarRoot = hpBar.transform;
                _hpFill = hpBar.transform.Find("Fill");
                _hpBg = hpBar.transform.Find("Background");
            }
            else
            {
                CreateDefaultHpBar3D();
            }
        }

        // 字段
        private TextMesh _hpText;
        private int _currentHp;
        private int _currentMaxHp;
        internal int Reward;
        public void SetMoving(bool isMoving)
        {
            if (_animator == null) return;
            _animator.SetBool("IsMoving", isMoving);
        }
        public void UpdateHpBar(int hp, int maxHp)
        {
            _currentHp = hp;
            _currentMaxHp = maxHp;

            if (_hpFill == null || maxHp == 0) return;
            float ratio = (float)hp / maxHp;
            _hpFill.localScale = new Vector3(ratio * 0.8f, 0.06f, 1f);
            _hpFill.localPosition = new Vector3(-(1f - ratio) * 0.4f, 0f, 0f);

            // 颜色
            var mr = _hpFill.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                // 用 sharedMaterial 避免每帧创建新材质
                mr.material.color = ratio > 0.5f
                    ? Color.Lerp(Color.yellow, Color.green, (ratio - 0.5f) * 2f)
                    : Color.Lerp(Color.red, Color.yellow, ratio * 2f);
            }

            // 文字
            if (_hpText != null)
                _hpText.text = $"{hp}/{maxHp}";
        }

        public void UpdateHpBarOrientation()
        {
            if (_hpBarRoot == null || Camera.main == null) return;
            _hpBarRoot.rotation = Camera.main.transform.rotation;
        }

        public void UpdateSlowEffect(bool isSlowed)
        {
            if (isSlowed == _showingSlow) return;
            _showingSlow = isSlowed;

            if (isSlowed)
            {
                // 简单蓝色发光表示减速
                foreach (var mr in GetComponentsInChildren<MeshRenderer>())
                {
                    if (mr.gameObject.name == "Body")
                    {
                        mr.material.SetColor("_EmissionColor",
                            new Color(0.1f, 0.3f, 0.8f) * 0.5f);
                        mr.material.EnableKeyword("_EMISSION");
                    }
                }
            }
            else
            {
                foreach (var mr in GetComponentsInChildren<MeshRenderer>())
                {
                    if (mr.gameObject.name == "Body")
                    {
                        mr.material.SetColor("_EmissionColor", Color.black);
                        mr.material.DisableKeyword("_EMISSION");
                    }
                }
            }
        }

        private void CreateDefaultHpBar3D()
        {
            _hpBarRoot = new GameObject("HpBar3D").transform;
            _hpBarRoot.SetParent(transform);
            _hpBarRoot.localPosition = Vector3.up * _hpBarHeight;

            // 背景
            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Background";
            bg.transform.SetParent(_hpBarRoot);
            bg.transform.localPosition = Vector3.zero;
            bg.transform.localScale = new Vector3(0.8f, 0.06f, 1f);
            var bgMat = new Material(Shader.Find("Sprites/Default"));
            bgMat.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
            bg.GetComponent<MeshRenderer>().material = bgMat;
            _hpBg = bg.transform;
            var bgCol = bg.GetComponent<MeshCollider>();
            if (bgCol != null) Destroy(bgCol);

            // 填充
            var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fill.name = "Fill";
            fill.transform.SetParent(_hpBarRoot);
            fill.transform.localPosition = Vector3.forward * -0.001f;
            fill.transform.localScale = new Vector3(0.8f, 0.06f, 1f);
            var fillMat = new Material(Shader.Find("Sprites/Default"));
            fillMat.color = Color.green;
            fill.GetComponent<MeshRenderer>().material = fillMat;
            _hpFill = fill.transform;
            var fillCol = fill.GetComponent<MeshCollider>();
            if (fillCol != null) Destroy(fillCol);

            // 血量文字
            var textGo = new GameObject("HpText");
            textGo.transform.SetParent(_hpBarRoot);
            textGo.transform.localPosition = new Vector3(0f, 0.06f, -0.001f);
            textGo.transform.localScale = Vector3.one * 0.12f;
            _hpText = textGo.AddComponent<TextMesh>();
            _hpText.fontSize = 24;
            _hpText.characterSize = 0.1f;
            _hpText.anchor = TextAnchor.MiddleCenter;
            _hpText.alignment = TextAlignment.Center;
            _hpText.color = Color.white;
            _hpText.text = "100/100";
        }
    }
}
