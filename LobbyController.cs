using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using MazeTD.Client.Network;
using MazeTD.Shared;

namespace MazeTD.Client
{
    /// <summary>
    /// Lobby 场景控制器
    ///
    /// 状态机：
    ///   Connect Panel  →  (连接+JoinAck)  →  Waiting Panel
    ///   Waiting Panel  →  (S2C_RoomState roomState=1)  →  Select Panel
    ///   Select Panel   →  (C2S_ReadyConfirm 双方都确认)  →  (S2C_GameStart) → GameScene
    /// </summary>
    public class LobbyController : MonoBehaviour
    {
        // ── 连接面板 ──────────────────────────────────────────────
        [Header("连接面板")]
        public GameObject ConnectPanel;
        public TMP_InputField IpInputField;
        public TMP_InputField PortInputField;
        public Button ConnectButton;
        public TMP_Text ConnectStatusText;

        // ── 等待面板 ──────────────────────────────────────────────
        [Header("等待面板")]
        public GameObject WaitingPanel;
        public TMP_Text WaitingStatusText;

        // ── 选择面板 ──────────────────────────────────────────────
        [Header("选择面板")]
        public GameObject SelectPanel;

        // 模式选择
        public Button BtnModeAlliance;     // 合作模式
        public Button BtnModeLegion;       // 对抗模式

        // Legion 角色选择（合作模式时隐藏）
        public GameObject RoleGroup;
        public Button BtnRoleDefender;     // role=0 防守方
        public Button BtnRoleAttacker;     // role=1 进攻方

        // 对方状态显示
        public TMP_Text Player0StatusText; // 显示 P0 选了什么
        public TMP_Text Player1StatusText; // 显示 P1 选了什么

        // 错误提示
        public TMP_Text SelectErrorText;

        // 确认/取消准备
        public Button BtnReady;
        public Button BtnCancelReady;

        // ── 内部状态 ──────────────────────────────────────────────
        private NetworkManager _net;
        private int _localPlayerId = -1;
        private int _selectedMode = 0;   // 0=Alliance 1=Legion
        private int _selectedRole = -1;  // -1=未选 0=防守 1=进攻
        private bool _isReady = false;

        // ── 生命周期 ──────────────────────────────────────────────

        private void Start()
        {
            _net = NetworkManager.Instance;

            // 重置本地状态
            _localPlayerId = -1;
            _selectedMode = 0;
            _selectedRole = -1;
            _isReady = false;

            if (IpInputField != null) IpInputField.text = "127.0.0.1";
            if (PortInputField != null) PortInputField.text = "7777";

            ConnectButton?.onClick.AddListener(OnConnectClicked);
            ConnectButton.interactable = true;  // 确保按钮可点击

            BtnModeAlliance?.onClick.AddListener(() => SelectMode(0));
            BtnModeLegion?.onClick.AddListener(() => SelectMode(1));
            BtnRoleDefender?.onClick.AddListener(() => SelectRole(0));
            BtnRoleAttacker?.onClick.AddListener(() => SelectRole(1));
            BtnReady?.onClick.AddListener(OnReadyClicked);
            BtnCancelReady?.onClick.AddListener(OnCancelReadyClicked);

            // 重置面板显示
            ShowPanel(ConnectPanel);
            RefreshModeButtons();
            RefreshRoleButtons();
            RefreshReadyButtons();

            if (RoleGroup != null) RoleGroup.SetActive(false);

            _net.OnPacketReceived += OnPacket;
        }

        private void OnDestroy()
        {
            if (_net != null) _net.OnPacketReceived -= OnPacket;
        }

        // ── 连接 ──────────────────────────────────────────────────

        private void OnConnectClicked()
        {
            string ip = IpInputField?.text ?? "127.0.0.1";
            int port = int.TryParse(PortInputField?.text, out int p) ? p : 7777;

            SetConnectStatus("连接中...", Color.yellow);
            ConnectButton.interactable = false;
            _net.Connect(ip, port);
        }

        // ── 包处理 ────────────────────────────────────────────────

        private void OnPacket(PacketType type, byte[] payload)
        {
            switch (type)
            {
                case PacketType.S2C_JoinAck:
                    HandleJoinAck(payload);
                    break;

                case PacketType.S2C_RoomState:
                    HandleRoomState(payload);
                    break;

                case PacketType.S2C_Error:
                    HandleError(payload);
                    break;

                case PacketType.S2C_GameStart:
                    _net.OnPacketReceived -= OnPacket;
                    _net.CacheGamePackets = true;
                    SceneManager.LoadScene("GameScene");
                    break;
            }
        }

        private void HandleJoinAck(byte[] payload)
        {
            var ack = PacketHelper.Deserialize<S2C_JoinAckPayload>(payload);
            if (ack.success)
            {
                _net.LocalPlayerId = ack.playerId;  // 加这行
                ShowPanel(WaitingPanel);
                SetWaitingStatus("已加入房间，等待对方玩家...", Color.green);
            }
            else
            {
                SetConnectStatus($"加入失败：{ack.reason}", Color.red);
                ConnectButton.interactable = true;
            }
        }

        private void HandleRoomState(byte[] payload)
        {
            var state = PacketHelper.Deserialize<S2C_RoomStatePayload>(payload);

            if (state.roomState == 0)
            {
                ShowPanel(WaitingPanel);
                SetWaitingStatus("已加入房间，等待对方玩家...", Color.green);
            }
            else if (state.roomState == 1)
            {
                ShowPanel(SelectPanel);
                RefreshSelectPanel(state);
            }
            else if (state.roomState == 2)
            {
                // 保存本地玩家的角色和模式
                _net.LocalMode = _selectedMode;
                _net.LocalRole = _selectedRole;
                _net.OnPacketReceived -= OnPacket;
                SceneManager.LoadScene("GameScene");
            }
        }

