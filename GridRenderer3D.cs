using System.Collections.Generic;
using UnityEngine;
using MazeTD.Shared;

namespace MazeTD.Client.Game
{
    /// <summary>
    /// 3D地图渲染器。
    /// 
    /// 核心改造：
    /// - 2D SpriteRenderer → 3D Plane/Cube 在 XZ 平面
    /// - Y轴为高度轴（塔、怪物都在Y>0的位置）
    /// - 使用Material替代Color实现写实效果
    /// - 支持塔模型实例化（按类型+等级）
    /// - 攻击范围圈使用Projector或LineRenderer
    /// </summary>
    public class GridRenderer3D : MonoBehaviour
    {

        [Header("塔Prefab - 箭塔")]
        public GameObject TowerPrefab_Arrow_Lv1;
        public GameObject TowerPrefab_Arrow_Lv2;
        public GameObject TowerPrefab_Arrow_Sniper;
        public GameObject TowerPrefab_Arrow_Multishot;

        [Header("塔Prefab - 炮塔")]
        public GameObject TowerPrefab_Cannon_Lv1;
        public GameObject TowerPrefab_Cannon_Lv2;
        public GameObject TowerPrefab_Cannon_Heavy;
        public GameObject TowerPrefab_Cannon_Cluster;

        [Header("塔Prefab - 减速塔")]
        public GameObject TowerPrefab_Ice_Lv1;
        public GameObject TowerPrefab_Ice_Lv2;
        public GameObject TowerPrefab_Ice_Freeze;
        public GameObject TowerPrefab_Ice_Poison;

        [Header("塔Prefab - 魔法塔")]
        public GameObject TowerPrefab_Magic_Lv1;
        public GameObject TowerPrefab_Magic_Lv2;
        public GameObject TowerPrefab_Magic_Lightning;
        public GameObject TowerPrefab_Magic_SoulHarvest;

        [Header("塔Prefab - 火焰塔")]
        public GameObject TowerPrefab_Fire_Lv1;
        public GameObject TowerPrefab_Fire_Lv2;
        public GameObject TowerPrefab_Fire_Lava;
        public GameObject TowerPrefab_Fire_Phoenix;

        [Header("塔Prefab - 支援塔")]
        public GameObject TowerPrefab_Support_Lv1;
        public GameObject TowerPrefab_Support_Lv2;
        public GameObject TowerPrefab_Support_Amplify;
        public GameObject TowerPrefab_Support_Gold;

        [Header("怪物Prefab")]
        public GameObject MonsterPrefab_Normal;
        public GameObject MonsterPrefab_Elite;
        public GameObject MonsterPrefab_Boss;
        public GameObject MonsterPrefab_HeavyArmor;
        public GameObject MonsterPrefab_Swift;
        public GameObject MonsterPrefab_Summoner;
        public GameObject MonsterPrefab_Assassin;
        public GameObject MonsterPrefab_Titan;
        public GameObject MonsterPrefab_ShadowLord;

      


        [Header("地面材质")]
        public Material MatEmpty;       // 空地（草地/石板）
        public Material MatBlocked;     // 障碍/已建塔地基
        public Material MatSpawn;       // 出生点（发光红色）
        public Material MatBase;        // 基地（发光蓝色）

        [Header("路径材质")]
        public Material MatPath0;       // 路径0高亮
        public Material MatPath1;       // 路径1高亮
        public Material MatPath2;       // 路径2
        public Material MatPath3;       // 路径3

        [Header("基地")]
        public GameObject BasePrefab;
        private GameObject _baseModel;
        [Header("建塔预览")]
        public Material MatPreviewOk;    // 半透明绿
        public Material MatPreviewBad;   // 半透明红

        [Header("格子尺寸")]
        public float CellSize = 0.7f;
        public float CellGap  = 0.02f;  // 格子间隙

        [Header("范围圈")]
        public GameObject RangeCirclePrefab;  // 带LineRenderer的圆环

        // ── 内部状态 ─────────────────────────────────────────────
        private int _mapW, _mapH;
        private GameObject[,] _cellObjects;
        private MeshRenderer[,] _cellRenderers;
        private int[,] _gridData;

