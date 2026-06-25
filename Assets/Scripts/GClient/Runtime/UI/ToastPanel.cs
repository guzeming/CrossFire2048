using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace CrossFire2048.Client.UI
{
    /// <summary>
    /// Overlay 层轻提示，不参与栈管理。通过 UIManager.ShowToast 调用。
    /// </summary>
    public sealed class ToastPanel : UIPanel
    {
        [SerializeField] private Text messageText;

        private Coroutine _hideCoroutine;

        /// <summary>显示提示并在指定秒数后自动隐藏。</summary>
        public void Show(string message, float duration = 2f)
        {
            if (messageText != null)
            {
                messageText.text = message ?? string.Empty;
            }

            gameObject.SetActive(true);

            if (_hideCoroutine != null)
            {
                StopCoroutine(_hideCoroutine);
            }

            _hideCoroutine = StartCoroutine(HideAfter(duration));
        }

        protected override void OnOpen(object args)
        {
            if (args is ToastOpenArgs toastArgs)
            {
                Show(toastArgs.Message, toastArgs.Duration);
                return;
            }

            if (args is string message)
            {
                Show(message);
            }
        }

        protected override void Refresh(object args)
        {
            OnOpen(args);
        }

        private IEnumerator HideAfter(float duration)
        {
            if (duration <= 0f)
            {
                yield break;
            }

            yield return new WaitForSeconds(duration);
            gameObject.SetActive(false);
            _hideCoroutine = null;
        }
    }
}
