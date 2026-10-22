using OperationBlacktide.Client.App;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Client.Features.Account;
using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Lobby
{
    /// <summary>
    /// 3D 大厅上的导航与操作 UI；角色和环境属于 LobbyScene。
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Account", "Assembly-CSharp", "LobbyPanel")]
    public sealed class LobbyPanel : UIPanel
    {
        [SerializeField] private Text welcomeText;
        [SerializeField] private Button logoutButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private AuthClient authClient;
        [SerializeField] private Button[] tabButtons;
        [SerializeField] private Text[] tabLabels;
        [SerializeField] private GameObject[] tabIndicators;
        [SerializeField] private GameObject[] tabPages;
        [SerializeField] private Button modeButton;
        [SerializeField] private Button enterGameButton;
        [SerializeField] private GameObject modePicker;
        [SerializeField] private Button dismissModePickerButton;
        [SerializeField] private Button[] modeOptions;
        [SerializeField] private GameObject[] modeIndicators;
        [SerializeField] private Text modeNameText;
        [SerializeField] private LobbyLoadoutView loadoutView;

        private const int TrainingModeIndex = 2;
        // Keep existing equipment/career indices stable for authored references.
        public const int AgentTabIndex = 3;
        private static readonly string[] ModeNames = { "爆破模式", "联机测试场", "训练场" };
        private static readonly Color Accent = BlackGoldTheme.Gold;
        private static readonly Color Muted = BlackGoldTheme.Muted;

        public int SelectedTabIndex { get; private set; }
        public int SelectedModeIndex { get; private set; }

        protected override void OnOpen(object args)
        {
            LobbyVoiceAssistant.Attach(this);
            if (authClient == null)
            {
                authClient = GameSceneFlow.Instance != null
                    ? GameSceneFlow.Instance.Auth : FindObjectOfType<AuthClient>();
            }

            if (welcomeText != null && authClient != null && authClient.Session.IsLoggedIn)
            {
                welcomeText.text = authClient.Session.Username;
            }

            if (logoutButton != null)
            {
                AddButton(logoutButton, OnLogoutClicked);
            }

            for (int i = 0; i < tabButtons.Length; i++)
            {
                int index = i;
                AddButton(tabButtons[i], () => SelectTab(index));
            }
            for (int i = 0; i < modeOptions.Length; i++)
            {
                int index = i;
                AddButton(modeOptions[i], () => OnModeClicked(index));
                var label = modeOptions[i].GetComponentInChildren<Text>(true);
                if (label != null) label.text = ModeNames[i];
            }
            AddButton(modeButton, () => modePicker.SetActive(!modePicker.activeSelf));
            AddButton(dismissModePickerButton, () => modePicker.SetActive(false));
            AddButton(enterGameButton, OnEnterGameClicked);
            AddButton(settingsButton, () => OpenPopup(UIPanelId.Settings));
            var root = UIRoot.Instance;
            if (root != null) AddEvent(h => root.UnhandledBackRequested += h, h => root.UnhandledBackRequested -= h, OnBackRequested);
            loadoutView?.Open(authClient != null ? authClient.Session.UserId : null);
            SelectTab(0);
            SelectMode(SelectedModeIndex);
        }

        protected override void OnClose()
        {
            loadoutView?.Close();
            if (modePicker != null) modePicker.SetActive(false);
        }

        private void OnBackRequested()
        {
            if (!IsOpen) return;
            if (modePicker != null && modePicker.activeSelf) modePicker.SetActive(false);
            else OpenPopup(UIPanelId.LobbyMenu);
        }

        private void OpenPopup(UIPanelId id)
        {
            if (GameSceneFlow.Instance != null && GameSceneFlow.Instance.IsLoading) return;
            if (modePicker != null) modePicker.SetActive(false);
            UIManager.Instance.Push(id);
        }

        private void SelectTab(int index)
        {
            SelectedTabIndex = index;
            for (int i = 0; i < tabButtons.Length; i++)
            {
                tabLabels[i].color = i == index ? Color.white : Muted;
                tabIndicators[i].SetActive(i == index);
                BlackGoldTheme.Select(tabButtons[i], i == index);
                tabPages[i].SetActive(i == index);
            }
            if (modePicker != null) modePicker.SetActive(false);
            loadoutView?.SetTab(index);
        }

        private void SelectMode(int index)
        {
            SelectedModeIndex = Mathf.Clamp(index, 0, ModeNames.Length - 1);
            modeNameText.text = ModeNames[SelectedModeIndex];
            for (int i = 0; i < modeIndicators.Length; i++)
                modeIndicators[i].SetActive(i == SelectedModeIndex);
            modeNameText.color = BlackGoldTheme.Text;
            modePicker.SetActive(false);
        }

        private void OnModeClicked(int index)
        {
            if (GameSceneFlow.Instance != null && GameSceneFlow.Instance.IsLoading) return;
            SelectMode(index);
            if (SelectedModeIndex == TrainingModeIndex)
                OnEnterGameClicked();
        }

        private void OnEnterGameClicked()
        {
            if (SelectedModeIndex == TrainingModeIndex)
            {
                GameSceneFlow.Instance?.EnterTrainingGround();
                return;
            }

            // 等房间/匹配业务接入后，将此入口交给对应业务服务。
            if (SelectedModeIndex == 1) { GameSceneFlow.Instance?.RequestBattleArena(); return; }
            OperationBlacktide.Client.Features.Demolition.DemolitionGateway.Get()?.Open();
        }

        private void OnLogoutClicked()
        {
            GameSceneFlow.Instance?.Logout();
        }
    }
}