        // 路径覆盖层（每条路径一个列表）
        private readonly Dictionary<int, List<GameObject>> _pathOverlays = new();

        // 塔模型实例
        private readonly Dictionary<(int, int), GameObject> _towerModels = new();
        private readonly Dictionary<(int, int), GameObject> _supportRangeCircles = new();

        // 在类字段里加
        private readonly Dictionary<(int, int), float> _towerAttackTimers = new();
        private readonly Dictionary<(int, int), Vector3> _towerOriginalPos = new();
        // 建塔预览
        private GameObject _previewObj;

        // 范围圈
        private GameObject _rangeCircle;

        // ── 初始化 ───────────────────────────────────────────────

        public void InitGrid(S2C_GameStartPayload data)
        {
            Debug.Log($"[GridRenderer3D] InitGrid {data.mapWidth}x{data.mapHeight}");
            _mapW = data.mapWidth;
            _mapH = data.mapHeight;
            _cellObjects   = new GameObject[_mapW, _mapH];
            _cellRenderers = new MeshRenderer[_mapW, _mapH];
            _gridData      = new int[_mapW, _mapH];

            // 创建父容器
            var gridParent = new GameObject("GridCells");
            gridParent.transform.SetParent(transform);

            for (int x = 0; x < _mapW; x++)
            {
                for (int y = 0; y < _mapH; y++)
                {
                    int cellType = 0;
                    if (data.spawnPos != null && data.spawnPos.x == x && data.spawnPos.y == y)
                        cellType = 2;
                    else if (data.basePos != null && data.basePos.x == x && data.basePos.y == y)
                        cellType = 3;

                    _gridData[x, y] = cellType;
                    SpawnCell(x, y, cellType, gridParent.transform);
                }
            }
            CreateBaseHpBar(data.basePos.x, data.basePos.y);
            SpawnBaseModel(data.basePos.x, data.basePos.y);
            // 初始路径
            DrawPath(new List<Vec2Int>(data.initPath), 0);
        }
        public void TriggerTowerAttack(int gridX, int gridY, int towerType)
        {
            var key = (gridX, gridY);
            if (!_towerModels.ContainsKey(key)) return;
            _towerAttackTimers[key] = 0.3f; // 动画持续0.3秒

            if (!_towerOriginalPos.ContainsKey(key))
                _towerOriginalPos[key] = _towerModels[key].transform.localPosition;
        }
        private void SpawnBaseModel(int x, int y)
        {
            if (_baseModel != null) Destroy(_baseModel);

            Vector3 baseWorld = GridToWorld(x, y) + Vector3.up * 0.1f;

            if (BasePrefab != null)
            {
                _baseModel = Instantiate(BasePrefab, baseWorld, Quaternion.identity, transform);
                _baseModel.name = "Base";
                // 调整缩放，基地模型稍大一点
                _baseModel.transform.localScale = Vector3.one * 1.2f;
            }
            else
            {
                // 没有 Prefab 用默认蓝色方块占位
                _baseModel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _baseModel.name = "Base";
                _baseModel.transform.SetParent(transform);
                _baseModel.transform.position = baseWorld;
                _baseModel.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
                _baseModel.GetComponent<MeshRenderer>().material.color = new Color(0.15f, 0.4f, 0.9f);
                var col = _baseModel.GetComponent<BoxCollider>();
                if (col != null) Destroy(col);
            }
        }
        private void Update()
        {
            // 基地血条面向相机
            if (_baseHpBar != null && Camera.main != null)
                _baseHpBar.transform.rotation = Camera.main.transform.rotation;

            // 塔等级文字面向相机
            if (Camera.main == null) return;
            foreach (var kv in _towerModels)
            {
                var levelText = kv.Value.transform.Find("LevelText");
                if (levelText != null)
                    levelText.rotation = Camera.main.transform.rotation;
            }
            var keys = new List<(int, int)>(_towerAttackTimers.Keys);
            foreach (var key in keys)
            {
                _towerAttackTimers[key] -= Time.deltaTime;
                var model = _towerModels.ContainsKey(key) ? _towerModels[key] : null;
                if (model == null) continue;

                float t = _towerAttackTimers[key];
                var towerData = _gridData[key.Item1, key.Item2];

                if (t > 0)
                {
                    // 根据塔类型做不同动画
                    int towerType = GetTowerTypeAt(key.Item1, key.Item2);
                    ApplyAttackAnimation(model, towerType, t);
                }
                else
                {
                    // 恢复原位
                    if (_towerOriginalPos.ContainsKey(key))
                        model.transform.localPosition = _towerOriginalPos[key];
                    _towerAttackTimers.Remove(key);
                }
            }

        }
        private void ApplyAttackAnimation(GameObject model, int towerType, float remainTime)
        {
            float shake = Mathf.Sin(remainTime * 40f) * 0.05f;

            switch (towerType)
            {
                case 1: // 炮塔：后坐力
                    model.transform.localPosition += new Vector3(0f, 0f, -shake * 0.3f);
                    break;

                case 3: // 魔法塔：左右摇摆
                    model.transform.localRotation = Quaternion.Euler(0f, shake * 20f, 0f);
                    break;

                case 4: // 火焰塔：上下抖动
                    model.transform.localPosition += new Vector3(0f, Mathf.Abs(shake) * 0.1f, 0f);
                    break;

                default: // 箭塔减速塔支援塔：轻微抖动
                    model.transform.localPosition += new Vector3(shake * 0.05f, 0f, 0f);
                    break;
            }
        }
        private int GetTowerTypeAt(int x, int y)
        {
            // 从缓存的塔数据里找类型
            foreach (var tower in _lastTowerStates)
            {
                if (tower.gridX == x && tower.gridY == y)
                    return tower.type;
            }
            return 0;
        }

