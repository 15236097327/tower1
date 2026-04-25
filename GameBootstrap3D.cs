using UnityEngine;
using MazeTD.Client.Network;
using MazeTD.Client.Game;

namespace MazeTD.Client
{
    /// <summary>
    /// 3D版游戏场景启动器。
    ///
    /// 场景层级结构（3D版）：
    /// GameScene
    ///  ├── [Bootstrap]              ← 本脚本
    ///  ├── [NetworkManager]         ← DontDestroyOnLoad，从Lobby携带
    ///  ├── [GameManager]            ← 引用所有子系统
    ///  ├── [GridRenderer3D]         ← 地图根节点
    ///  ├── [MonsterController3D]    ← 怪物根节点
    ///  ├── [BulletRenderer3D]       ← 子弹根节点
    ///  ├── [TowerPlacer3D]
    ///  ├── [UIManager]
    ///  ├── Main Camera              ← 透视相机 + CameraController3D
    ///  ├── Directional Light        ← 主光源（模拟太阳）
    ///  ├── Canvas (UI)
    ///  └── EventSystem
    ///
    /// 光照设置建议：
    /// - Directional Light: Rotation(50, -30, 0), 强度1.0, 软阴影
    /// - Ambient: Gradient模式, 天空色#B4D4E8, 地面色#3A4A3A
    /// - 可选Point Light放在基地位置做氛围光
    /// </summary>
    public class GameBootstrap3D : MonoBehaviour
    {
        [Header("光照（可选自动创建）")]
        public bool AutoCreateLighting = true;

        private void Awake()
        {
            if (NetworkManager.Instance == null)
            {
                Debug.LogError("[Bootstrap3D] NetworkManager不存在！请先从Lobby场景进入。");
                return;
            }

            // 确保相机是透视模式
            var cam = Camera.main;
            if (cam != null)
            {
                cam.orthographic = false;
                cam.fieldOfView = 60f;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 200f;

                // 确保有CameraController3D
                if (cam.GetComponent<CameraController3D>() == null)
                    cam.gameObject.AddComponent<CameraController3D>();
            }

            // 自动光照
            if (AutoCreateLighting)
                SetupLighting();

            // 创建地面碰撞平面（供射线检测用）
            CreateGroundCollider();

            Debug.Log("[Bootstrap3D] 3D GameScene 初始化完成");
        }

        private void SetupLighting()
        {
            // 检查是否已有方向光
            if (FindObjectOfType<Light>() != null) return;

            // 主方向光
            var sunGo = new GameObject("Directional Light");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.0f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;
            sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 环境光设置
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.7f, 0.83f, 0.91f);
            RenderSettings.ambientEquatorColor = new Color(0.6f, 0.65f, 0.6f);
            RenderSettings.ambientGroundColor = new Color(0.23f, 0.29f, 0.23f);
        }

        /// <summary>
        /// 创建一个不可见的地面碰撞器，用于TowerPlacer3D的射线检测
        /// </summary>
        private void CreateGroundCollider()
        {
            var ground = new GameObject("GroundCollider");
            ground.layer = LayerMask.NameToLayer("Default");
            var col = ground.AddComponent<BoxCollider>();
            // 足够大的平面
            col.size = new Vector3(100f, 0.01f, 100f);
            col.center = new Vector3(50f, -0.01f, 50f);  // 大致地图中心
            // 不需要MeshRenderer，只需Collider
        }
    }
}
