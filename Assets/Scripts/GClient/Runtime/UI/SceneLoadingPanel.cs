using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OperationBlacktide.Client.UI
{
    /// <summary>跨场景加载遮罩。进度只由场景流程更新，不按时间虚增。</summary>
    public sealed class SceneLoadingPanel : UIPanel
    {
        [SerializeField] private RawImage background;
        [SerializeField] private Texture lobbyBackground;
        [SerializeField] private Texture mapBackground;
        [SerializeField] private Text destination;
        [SerializeField] private Text status;
        [SerializeField] private Text percentage;
        [SerializeField] private RectTransform progressFill;

        private EventSystem inputSystem;
        private bool previousNavigation;
        public float Progress { get; private set; }

        protected override void OnOpen(object args)
        {
            inputSystem = EventSystem.current;
            if (inputSystem != null)
            {
                previousNavigation = inputSystem.sendNavigationEvents;
                inputSystem.SetSelectedGameObject(null);
                inputSystem.sendNavigationEvents = false;
            }
        }

        public void SetDestination(string title, bool isMap)
        {
            destination.text = title;
            background.texture = isMap ? mapBackground : lobbyBackground;
            Progress = 0;
            SetProgress(0, "正在加载场景");
            FitBackground();
        }

        public void SetProgress(float value, string message)
        {
            Progress = Mathf.Max(Progress, Mathf.Clamp01(value));
            progressFill.anchorMax = new Vector2(Progress, 1);
            progressFill.gameObject.SetActive(Progress > 0);
            percentage.text = Mathf.FloorToInt(Progress * 100) + "%";
            status.text = message;
        }

        private void LateUpdate()
        {
            // 新场景的默认选中项也不能在遮罩后接受 Submit/方向键。
            if (inputSystem != null && inputSystem.currentSelectedGameObject != null)
                inputSystem.SetSelectedGameObject(null);
            FitBackground();
        }

        private void FitBackground()
        {
            if (background.texture == null) return;
            Rect bounds = background.rectTransform.rect;
            float aspect = bounds.width / Mathf.Max(1, bounds.height);
            float imageAspect = (float)background.texture.width / background.texture.height;
            float width = Mathf.Min(1, aspect / imageAspect);
            float height = Mathf.Min(1, imageAspect / aspect);
            background.uvRect = new Rect((1 - width) * .5f, (1 - height) * .5f, width, height);
        }

        protected override void OnClose() => RestoreNavigation();
        private void OnDestroy() => RestoreNavigation();
        private void RestoreNavigation()
        {
            if (inputSystem != null) inputSystem.sendNavigationEvents = previousNavigation;
            inputSystem = null;
        }
        public override bool HandleBackInput() => true;
    }
}
