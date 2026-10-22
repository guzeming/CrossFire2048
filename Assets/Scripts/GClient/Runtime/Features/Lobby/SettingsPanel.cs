using System.Collections.Generic;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.UI;
using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Lobby
{
    public sealed class SettingsPanel : UIPanel
    {
        [SerializeField] private Toggle fullscreenToggle;
        [SerializeField] private Toggle performanceStatsToggle;
        [SerializeField] private SettingsResolutionDropdown resolutionDropdown;
        [SerializeField] private Button saveButton, backButton, saveAndLeaveButton, discardButton, cancelButton;
        [SerializeField] private Text statusText;
        [SerializeField] private CanvasGroup controls;
        [SerializeField] private GameObject unsavedPrompt;
        [SerializeField] private Button[] categoryButtons;
        [SerializeField] private GameObject[] categoryPages;
        private List<Vector2Int> resolutions;
        private DisplaySettings.Value original, draft;
        private bool quitAfterPrompt;
        public bool HasUnsavedChanges => !draft.Equals(original);

        protected override void OnOpen(object args)
        {
            original = draft = DisplaySettings.Current;
            resolutions = DisplaySettings.Resolutions(original);
            resolutionDropdown.ClearOptions();
            resolutionDropdown.AddOptions(resolutions.ConvertAll(size => $"{size.x} × {size.y}"));
            resolutionDropdown.SetValueWithoutNotify(resolutions.IndexOf(new Vector2Int(draft.width, draft.height)));
            fullscreenToggle.SetIsOnWithoutNotify(draft.fullscreen);
            fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
            performanceStatsToggle.SetIsOnWithoutNotify(draft.ShowPerformanceStats);
            performanceStatsToggle.onValueChanged.AddListener(OnPerformanceStatsChanged);
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
            Application.wantsToQuit += OnWantsToQuit;
            AddButton(saveButton, Save);
            AddButton(backButton, RequestClose);
            AddButton(saveAndLeaveButton, () => { Save(); Leave(); });
            AddButton(discardButton, Leave);
            AddButton(cancelButton, CancelPrompt);
            for (int i = 0; i < categoryButtons.Length; i++)
            {
                int index = i;
                AddButton(categoryButtons[i], () => SelectCategory(index));
            }
            SelectCategory(0);
            quitAfterPrompt = false;
            SetPrompt(false);
            RefreshStatus();
        }

        protected override void OnClose()
        {
            fullscreenToggle.onValueChanged.RemoveListener(OnFullscreenChanged);
            performanceStatsToggle.onValueChanged.RemoveListener(OnPerformanceStatsChanged);
            resolutionDropdown.onValueChanged.RemoveListener(OnResolutionChanged);
            Application.wantsToQuit -= OnWantsToQuit;
            resolutionDropdown.Hide();
            SetPrompt(false);
        }

        public override bool HandleBackInput()
        {
            if (resolutionDropdown.IsExpanded) resolutionDropdown.Hide();
            else if (unsavedPrompt.activeSelf) CancelPrompt();
            else RequestClose();
            return true;
        }

        private void OnFullscreenChanged(bool value) { draft.fullscreen = value; RefreshStatus(); }
        private void OnPerformanceStatsChanged(bool value) { draft.ShowPerformanceStats = value; RefreshStatus(); }
        private void SelectCategory(int index)
        {
            resolutionDropdown.Hide();
            for (int i = 0; i < categoryButtons.Length; i++)
            {
                bool selected = i == index;
                BlackGoldTheme.Select(categoryButtons[i], selected);
                categoryPages[i].SetActive(selected);
            }
        }
        private void OnResolutionChanged(int index)
        {
            if (index < 0 || index >= resolutions.Count) return;
            draft.width = resolutions[index].x; draft.height = resolutions[index].y; RefreshStatus();
        }
        private void RefreshStatus()
        {
            fullscreenToggle.GetComponentInChildren<Text>().text = draft.fullscreen ? "已开启" : "已关闭";
            performanceStatsToggle.GetComponentInChildren<Text>().text = draft.ShowPerformanceStats ? "已开启" : "已关闭";
            saveButton.interactable = HasUnsavedChanges;
            statusText.text = HasUnsavedChanges ? "有未保存的修改" : "";
        }
        private void Save()
        {
            DisplaySettings.Save(draft);
            original = draft;
            RefreshStatus();
            UIManager.Instance.ShowToast("显示设置已保存");
        }
        private void RequestClose()
        {
            if (HasUnsavedChanges) SetPrompt(true);
            else UIManager.Instance.Close(this);
        }
        private bool OnWantsToQuit()
        {
            if (!HasUnsavedChanges) return true;
            quitAfterPrompt = true;
            resolutionDropdown.Hide();
            SetPrompt(true);
            return false;
        }
        private void CancelPrompt() { quitAfterPrompt = false; SetPrompt(false); }
        private void Leave()
        {
            bool quit = quitAfterPrompt;
            UIManager.Instance.Close(this);
            if (quit) GameSceneFlow.QuitGame();
        }
        private void SetPrompt(bool visible)
        {
            unsavedPrompt.SetActive(visible);
            controls.interactable = controls.blocksRaycasts = !visible;
            controls.alpha = visible ? 0 : 1;
        }
    }
}