        private List<TowerState> _lastTowerStates = new();
        private void SpawnCell(int x, int y, int type, Transform parent)
        {
            // 创建一个扁平Cube作为地面格子
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Cell_{x}_{y}";
            go.transform.SetParent(parent);

            float s = CellSize - CellGap;
            go.transform.localScale = new Vector3(s, 0.1f, s);
            go.transform.position = GridToWorld(x, y) + Vector3.down * 0.05f;

            // 移除默认Collider（我们用Raycast到自定义平面）
            var col = go.GetComponent<BoxCollider>();
            if (col != null) Destroy(col);

            var mr = go.GetComponent<MeshRenderer>();
            mr.material = GetCellMaterial(type);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;

            _cellObjects[x, y] = go;
            _cellRenderers[x, y] = mr;
        }

        private Material GetCellMaterial(int type)
        {
            Material mat = type switch
            {
                1 => MatBlocked,
                2 => MatSpawn,
                3 => MatBase,
                _ => MatEmpty,
            };
            return mat != null ? mat : CreateDefaultMaterial(type);
        }

        // ── 格子更新（建塔/拆塔后）─────────────────────────────

        public void SetCell(int x, int y, int type)
        {
            if (!InBounds(x, y)) return;
            _gridData[x, y] = type;

            if (_cellRenderers[x, y] != null)
                _cellRenderers[x, y].material = GetCellMaterial(type);
        }

        // ── 塔渲染（由StateSync驱动）────────────────────────────

        // 在类字段里加
       