        private void HandleError(byte[] payload)
        {
            var err = PacketHelper.Deserialize<S2C_ErrorPayload>(payload);

            // 错误码 3/4/5/7 都是选角阶段的错误，显示在 SelectPanel 上
            if (SelectErrorText != null)
            {
                SelectErrorText.text = err.msg;
                SelectErrorText.color = Color.red;
            }

            // 如果是 ready 被打回，重置准备状态
            if (err.code == 4 || err.code == 5 || err.code == 7)
            {
                _isReady = false;
                RefreshReadyButtons();
            }
        }

        // ── 选择面板逻辑 ──────────────────────────────────────────

        private void SelectMode(int mode)
        {
            _selectedMode = mode;
            _selectedRole = -1;   // 切换模式时清空角色
            ClearError();

            // Legion 才显示角色选择组
            if (RoleGroup != null)
                RoleGroup.SetActive(mode == 1);

            // 通知服务端（只改模式，不 ready）
            _net.Send(PacketType.C2S_SelectRole,
                new C2S_SelectRolePayload { role = -1, mode = _selectedMode });

            RefreshModeButtons();
            RefreshReadyButtons();
        }

        private void SelectRole(int role)
        {
            _selectedRole = role;
            ClearError();

            _net.Send(PacketType.C2S_SelectRole,
                new C2S_SelectRolePayload { role = _selectedRole, mode = _selectedMode });

            RefreshRoleButtons();
            RefreshReadyButtons();
        }

        private void OnReadyClicked()
        {
            // 合作模式无需选角色；Legion 必须选角色
            if (_selectedMode == 1 && _selectedRole < 0)
            {
                if (SelectErrorText != null)
                {
                    SelectErrorText.text = "Legion 模式请先选择角色";
                    SelectErrorText.color = Color.red;
                }
                return;
            }

            _isReady = true;
            ClearError();
            RefreshReadyButtons();

            _net.Send(PacketType.C2S_ReadyConfirm,
                new C2S_SelectRolePayload { role = _selectedRole, mode = _selectedMode });
        }

        private void OnCancelReadyClicked()
        {
            _isReady = false;
            RefreshReadyButtons();
            _net.Send(PacketType.C2S_CancelReady);
        }

        // ── 刷新 UI ───────────────────────────────────────────────

        private void RefreshSelectPanel(S2C_RoomStatePayload state)
        {
            foreach (var slot in state.players)
            {
                string modeStr = slot.mode == 1 ? "对抗" : "合作";
                string roleStr = slot.mode == 1
                    ? (slot.role == 0 ? "防守方" : slot.role == 1 ? "进攻方" : "未选角色")
                    : "—";
                string readyStr = slot.ready ? "  [已准备]" : "";
                string line = $"P{slot.playerId}  {modeStr}  {roleStr}{readyStr}";

                if (slot.playerId == 0 && Player0StatusText != null)
                    Player0StatusText.text = line;
                if (slot.playerId == 1 && Player1StatusText != null)
                    Player1StatusText.text = line;
            }

            // 角色互斥：对方选了哪个角色，本地就禁用那个按钮
            if (_selectedMode == 1)
            {
                int opponentRole = -1;
                foreach (var slot in state.players)
                {
                    if (slot.playerId != _localPlayerId)
                        opponentRole = slot.role;
                }

                if (BtnRoleDefender != null)
                    BtnRoleDefender.interactable = (opponentRole != 0);
                if (BtnRoleAttacker != null)
                    BtnRoleAttacker.interactable = (opponentRole != 1);
            }
            else
            {
                // 合作模式两个角色按钮都不可用
                if (BtnRoleDefender != null) BtnRoleDefender.interactable = true;
                if (BtnRoleAttacker != null) BtnRoleAttacker.interactable = true;
            }
        }

        private void RefreshModeButtons()
        {
            SetButtonHighlight(BtnModeAlliance, _selectedMode == 0);
            SetButtonHighlight(BtnModeLegion, _selectedMode == 1);
        }

        private void RefreshRoleButtons()
        {
            SetButtonHighlight(BtnRoleDefender, _selectedRole == 0);
            SetButtonHighlight(BtnRoleAttacker, _selectedRole == 1);
        }

        private void RefreshReadyButtons()
        {
            if (BtnReady != null) BtnReady.gameObject.SetActive(!_isReady);
            if (BtnCancelReady != null) BtnCancelReady.gameObject.SetActive(_isReady);
        }

        // ── 工具方法 ──────────────────────────────────────────────

        private void ShowPanel(GameObject target)
        {
            ConnectPanel?.SetActive(ConnectPanel == target);
            WaitingPanel?.SetActive(WaitingPanel == target);
            SelectPanel?.SetActive(SelectPanel == target);
        }

        private void SetConnectStatus(string msg, Color color)
        {
            if (ConnectStatusText == null) return;
            ConnectStatusText.text = msg;
            ConnectStatusText.color = color;
        }

        private void SetWaitingStatus(string msg, Color color)
        {
            if (WaitingStatusText == null) return;
            WaitingStatusText.text = msg;
            WaitingStatusText.color = color;
        }

        private void ClearError()
        {
            if (SelectErrorText != null) SelectErrorText.text = "";
        }

        private void SetButtonHighlight(Button btn, bool active)
        {
            if (btn == null) return;
            var colors = btn.colors;
            colors.normalColor = active
                ? new Color(0.2f, 0.7f, 0.5f, 1f)   // 选中：绿色
                : new Color(0.9f, 0.9f, 0.9f, 1f);   // 未选：默认
            btn.colors = colors;
        }
    }
}