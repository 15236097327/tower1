using MazeTD.Client.Game;
using MazeTD.Client.Network;
using UnityEngine;

namespace MazeTD.Client.Game
{
    /// <summary>
    /// 3D建塔交互控制器。
    ///
    /// 改造要点：
    /// - ScreenToWorldPoint → Physics.Raycast（射线击中地面平面）
    /// - 使用LayerMask过滤地面层
    /// - 悬停时显示3D预览模型和攻击范围圈
    /// - 支持塔信息面板（悬停已有塔时）
    /// </summary>
    public class TowerPlacer3D : MonoBehaviour
    {
        [Header("引用")]
        public GridRenderer3D GridRenderer;
        public UIManager      UIManager;

        [Header("射线配置")]
        public LayerMask GroundLayer = ~0;     // 地面层（默认射向所有层）
        public float     RayMaxDist  = 200f;

        // 当前选中的塔类型
        private int  _selectedTowerType = 0;
        private bool _isPlaceMode       = false;
        private Camera _cam;

        // 悬停的格子
        private int _hoverX = -1, _hoverY = -1;

        // 塔属性（客户端只读配置，与服务端TowerConfig对齐）
        //private static readonly float[] TowerRange = { 2.5f, 2.0f, 2.5f };
        private static readonly float[] TowerRange = { 2.5f, 2.0f, 2.5f, 3.0f, 2.0f, 3.0f };
        private void Start()
        {
            _cam = Camera.main;
        }

        private void Update()
        {
            if (!GameManager.Instance.GameStarted) return;

            // 右键取消放置模式
            if (Input.GetMouseButtonDown(1))
            {
                ExitPlaceMode();
                return;
            }

            if (_isPlaceMode)
                HandlePlaceModeInput();
            else
                HandleNormalInput();
        }

        // ── 放置模式 ─────────────────────────────────────────────

        private void HandlePlaceModeInput()
        {
            if (NetworkManager.Instance.LocalMode == 1 &&
        NetworkManager.Instance.LocalRole == 1) return;
            if (!TryRaycastGrid(out int gx, out int gy))
            {
                GridRenderer.HidePreview();
                GridRenderer.HideRangeCircle();
                _hoverX = _hoverY = -1;
                return;
            }

            // 格子变化时更新预览
            if (gx != _hoverX || gy != _hoverY)
            {
                _hoverX = gx;
                _hoverY = gy;

                int cellType = GridRenderer.GetCellType(gx, gy);
                bool canPlace = cellType == 0;
                GridRenderer.ShowPreview(gx, gy, canPlace);

                // 显示范围圈
                float range = _selectedTowerType < TowerRange.Length
                    ? TowerRange[_selectedTowerType] : 2.5f;
                GridRenderer.ShowRangeCircle(gx, gy, range);
            }

            // 点击放置
            if (Input.GetMouseButtonDown(0))
            {
                Debug.Log($"[TowerPlacer3D] 点击放置 gx={gx} gy={gy} cellType={GridRenderer.GetCellType(gx, gy)}");
                int cellType = GridRenderer.GetCellType(gx, gy);
                if (cellType == 0)
                {
                    GameManager.Instance.RequestPlaceTower(gx, gy, _selectedTowerType);
                }
            }
        }

        // ── 普通模式（点击已有塔）────────────────────────────────

        private void HandleNormalInput()
        {
            if (NetworkManager.Instance.LocalMode == 1 &&
         NetworkManager.Instance.LocalRole == 1) return;
            // 悬停检测（显示范围圈）
            if (TryRaycastGrid(out int gx, out int gy))
            {
                if (gx != _hoverX || gy != _hoverY)
                {
                    _hoverX = gx;
                    _hoverY = gy;

                    if (GridRenderer.GetCellType(gx, gy) == 1)
                    {
                        // 悬停已有塔：显示范围圈
                        // 需要从StateSync数据获取塔类型，这里用默认范围
                        GridRenderer.ShowRangeCircle(gx, gy, 2.5f);
                    }
                    else
                    {
                        GridRenderer.HideRangeCircle();
                    }
                }

                // 左键点击已有塔
                if (Input.GetMouseButtonDown(0))
                {
                    if (GridRenderer.GetCellType(gx, gy) == 1)
                        UIManager?.ShowTowerOptions(gx, gy);
                }
            }
            else
            {
                if (_hoverX != -1)
                {
                    _hoverX = _hoverY = -1;
                    GridRenderer.HideRangeCircle();
                }
            }
        }

        // ── 射线检测 ─────────────────────────────────────────────

        /// <summary>
        /// 从鼠标位置发射射线，检测击中地面的位置并转换为格子坐标。
        /// 使用两种策略：
        /// 1. Physics.Raycast（如果地面有Collider）
        /// 2. 数学平面交点（回退方案）
        /// </summary>
        private bool TryRaycastGrid(out int gx, out int gy)
        {
            gx = gy = -1;
            Ray ray = _cam.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit, RayMaxDist, GroundLayer))
            {
                //Debug.Log($"[TowerPlacer3D] 射线击中 {hit.point} collider={hit.collider.name}");
                return GridRenderer.WorldToGrid(hit.point, out gx, out gy);
            }

            var groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float enter))
            {
                Vector3 point = ray.GetPoint(enter);
                Debug.Log($"[TowerPlacer3D] 数学平面击中 {point}");
                return GridRenderer.WorldToGrid(point, out gx, out gy);
            }

            Debug.Log("[TowerPlacer3D] 射线未击中任何东西");
            return false;
        }

        // ── 公共接口 ─────────────────────────────────────────────

        public void EnterPlaceMode(int towerType)
        {
            Debug.Log($"[TowerPlacer3D] EnterPlaceMode type={towerType}");  // 加这行
            _selectedTowerType = towerType;
            _isPlaceMode = true;
            _hoverX = _hoverY = -1;
        }

        public void ExitPlaceMode()
        {
            _isPlaceMode = false;
            _hoverX = _hoverY = -1;
            GridRenderer.HidePreview();
            GridRenderer.HideRangeCircle();
        }

        public void OnPlaceFailed(int x, int y)
        {
            Debug.Log($"[TowerPlacer3D] 建塔失败 ({x},{y})");
            // 可在此播放失败音效或屏幕抖动
        }
    }
}