        public void UpdateTowers(List<TowerState> towers)
        {
            _lastTowerStates = towers;
            var activeKeys = new HashSet<(int, int)>();

            foreach (var t in towers)
            {
                var key = (t.gridX, t.gridY);
                activeKeys.Add(key);

                SetCell(t.gridX, t.gridY, 1);

                if (!_towerModels.TryGetValue(key, out var model))
                {
                    model = SpawnTowerModel(t);
                    _towerModels[key] = model;
                }
                else
                {
                    string expectedName = $"Tower_{t.gridX}_{t.gridY}_L{t.level}_B{t.branch}";
                    if (model.name != expectedName)
                    {
                        Destroy(model);
                        model = SpawnTowerModel(t);
                        model.name = expectedName;
                        _towerModels[key] = model;
                    }
                }

                // 支援塔显示常驻范围圈
                if (t.type == 5)
                {
                    float range = t.level switch { 2 => 3.5f, 3 => 3.5f, _ => 3.0f };

                    if (!_supportRangeCircles.ContainsKey(key))
                    {
                        var circle = CreateSupportRangeCircle(t.gridX, t.gridY, range);
                        _supportRangeCircles[key] = circle;
                    }
                }
                else
                {
                    // 非支援塔确保没有范围圈
                    if (_supportRangeCircles.TryGetValue(key, out var oldCircle))
                    {
                        if (oldCircle != null) Destroy(oldCircle);
                        _supportRangeCircles.Remove(key);
                    }
                }
            }

            // 移除被拆的塔
            var toRemove = new List<(int, int)>();
            foreach (var kv in _towerModels)
            {
                if (!activeKeys.Contains(kv.Key))
                {
                    if (kv.Value != null) Destroy(kv.Value);
                    SetCell(kv.Key.Item1, kv.Key.Item2, 0);
                    toRemove.Add(kv.Key);

                    // 同时移除支援塔范围圈
                    if (_supportRangeCircles.TryGetValue(kv.Key, out var circle))
                    {
                        if (circle != null) Destroy(circle);
                        _supportRangeCircles.Remove(kv.Key);
                    }
                }
            }
            foreach (var t in towers)
            {
                var key = (t.gridX, t.gridY);
                if (_towerModels.TryGetValue(key, out var model))
                {
                    UpdateTowerHpBar(model, t.hp, t.maxHp);
                    // 面向相机
                    var hpBarRoot = model.transform.Find("TowerHpBar");
                    if (hpBarRoot != null && Camera.main != null)
                        hpBarRoot.rotation = Camera.main.transform.rotation;
                }
            }

            foreach (var k in toRemove) _towerModels.Remove(k);
        }// 在 SpawnTowerModel 里加血条
        private void UpdateTowerHpBar(GameObject tower, int hp, int maxHp)
        {
            if (maxHp == 0) return;

            // 找血条填充物体
            var hpBarRoot = tower.transform.Find("TowerHpBar");
            if (hpBarRoot == null)
            {
                CreateTowerHpBar(tower, maxHp);
                hpBarRoot = tower.transform.Find("TowerHpBar");
            }

            Transform fill = null;
            foreach (Transform child in hpBarRoot)
            {
                if (child.name == "TowerHpFill") { fill = child; break; }
            }

            if (fill == null) return;

            float ratio = (float)hp / maxHp;
            fill.localScale = new Vector3(ratio * 0.8f, 0.06f, 1f);
            fill.localPosition = new Vector3(-(1f - ratio) * 0.4f, 0f, 0f);

            var mr = fill.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.material.color = ratio > 0.5f
                    ? Color.Lerp(Color.yellow, Color.green, (ratio - 0.5f) * 2f)
                    : Color.Lerp(Color.red, Color.yellow, ratio * 2f);
            }

            // 满血时隐藏血条
            hpBarRoot.gameObject.SetActive(hp < maxHp);
        }
        private void CreateTowerHpBar(GameObject tower, int maxHp)
        {
            var hpBarRoot = new GameObject("TowerHpBar");
            hpBarRoot.transform.SetParent(tower.transform);
            hpBarRoot.transform.localPosition = new Vector3(0f, 1.5f, 0f);

            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.transform.SetParent(hpBarRoot.transform);
            bg.transform.localPosition = Vector3.zero;
            bg.transform.localScale = new Vector3(0.8f, 0.06f, 1f);
            var bgMat = new Material(Shader.Find("Sprites/Default"));
            bgMat.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
            bg.GetComponent<MeshRenderer>().material = bgMat;
            var bgCol = bg.GetComponent<MeshCollider>();
            if (bgCol != null) Destroy(bgCol);

            var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fill.name = "TowerHpFill";
            fill.transform.SetParent(hpBarRoot.transform);
            fill.transform.localPosition = Vector3.forward * -0.001f;
            fill.transform.localScale = new Vector3(0.8f, 0.06f, 1f);
            var fillMat = new Material(Shader.Find("Sprites/Default"));
            fillMat.color = Color.green;
            fill.GetComponent<MeshRenderer>().material = fillMat;
            var fillCol = fill.GetComponent<MeshCollider>();
            if (fillCol != null) Destroy(fillCol);
        }

