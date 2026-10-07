using OperationBlacktide.Client.App;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Client.Features.Lobby;
using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Account
{
    /// <summary>
    /// 3D 大厅上的导航与操作 UI；角色和环境属于 LobbyScene。
    /// </summary>
    public sealed class LobbyPanel : UIPanel
    {
        [SerializeField] private Text welcomeText;
        [SerializeField] private Button logoutButton;
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
        private static readonly string[] ModeNames = { "爆破模式", "团队竞技", "训练场" };
        private static readonly Color Accent = new Color32(255, 164, 54, 255);
        private static readonly Color Muted = new Color32(163, 174, 180, 255);

        public int SelectedTabIndex { get; private set; }
        public int SelectedModeIndex { get; private set; }

        protected override void OnOpen(object args)
        {
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
            }
            AddButton(modeButton, () => modePicker.SetActive(!modePicker.activeSelf));
            AddButton(dismissModePickerButton, () => modePicker.SetActive(false));
            AddButton(enterGameButton, OnEnterGameClicked);
            loadoutView?.Open(authClient != null ? authClient.Session.UserId : null);
            SelectTab(0);
            SelectMode(SelectedModeIndex);
        }

        protected override void OnClose()
        {
            loadoutView?.Close();
            if (modePicker != null) modePicker.SetActive(false);
        }

        private void Update()
        {
            if (IsOpen && modePicker != null && modePicker.activeSelf && Input.GetKeyDown(KeyCode.Escape))
                modePicker.SetActive(false);
        }

        private void SelectTab(int index)
        {
            SelectedTabIndex = index;
            for (int i = 0; i < tabButtons.Length; i++)
            {
                tabLabels[i].color = i == index ? Color.white : Muted;
                tabIndicators[i].SetActive(i == index);
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
            modeNameText.color = Accent;
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
            UIManager.Instance?.ShowToast($"{ModeNames[SelectedModeIndex]}暂未开放");
        }

        private void OnLogoutClicked()
        {
            GameSceneFlow.Instance?.Logout();
        }
    }
}
