using UnityEngine;
using UnityEngine.UI;

namespace CrossFire2048.Client.UI
{
    /// <summary>
    /// 全屏半透明遮罩，拦截射线，防止点击穿透到下层 UI。
    /// 由 UIManager 在 Popup 等层的栈顶为 Modal 面板时自动显示。
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class UIModalBlocker : MonoBehaviour
    {
        private static readonly Color DefaultColor = new Color(0f, 0f, 0f, 0.55f);

        [SerializeField] private Image image;

        public static UIModalBlocker Create(Transform layerRoot, Color? tint = null)
        {
            GameObject blockerObject = new GameObject("ModalBlocker", typeof(RectTransform));
            blockerObject.transform.SetParent(layerRoot, false);

            RectTransform rect = blockerObject.GetComponent<RectTransform>();
            StretchFullScreen(rect);

            Image blockerImage = blockerObject.AddComponent<Image>();
            blockerImage.color = tint ?? DefaultColor;
            blockerImage.raycastTarget = true;

            UIModalBlocker blocker = blockerObject.AddComponent<UIModalBlocker>();
            blocker.image = blockerImage;
            blockerObject.SetActive(false);
            return blocker;
        }

        public void ShowAbove(UIPanel panel)
        {
            if (panel == null)
            {
                Hide();
                return;
            }

            Transform layerRoot = panel.transform.parent;
            if (layerRoot != null && transform.parent != layerRoot)
            {
                transform.SetParent(layerRoot, false);
            }

            StretchFullScreen(transform as RectTransform);
            gameObject.SetActive(true);

            int panelIndex = panel.transform.GetSiblingIndex();
            transform.SetSiblingIndex(panelIndex);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private static void StretchFullScreen(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localPosition = Vector3.zero;
        }
    }
}