        private GameObject CreateSupportRangeCircle(int x, int y, float range)
        {
            var go = new GameObject("SupportRange");
            go.transform.SetParent(transform);
            go.transform.position = GridToWorld(x, y) + Vector3.up * 0.05f;

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.startWidth = 0.04f;
            lr.endWidth = 0.04f;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lr.endColor = new Color(1f, 0.85f, 0.2f, 0.4f);  // 金黄色半透明

            int segments = 48;
            lr.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * 2f * Mathf.PI / segments;
                lr.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * range, 0.1f, Mathf.Sin(angle) * range));
            }

            return go;
        }

        private GameObject SpawnTowerModel(TowerState t)
        {
            var prefab = GetTowerPrefab(t.type, t.level, t.branch);

            GameObject go;
            if (prefab != null)
                go = Instantiate(prefab);
            else
                go = CreateDefaultTower(t.type);

            go.name = $"Tower_{t.gridX}_{t.gridY}_L{t.level}_B{t.branch}";
            go.transform.SetParent(transform);
            go.transform.position = GridToWorld(t.gridX, t.gridY) + Vector3.up * 0.3f;
            UpdateTowerLevel(go, t.level, t.type);
            return go;
        }
        private GameObject GetTowerPrefab(int type, int level, int branch)
        {
            return type switch
            {
                0 => branch switch
                {
                    1 => TowerPrefab_Arrow_Sniper,
                    2 => TowerPrefab_Arrow_Multishot,
                    _ => level >= 2 ? TowerPrefab_Arrow_Lv2 : TowerPrefab_Arrow_Lv1
                },
                1 => branch switch
                {
                    1 => TowerPrefab_Cannon_Heavy,
                    2 => TowerPrefab_Cannon_Cluster,
                    _ => level >= 2 ? TowerPrefab_Cannon_Lv2 : TowerPrefab_Cannon_Lv1
                },
                2 => branch switch
                {
                    1 => TowerPrefab_Ice_Freeze,
                    2 => TowerPrefab_Ice_Poison,
                    _ => level >= 2 ? TowerPrefab_Ice_Lv2 : TowerPrefab_Ice_Lv1
                },
                3 => branch switch
                {
                    1 => TowerPrefab_Magic_Lightning,
                    2 => TowerPrefab_Magic_SoulHarvest,
                    _ => level >= 2 ? TowerPrefab_Magic_Lv2 : TowerPrefab_Magic_Lv1
                },
                4 => branch switch
                {
                    1 => TowerPrefab_Fire_Lava,
                    2 => TowerPrefab_Fire_Phoenix,
                    _ => level >= 2 ? TowerPrefab_Fire_Lv2 : TowerPrefab_Fire_Lv1
                },
                5 => branch switch
                {
                    1 => TowerPrefab_Support_Amplify,
                    2 => TowerPrefab_Support_Gold,
                    _ => level >= 2 ? TowerPrefab_Support_Lv2 : TowerPrefab_Support_Lv1
                },
                _ => null
            };
        }
        private GameObject _baseHpBar;
        private Transform _baseHpFill;
        private TextMesh _baseHpText;

        private void CreateBaseHpBar(int baseX, int baseY)
        {
            Vector3 baseWorld = GridToWorld(baseX, baseY) + Vector3.up * 1.5f;

            _baseHpBar = new GameObject("BaseHpBar");
            _baseHpBar.transform.SetParent(transform);
            _baseHpBar.transform.position = baseWorld;

            // 背景
            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Background";
            bg.transform.SetParent(_baseHpBar.transform);
            bg.transform.localPosition = Vector3.zero;
            bg.transform.localScale = new Vector3(1.2f, 0.1f, 1f);
            var bgMat = new Material(Shader.Find("Sprites/Default"));
            bgMat.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
            bg.GetComponent<MeshRenderer>().material = bgMat;
            var bgCol = bg.GetComponent<MeshCollider>();
            if (bgCol != null) Destroy(bgCol);

            // 填充
            var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fill.name = "Fill";
            fill.transform.SetParent(_baseHpBar.transform);
            fill.transform.localPosition = Vector3.forward * -0.001f;
            fill.transform.localScale = new Vector3(1.2f, 0.1f, 1f);
            var fillMat = new Material(Shader.Find("Sprites/Default"));
            fillMat.color = new Color(0.3f, 0.9f, 0.5f);
            fill.GetComponent<MeshRenderer>().material = fillMat;
            _baseHpFill = fill.transform;
            var fillCol = fill.GetComponent<MeshCollider>();
            if (fillCol != null) Destroy(fillCol);

            // 文字
            var textGo = new GameObject("HpText");
            textGo.transform.SetParent(_baseHpBar.transform);
            textGo.transform.localPosition = new Vector3(0f, 0.12f, -0.001f);
            textGo.transform.localScale = Vector3.one * 0.18f;
            _baseHpText = textGo.AddComponent<TextMesh>();
            _baseHpText.fontSize = 48;
            _baseHpText.characterSize = 0.08f;
            _baseHpText.anchor = TextAnchor.MiddleCenter;
            _baseHpText.alignment = TextAlignment.Center;
            _baseHpText.color = Color.white;
            _baseHpText.text = "基地 500/500";
        }

        public void UpdateBaseHpBar(int hp, int maxHp)
        {
            if (_baseHpFill == null) return;
            float ratio = (float)hp / maxHp;
            _baseHpFill.localScale = new Vector3(ratio * 1.2f, 0.1f, 1f);
            _baseHpFill.localPosition = new Vector3(-(1f - ratio) * 0.6f, 0f, 0f);

            var mr = _baseHpFill.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.material.color = ratio > 0.5f
                    ? Color.Lerp(new Color(0.9f, 0.8f, 0.1f), new Color(0.3f, 0.9f, 0.5f), (ratio - 0.5f) * 2f)
                    : Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.9f, 0.8f, 0.1f), ratio * 2f);
            }

            if (_baseHpText != null)
                _baseHpText.text = $"基地 {hp}/{maxHp}";

            if (_baseHpBar != null && Camera.main != null)
                _baseHpBar.transform.rotation = Camera.main.transform.rotation;
        }
        private void UpdateTowerLevel(GameObject tower, int level, int towerType)
        {
            // 查找或创建等级文字
            var levelTextTf = tower.transform.Find("LevelText");
            TextMesh levelText;

            if (levelTextTf == null)
            {
                // 没有就创建，位置用默认值
                var textGo = new GameObject("LevelText");
                textGo.transform.SetParent(tower.transform);
                textGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
                textGo.transform.localScale = Vector3.one * 0.3f;
                levelText = textGo.AddComponent<TextMesh>();
                levelText.fontSize = 32;
                levelText.characterSize = 0.1f;
                levelText.anchor = TextAnchor.MiddleCenter;
                levelText.alignment = TextAlignment.Center;
            }
            else
            {
                // 已有就直接用，不改位置
                levelText = levelTextTf.GetComponent<TextMesh>();
                if (levelText == null)
                {
                    levelText = levelTextTf.gameObject.AddComponent<TextMesh>();
                    levelText.fontSize = 32;
                    levelText.characterSize = 0.1f;
                    levelText.anchor = TextAnchor.MiddleCenter;
                    levelText.alignment = TextAlignment.Center;
                }
            }

            if (levelText != null)
            {
                levelText.text = level switch
                {
                    3 => "★",
                    2 => "Lv2",
                    _ => "Lv1",
                };

                levelText.color = level switch
                {
                    3 => new Color(1f, 0.8f, 0.1f),
                    2 => new Color(0.3f, 0.8f, 1f),
                    _ => new Color(0.8f, 0.8f, 0.8f),
                };
            }
        }

        // ── 路径绘制 ─────────────────────────────────────────────

        public void DrawPath(List<Vec2Int> path, int field)
        {
            // 清除旧路径
            if (_pathOverlays.TryGetValue(field, out var oldList))
            {
                foreach (var obj in oldList) if (obj != null) Destroy(obj);
                oldList.Clear();
            }
            else
            {
                _pathOverlays[field] = new List<GameObject>();
            }

            Material pathMat = field switch
            {
                0 => MatPath0,
                1 => MatPath1,
                2 => MatPath2,
                3 => MatPath3,
                _ => MatPath0,
            };

            if (pathMat == null) pathMat = CreateDefaultPathMaterial(field);

            var list = _pathOverlays[field];

            foreach (var p in path)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = $"Path_{field}_{p.x}_{p.y}";
                go.transform.SetParent(transform);

                // 水平放置在地面略上方
                go.transform.position = GridToWorld(p.x, p.y) + Vector3.up * 0.01f * (field + 1);
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                go.transform.localScale = Vector3.one * (CellSize * 0.85f);

                var col = go.GetComponent<MeshCollider>();
                if (col != null) Destroy(col);

                var mr = go.GetComponent<MeshRenderer>();
                mr.material = pathMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                list.Add(go);
            }
        }

        // ── 建塔预览 ─────────────────────────────────────────────

        public void ShowPreview(int x, int y, bool canPlace)
        {
            HidePreview();
            _previewObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _previewObj.name = "TowerPreview";

            var col = _previewObj.GetComponent<BoxCollider>();
            if (col != null) Destroy(col);

            _previewObj.transform.position = GridToWorld(x, y) + Vector3.up * 0.25f;
            _previewObj.transform.localScale = new Vector3(
                CellSize * 0.7f, 0.5f, CellSize * 0.7f);

            var mr = _previewObj.GetComponent<MeshRenderer>();
            if (canPlace && MatPreviewOk != null)
                mr.material = MatPreviewOk;
            else if (!canPlace && MatPreviewBad != null)
                mr.material = MatPreviewBad;
            else
                mr.material.color = canPlace
                    ? new Color(0f, 1f, 0f, 0.4f)
                    : new Color(1f, 0f, 0f, 0.4f);
        }

        public void HidePreview()
        {
            if (_previewObj != null) Destroy(_previewObj);
        }

        // ── 攻击范围圈 ──────────────────────────────────────────

        public void ShowRangeCircle(int x, int y, float range)
        {
            HideRangeCircle();

            if (RangeCirclePrefab != null)
            {
                _rangeCircle = Instantiate(RangeCirclePrefab);
            }
            else
            {
                // 默认用LineRenderer画圆
                _rangeCircle = new GameObject("RangeCircle");
                var lr = _rangeCircle.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = true;
                lr.startWidth = 0.05f;
                lr.endWidth = 0.05f;
                lr.material = new Material(Shader.Find("Sprites/Default"));
                lr.startColor = lr.endColor = new Color(1f, 1f, 0f, 0.5f);

                int segments = 48;
                lr.positionCount = segments;
                for (int i = 0; i < segments; i++)
                {
                    float angle = i * 2f * Mathf.PI / segments;
                    lr.SetPosition(i, new Vector3(
                        Mathf.Cos(angle) * range, 0.1f, Mathf.Sin(angle) * range));
                }
            }

            _rangeCircle.transform.position = GridToWorld(x, y) + Vector3.up * 0.05f;
            if (RangeCirclePrefab != null)
                _rangeCircle.transform.localScale = Vector3.one * range * 2f;
        }

        public void HideRangeCircle()
        {
            if (_rangeCircle != null) Destroy(_rangeCircle);
        }

        // ── 坐标转换（2D的X,Y → 3D的X,Z）────────────────────────

        /// <summary>
        /// 将服务端格子坐标(x,y)转换为3D世界坐标。
        /// 服务端的Y轴 → Unity的Z轴，高度Y固定为0。
        /// </summary>
        public Vector3 GridToWorld(int x, int y) =>
            new Vector3(x * CellSize + CellSize / 2f, 0f, y * CellSize + CellSize / 2f);

        /// <summary>
        /// 将服务端的float坐标转为3D世界坐标（用于怪物/子弹插值）
        /// </summary>
        public Vector3 ServerToWorld(float sx, float sy) =>
            new Vector3(sx * CellSize, 0f, sy * CellSize);

        /// <summary>
        /// 从射线击中点反算格子坐标
        /// </summary>
        public bool WorldToGrid(Vector3 world, out int x, out int y)
        {
            x = Mathf.FloorToInt(world.x / CellSize);
            y = Mathf.FloorToInt(world.z / CellSize);
            return InBounds(x, y);
        }

        public int GetCellType(int x, int y) =>
            InBounds(x, y) ? _gridData[x, y] : -1;

        public bool InBounds(int x, int y) =>
            x >= 0 && x < _mapW && y >= 0 && y < _mapH;

        public int MapWidth  => _mapW;
        public int MapHeight => _mapH;

        // ── 默认材质（无美术资源时的占位）────────────────────────

        private Material CreateDefaultMaterial(int type)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = type switch
            {
                0 => new Color(0.35f, 0.55f, 0.25f),  // 草绿
                1 => new Color(0.45f, 0.35f, 0.2f),   // 棕色
                2 => new Color(0.9f, 0.2f, 0.15f),    // 红色
                3 => new Color(0.15f, 0.4f, 0.9f),    // 蓝色
                _ => Color.gray,
            };
            return mat;
        }

        private Material CreateDefaultPathMaterial(int field)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.SetFloat("_Mode", 3); // Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;

            mat.color = field switch
            {
                0 => new Color(1f, 0.9f, 0.2f, 0.35f),
                1 => new Color(0.2f, 0.8f, 1f, 0.35f),
                2 => new Color(0.2f, 1f, 0.3f, 0.35f),
                3 => new Color(1f, 0.3f, 0.8f, 0.35f),
                _ => new Color(1f, 1f, 1f, 0.3f),
            };
            return mat;
        }

        private GameObject CreateDefaultTower(int type)
        {
            var go = new GameObject("DefaultTower3D");

            // 底座
            var baseObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseObj.name = "Base";
            baseObj.transform.SetParent(go.transform);
            baseObj.transform.localPosition = Vector3.zero;
            baseObj.transform.localScale = new Vector3(0.6f, 0.15f, 0.6f);
            var baseCol = baseObj.GetComponent<CapsuleCollider>();
            if (baseCol != null) Destroy(baseCol);

            // 塔身
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(go.transform);
            body.transform.localPosition = Vector3.up * 0.3f;
            body.transform.localScale = new Vector3(0.35f, 0.3f, 0.35f);
            var bodyCol = body.GetComponent<CapsuleCollider>();
            if (bodyCol != null) Destroy(bodyCol);

            // 顶部特征
            var top = GameObject.CreatePrimitive(
                type == 1 ? PrimitiveType.Sphere : PrimitiveType.Cube);
            top.name = "Top";
            top.transform.SetParent(go.transform);
            top.transform.localPosition = Vector3.up * 0.6f;
            top.transform.localScale = type == 1
                ? new Vector3(0.3f, 0.3f, 0.3f)
                : new Vector3(0.25f, 0.15f, 0.25f);
            var topCol = top.GetComponent<Collider>();
            if (topCol != null) Destroy(topCol);

            // 等级指示灯
            var indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.name = "LevelIndicator";
            indicator.transform.SetParent(go.transform);
            indicator.transform.localPosition = Vector3.up * 0.8f;
            indicator.transform.localScale = Vector3.one * 0.1f;
            var indCol = indicator.GetComponent<SphereCollider>();
            if (indCol != null) Destroy(indCol);

            // 颜色
            Color towerColor = type switch
            {
                0 => new Color(0.6f, 0.6f, 0.6f),   // 箭塔-灰
                1 => new Color(0.7f, 0.3f, 0.1f),   // 炮塔-橙
                2 => new Color(0.2f, 0.5f, 0.9f),   // 减速塔-蓝
                3 => new Color(0.6f, 0.2f, 0.9f),   // 魔法塔-紫
                4 => new Color(0.9f, 0.3f, 0.1f),   // 火焰塔-红
                5 => new Color(0.9f, 0.8f, 0.2f),   // 支援塔-金
                _ => Color.gray,
            };

            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
            {
                if (mr.gameObject.name != "LevelIndicator")
                    mr.material.color = towerColor;
            }

            return go;
        }
    }
}
