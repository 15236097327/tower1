using MazeTD.Client.Network;
using MazeTD.Shared;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
namespace MazeTD.Client.Game
{
    /// <summary>
    /// UI管理器：负责所有HUD元素。
    /// 使用Unity UGUI，不依赖TextMeshPro（可选升级）。
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Header("HUD")]
        public TextMeshProUGUI     GoldText;
        public TextMeshProUGUI     WaveText;
        public TextMeshProUGUI     MessageText;
        public TextMeshProUGUI     ErrorText;

       

        [Header("建塔按钮")]
        public GameObject TowerButtonGroup;
        public Button   BtnPlaceTower0;  // 箭塔
        public Button   BtnPlaceTower1;  // 炮塔
        public Button   BtnPlaceTower2;  // 减速塔
        public Button BtnPlaceTower3;  // 魔法
        public Button BtnPlaceTower4;  // 火焰
        public Button BtnPlaceTower5;//支援
        [Header("Legion专用")]
        public GameObject LegionPanel;

        // 发兵按钮（9种）
        public Button BtnSendTroops0;
        public Button BtnSendTroops1;
        public Button BtnSendTroops2;
        public Button BtnSendTroops3;
        public Button BtnSendTroops4;
        public Button BtnSendTroops5;
        public Button BtnSendTroops6;
        public Button BtnSendTroops7;
        public Button BtnSendTroops8;

        // 解锁按钮（6个）
        public Button BtnUnlock0;
        public Button BtnUnlock1;
        public Button BtnUnlock2;
        public Button BtnUnlock3;
        public Button BtnUnlock4;
        public Button BtnUnlock5;

        // 方向按钮（已有）
        public Button BtnDirTop;
        public Button BtnDirBottom;
        public Button BtnDirLeft;
        public Button BtnDirRight;

        // 内部状态
       
        private Button _selectedDirBtn;
        private bool[] _unlockedTroops = new bool[6];

        // 当前选中方向
        private int _selectedDirection = 0;  // 0=下 1=上 2=左 3=右，对应服务端路径索引
        [Header("塔操作面板")]
        public GameObject TowerOptionPanel;
        public Button BtnUpgrade;
        public Button BtnRemove;
        public Button BtnClose;
        public TMP_Text TowerInfoText;
        public TMP_Text StatsText;
        public TMP_Text CostText;

        // 分支选择（新增）
        public GameObject BranchPanel;
        public TMP_Text BranchTitleText;
        public Button BtnBranchA;
        public TMP_Text BranchADescText;
        public Button BtnBranchB;
        public TMP_Text BranchBDescText;

        private int _selectedTX, _selectedTY;
        private int _selectedTowerType;

        [Header("聊天")]
        public GameObject ChatPanel;
        public TextMeshProUGUI ChatLog;
        public TMP_InputField ChatInput;
        public Button     ChatSendBtn;

        [Header("游戏结束")]
        public GameObject GameOverPanel;
        public TextMeshProUGUI GameOverTitle;
        public TextMeshProUGUI GameOverDetail;
        public TMP_Text GameOverStatsText;   // 新增
        public Button BtnBackToLobby;      // 新增


        private TowerPlacer3D _placer;
        // 字段
        private List<TowerState> _cachedTowers = new List<TowerState>();
        private void Start()
        {
            _placer = FindObjectOfType<TowerPlacer3D>();
            Debug.Log($"[UIManager] TowerPlacer3D = {_placer}");
            // 建塔按钮
           
            BtnPlaceTower0?.onClick.AddListener(() => _placer?.EnterPlaceMode(0));
            BtnPlaceTower1?.onClick.AddListener(() => _placer?.EnterPlaceMode(1));
            BtnPlaceTower2?.onClick.AddListener(() => _placer?.EnterPlaceMode(2));
            BtnPlaceTower3?.onClick.AddListener(() => _placer?.EnterPlaceMode(3));
            BtnPlaceTower4?.onClick.AddListener(() => _placer?.EnterPlaceMode(4));
            BtnPlaceTower5?.onClick.AddListener(() => _placer?.EnterPlaceMode(5));



            // Legion发兵按钮
            var troopCosts = new[] { 50, 120, 300, 80, 60, 150, 200, 400, 350 };
            var troopCounts = new[] { 5, 3, 1, 4, 6, 2, 2, 1, 1 };
            for (int i = 0; i < 9; i++)
            {
                int idx = i;
                GetSendTroopBtn(i)?.onClick.AddListener(() =>
                    GameManager.Instance.RequestSendTroops(idx, troopCounts[idx], _selectedDirection));
            }
            BtnUnlock0?.onClick.AddListener(() => GameManager.Instance.RequestUnlockTroop(0, 0));
            BtnUnlock1?.onClick.AddListener(() => GameManager.Instance.RequestUnlockTroop(0, 1));
            BtnUnlock2?.onClick.AddListener(() => GameManager.Instance.RequestUnlockTroop(0, 2));
            BtnUnlock3?.onClick.AddListener(() => GameManager.Instance.RequestUnlockTroop(1, 0));
            BtnUnlock4?.onClick.AddListener(() => GameManager.Instance.RequestUnlockTroop(1, 1));
            BtnUnlock5?.onClick.AddListener(() => GameManager.Instance.RequestUnlockTroop(1, 2));

            BtnDirTop?.onClick.AddListener(() => SelectDirection(1, BtnDirTop));
            BtnDirBottom?.onClick.AddListener(() => SelectDirection(0, BtnDirBottom));
            BtnDirLeft?.onClick.AddListener(() => SelectDirection(2, BtnDirLeft));
            BtnDirRight?.onClick.AddListener(() => SelectDirection(3, BtnDirRight));

            // 默认选中下方
            SelectDirection(0, BtnDirBottom);
            // 塔操作
            BtnUpgrade?.onClick.AddListener(OnUpgradeClick);
            BtnRemove?.onClick.AddListener(OnRemoveClick);
            BtnClose?.onClick.AddListener(() => TowerOptionPanel?.SetActive(false));
            TowerOptionPanel?.SetActive(false);
            BtnBranchA?.onClick.AddListener(() => OnBranchClicked(1));
            BtnBranchB?.onClick.AddListener(() => OnBranchClicked(2));
            BranchPanel?.SetActive(false);
            // 聊天
            ChatSendBtn?.onClick.AddListener(OnChatSend);

            

            // 连接
            BtnBackToLobby?.onClick.AddListener(() =>
            {
                GameOverPanel?.SetActive(false);
                NetworkManager.Instance.Disconnect();  // 确保断开连接
                NetworkManager.Instance.ResetGameState();
                SceneManager.LoadScene(0);
            });
            GameOverPanel?.SetActive(false);
            LegionPanel?.SetActive(false);
        }
        private Button GetSendTroopBtn(int i) => i switch
        {
            0 => BtnSendTroops0,
            1 => BtnSendTroops1,
            2 => BtnSendTroops2,
            3 => BtnSendTroops3,
            4 => BtnSendTroops4,
            5 => BtnSendTroops5,
            6 => BtnSendTroops6,
            7 => BtnSendTroops7,
            8 => BtnSendTroops8,
            _ => null
        };

        private Button GetUnlockBtn(int i) => i switch
        {
            0 => BtnUnlock0,
            1 => BtnUnlock1,
            2 => BtnUnlock2,
            3 => BtnUnlock3,
            4 => BtnUnlock4,
            5 => BtnUnlock5,
            _ => null
        };
        // ── 连接面板 ─────────────────────────────────────────────



        // ── 游戏开始 ─────────────────────────────────────────────

        public void OnGameStart(S2C_GameStartPayload data, int playerId, int role)
        {
            BtnPlaceTower0?.GetComponentInChildren<TMP_Text>()?.SetText("箭塔 ¥50");
            BtnPlaceTower1?.GetComponentInChildren<TMP_Text>()?.SetText("炮塔 ¥100");
            BtnPlaceTower2?.GetComponentInChildren<TMP_Text>()?.SetText("减速塔 ¥80");
            BtnPlaceTower3?.GetComponentInChildren<TMP_Text>()?.SetText("魔法塔 120");
            BtnPlaceTower4?.GetComponentInChildren<TMP_Text>()?.SetText("火焰塔 ¥110");
            BtnPlaceTower5?.GetComponentInChildren<TMP_Text>()?.SetText("支援塔 ¥150");

            BtnSendTroops0?.GetComponentInChildren<TMP_Text>()?.SetText("普通兵x5\n¥50");
            BtnSendTroops1?.GetComponentInChildren<TMP_Text>()?.SetText("精英兵x3\n¥120");
            BtnSendTroops2?.GetComponentInChildren<TMP_Text>()?.SetText("BOSSx1\n¥300");
            BtnSendTroops3?.GetComponentInChildren<TMP_Text>()?.SetText("重甲兵x4\n¥80");
            BtnSendTroops4?.GetComponentInChildren<TMP_Text>()?.SetText("速度兵x6\n¥60");
            BtnSendTroops5?.GetComponentInChildren<TMP_Text>()?.SetText("召唤师x2\n¥150");
            // 发兵按钮文字
            string[] troopNames ={"普通兵\nx5\n¥50","精英兵\nx3\n¥120","BOSS\nx1\n¥300",
    "重甲兵\nx4\n¥80","速度兵\nx6\n¥60","召唤师\nx2\n¥150",
    "刺客\nx2\n¥200","泰坦\nx1\n¥400","暗影\nx1\n¥350"};
            for (int i = 0; i < 9; i++)
                GetSendTroopBtn(i)?.GetComponentInChildren<TMP_Text>()?.SetText(troopNames[i]);

            // 解锁按钮文字
            string[] unlockNames ={
    "重甲兵\n¥100","召唤师\n¥200","泰坦\n¥400","速度兵\n¥100","刺客\n¥200" ,"暗影领主\n¥400"
};
            for (int i = 0; i < 6; i++)
                GetUnlockBtn(i)?.GetComponentInChildren<TMP_Text>()?.SetText(unlockNames[i]);

            // 初始状态：高级兵种全部锁定
            RefreshTroopState();
            if (GoldText != null) GoldText.text = $"¥ {data.initGold}";
            if (WaveText != null) WaveText.text = "Wave: 0";

            bool isLegion = data.mode == 1;

            if (isLegion)
            {
                if (role == 1) // 进攻方
                {
                    // 隐藏建塔按钮，显示派兵面板
                    TowerButtonGroup?.SetActive(false);
                    LegionPanel?.SetActive(true);
                }
                else // 防守方 role==0
                {
                    // 显示建塔按钮，隐藏派兵面板
                    TowerButtonGroup?.SetActive(true);
                    LegionPanel?.SetActive(false);
                }

                
            }
            else // 合作模式
            {
                TowerButtonGroup?.SetActive(true);
                LegionPanel?.SetActive(false);
               
            }

            

            ShowMessage($"游戏开始！你是 Player {playerId}");
            StartCoroutine(ClearMessageAfter(3f));
        }
        private void RefreshTroopState()
        {
            // 前三种默认可用
            for (int i = 0; i < 3; i++)
                SetTroopBtnState(GetSendTroopBtn(i), true);

            // 解锁index到发兵按钮的映射
            // 左列：0→3(重甲) 1→5(召唤) 2→7(泰坦)
            // 右列：3→4(速度) 4→6(刺客) 5→8(暗影)
            int[] unlockToTroopBtn = { 3, 5, 7, 4, 6, 8 };

            for (int i = 0; i < 6; i++)
            {
                bool isUnlocked = i < _unlockedTroops.Length && _unlockedTroops[i];
                SetTroopBtnState(GetSendTroopBtn(unlockToTroopBtn[i]), isUnlocked);

                var unlockBtn = GetUnlockBtn(i);
                if (unlockBtn != null)
                {
                    unlockBtn.gameObject.SetActive(!isUnlocked);
                    int prereq = i switch
                    {
                        1 => 0,  // 召唤需要重甲
                        2 => 1,  // 泰坦需要召唤
                        4 => 3,  // 刺客需要速度
                        5 => 4,  // 暗影需要刺客
                        _ => -1
                    };
                    unlockBtn.interactable = prereq < 0 || _unlockedTroops[prereq];
                }
            }
        }

        private void SetTroopBtnState(Button btn, bool available)
        {
            if (btn == null) return;
            btn.interactable = available;
            var colors = btn.colors;
            colors.normalColor = available
                ? new Color(0.9f, 0.9f, 0.9f)
                : new Color(0.4f, 0.4f, 0.4f);
            btn.colors = colors;
        }
        //private Button _selectedDirBtn;
        public void OnTroopTreeUpdate(bool[] unlocked)
        {
            _unlockedTroops = unlocked;
            RefreshTroopState();
        }

        public void OnUnlockFailed(string reason)
        {
            ShowError($"解锁失败：{reason}");
        }
        private void SelectDirection(int dir, Button btn)
        {
            _selectedDirection = dir;

            // 高亮选中的按钮，其他恢复默认
            foreach (var b in new[] { BtnDirTop, BtnDirBottom, BtnDirLeft, BtnDirRight })
            {
                if (b == null) continue;
                var colors = b.colors;
                colors.normalColor = b == btn
                    ? new Color(0.3f, 0.8f, 0.5f)   // 选中绿色
                    : new Color(0.9f, 0.9f, 0.9f);   // 默认
                b.colors = colors;
            }
            _selectedDirBtn = btn;
        }
        // ── HUD更新 ──────────────────────────────────────────────

        public void UpdateGold(int gold)
        {
            if (GoldText != null) GoldText.text = $"💰 {gold}";
        }
        public void ShowLegionTimer(int second)
        {
            if(WaveText!= null) WaveText.text = $"剩余 {second}s";
        }
       

        public void ShowWave(int wave, int total)
        {
            if (WaveText != null) WaveText.text = $"Wave: {wave}";
            ShowMessage($"⚠ 第 {wave} 波来袭！共 {total} 只怪物");
            StartCoroutine(ClearMessageAfter(4f));
        }

        // ── 塔操作面板 ──────────────────────────────────────────
        private (string nameA, string descA, int costA, string nameB, string descB, int costB) GetBranchInfo(int type)
        {
            return type switch
            {
                0 => ("狙击手塔", "超远射程，50%暴击x2", 100, "速射塔", "连射3箭穿透敌人", 100),
                1 => ("重炮塔", "超高伤害，命中击退", 200, "集束炮塔", "同时攻击3个目标", 200),
                2 => ("冰冻塔", "完全冰冻，冰冻受击x2", 160, "毒雾塔", "持续减速+DOT", 160),
                3 => ("闪电塔", "链式闪电跳跃5目标", 240, "灵魂收割", "击杀额外回金币", 240),
                4 => ("熔岩塔", "命中留下岩浆区域", 220, "凤凰塔", "召唤火焰鸟攻击", 220),
                5 => ("增幅塔", "周围塔攻速+30%", 300, "金币塔", "全图击杀金币+50%", 300),
                _ => ("分支A", "", 100, "分支B", "", 100),
            };
        }

        private string[] GetBranchNames(int type, int branch)
        {
            var (nameA, descA, _, nameB, descB, _) = GetBranchInfo(type);
            return branch == 1
                ? new[] { nameA, descA }
                : new[] { nameB, descB };
        }

        private (int dmg, float spd, float rng) GetTowerStats(int type, int level)
        {
            int baseDmg = type switch { 1 => 35, 2 => 5, 3 => 20, 4 => 15, 5 => 0, _ => 10 };
            float spd = type switch { 1 => 2.0f, 2 => 0.8f, 3 => 1.5f, 4 => 0.6f, 5 => 0f, _ => 1.0f };
            float rng = type switch { 1 => 2.0f, 2 => 2.5f, 3 => 3.0f, 4 => 2.0f, 5 => 3.0f, _ => 2.5f };
            return (baseDmg * level, spd, rng);
        }

        private int GetUpgradeCost(int type, int level)
        {
            int baseCost = type switch { 1 => 100, 2 => 80, 3 => 120, 4 => 110, 5 => 150, _ => 50 };
            return level == 1 ? baseCost : baseCost * 2;
        }
        public void ShowTowerOptions(int x, int y)
        {
            if (NetworkManager.Instance.LocalMode == 1 &&
        NetworkManager.Instance.LocalRole == 1) return;
            _selectedTX = x;
            _selectedTY = y;

            var tower = _cachedTowers.Find(t => t.gridX == x && t.gridY == y);
            if (tower == null)
            {
                TowerOptionPanel?.SetActive(true);
                return;
            }

            _selectedTowerType = tower.type;

            string[] names = { "箭塔", "炮塔", "减速塔", "魔法塔", "火焰塔", "支援塔" };
            string name = tower.type < names.Length ? names[tower.type] : "塔";

            // 根据等级和分支显示不同内容
            if (tower.level == 1)
            {
                // Lv.1 → 显示升级到Lv.2
                ShowLv1Panel(tower, name);
            }
            else if (tower.level == 2 && tower.branch == 0)
            {
                // Lv.2 未选分支 → 显示分支选择
                ShowBranchPanel(tower, name);
            }
            else if (tower.level == 3)
            {
                // Lv.3 已选分支 → 显示满级信息
                ShowMaxLevelPanel(tower, name);
            }

            TowerOptionPanel?.SetActive(true);
        }

        private void ShowLv1Panel(TowerState tower, string name)
        {
            if (TowerInfoText != null) TowerInfoText.text = $"{name}  Lv.1";
            if (tower.type == 5)
            {
                if (StatsText != null)
                    StatsText.text = "被动：周围3格内的塔攻速 +10%";
                if (CostText != null)
                    CostText.text = $"升级费用  ¥{GetUpgradeCost(tower.type, 1)}";
                BtnUpgrade?.gameObject.SetActive(true);
                BranchPanel?.SetActive(false);
                return;
            }
            var (dmgCur, spdCur, rngCur) = GetTowerStats(tower.type, 1);
            var (dmgNxt, spdNxt, rngNxt) = GetTowerStats(tower.type, 2);

            if (StatsText != null)
                StatsText.text = $"伤害  {dmgCur} → {dmgNxt}\n攻速  {spdCur}s → {spdNxt}s\n射程  {rngCur} → {rngNxt}";

            int cost = GetUpgradeCost(tower.type, 1);
            if (CostText != null)
                CostText.text = $"升级费用  ¥{cost}";

            BtnUpgrade?.gameObject.SetActive(true);
            BtnUpgrade.interactable = true;
            BranchPanel?.SetActive(false);
        }

        private void ShowBranchPanel(TowerState tower, string name)
        {
            if (TowerInfoText != null) TowerInfoText.text = $"{name}  Lv.2  选择进化方向";
            if (tower.type == 5)
            {
                if (StatsText != null)
                    StatsText.text = "被动：周围3.5格内的塔\n攻速 +20%  射程 +0.3\n\n选择进化方向";
            }
            else
            {
                if (StatsText != null)
                    StatsText.text = "选择一个方向进化到最终形态";
            }
            
            if (CostText != null) CostText.text = "";

            BtnUpgrade?.gameObject.SetActive(false);
            BranchPanel?.SetActive(true);

            if (BranchTitleText != null) BranchTitleText.text = "进化方向";

            // 从配置里读分支信息
            var (nameA, descA, costA, nameB, descB, costB) = GetBranchInfo(tower.type);

            if (BtnBranchA != null)
                BtnBranchA.GetComponentInChildren<TMP_Text>()?.SetText($"{nameA}\n¥{costA}");
            if (BranchADescText != null)
                BranchADescText.text = descA;

            if (BtnBranchB != null)
                BtnBranchB.GetComponentInChildren<TMP_Text>()?.SetText($"{nameB}\n¥{costB}");
            if (BranchBDescText != null)
                BranchBDescText.text = descB;
        }

        private void ShowMaxLevelPanel(TowerState tower, string name)
        {
            string[] branchNames = GetBranchNames(tower.type, tower.branch);
            if (TowerInfoText != null)
                TowerInfoText.text = $"{name} → {branchNames[0]}  满级";

            // 支援塔满级单独处理
            if (tower.type == 5)
            {
                if (StatsText != null)
                    StatsText.text = tower.branch == 1
                        ? "被动：周围3.5格内的塔攻速 +30%  射程 +0.5"
                        : "被动：全图击杀怪物金币收益 +50%";
            }
            else
            {
                if (StatsText != null)
                    StatsText.text = branchNames[1];
            }

            if (CostText != null) CostText.text = "已达最终形态";
            BtnUpgrade?.gameObject.SetActive(false);
            BranchPanel?.SetActive(false);
        }

        private void OnBranchClicked(int branch)
        {
            GameManager.Instance.RequestChooseBranch(_selectedTX, _selectedTY, branch);
            TowerOptionPanel?.SetActive(false);
        }

        
        public void UpdateTowerCache(List<TowerState> towers)
        {
            _cachedTowers = towers;
        }
        private void OnUpgradeClick()
        {
            GameManager.Instance.RequestUpgradeTower(_selectedTX, _selectedTY);
            TowerOptionPanel?.SetActive(false);
        }

        private void OnRemoveClick()
        {
            GameManager.Instance.RequestRemoveTower(_selectedTX, _selectedTY);
            TowerOptionPanel?.SetActive(false);
        }

        // ── 聊天 ─────────────────────────────────────────────────

        private void OnChatSend()
        {
            string text = ChatInput?.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(text)) return;
            GameManager.Instance.SendChat(text);
            if (ChatInput != null) ChatInput.text = "";
        }

        public void AddChatMessage(string from, string text)
        {
            if (ChatLog == null) return;
            ChatLog.text += $"\n<color=#aaffaa>[{from}]</color> {text}";
            // 限制行数
            var lines = ChatLog.text.Split('\n');
            if (lines.Length > 20)
                ChatLog.text = string.Join("\n", lines[^20..]);
        }

        // ── 游戏结束 ─────────────────────────────────────────────

        public void ShowGameOver(bool win, string reason, int wave)
        {
            GameOverPanel?.SetActive(true);

            if (GameOverTitle != null)
            {
                GameOverTitle.text = win ? "胜利" : "失败";
                GameOverTitle.color = win
                    ? new Color(0.3f, 0.9f, 0.5f)
                    : new Color(0.9f, 0.3f, 0.3f);
            }

            if (GameOverDetail != null)
                GameOverDetail.text = reason;

            if (GameOverStatsText != null)
            {
                string modeStr = GameManager.Instance.GameMode == 1 ? "对抗模式" : "合作模式";
                string waveStr = GameManager.Instance.GameMode == 1
                    ? ""
                    : $"坚守到第 {wave} 波\n";
                GameOverStatsText.text = $"{modeStr}\n{waveStr}剩余金币  ¥{GameManager.Instance.LocalGold}";
            }
        }

        // ── 消息/错误 ────────────────────────────────────────────

        public void ShowMessage(string msg)
        {
            if (MessageText != null) MessageText.text = msg;
        }

        public void ShowError(string msg)
        {
            if (ErrorText == null) return;
            ErrorText.text = msg;
            ErrorText.gameObject.SetActive(true);
            StartCoroutine(ClearErrorAfter(3f));
        }

        private IEnumerator ClearMessageAfter(float sec)
        {
            yield return new WaitForSeconds(sec);
            if (MessageText != null) MessageText.text = "";
        }

        private IEnumerator ClearErrorAfter(float sec)
        {
            yield return new WaitForSeconds(sec);
            if (ErrorText != null) ErrorText.gameObject.SetActive(false);
        }
    }
}
