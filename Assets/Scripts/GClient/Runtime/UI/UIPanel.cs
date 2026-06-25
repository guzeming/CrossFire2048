using System;
using System.Threading;
using System.Threading.Tasks;
using CrossFire2048.Client.Common;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CrossFire2048.Client.UI
{
    /// <summary>
    /// 所有 UI 面板的基类。子类重写 OnOpen / OnClose 处理显示逻辑。
    /// 通过 AddButton / AddEvent / AddGameEvent / AddTimer / AddAsync 注册的资源会在面板关闭时自动释放。
    /// </summary>
    public abstract class UIPanel : MonoBehaviour
    {
        [SerializeField] private string panelId;
        [SerializeField] private UILayer layer = UILayer.Normal;
        [SerializeField] private bool isModal;

        private PanelLifetime _lifetime;

        public string PanelId => string.IsNullOrWhiteSpace(panelId) ? GetType().Name : panelId;
        public UILayer Layer => layer;
        /// <summary>为 true 时打开该面板会在同层显示全屏遮罩并拦截下层输入。Popup 层默认视为 Modal。</summary>
        public bool IsModal => isModal || layer == UILayer.Popup;
        public bool IsOpen { get; private set; }

        internal void OpenInternal(object args)
        {
            if (IsOpen)
            {
                Refresh(args);
                return;
            }

            IsOpen = true;
            _lifetime = new PanelLifetime(this);
            gameObject.SetActive(true);
            OnOpen(args);
        }

        internal void CloseInternal()
        {
            if (!IsOpen)
            {
                return;
            }

            OnClose();
            _lifetime?.Dispose();
            _lifetime = null;
            IsOpen = false;
            gameObject.SetActive(false);
        }

        /// <summary>面板被打开时调用，args 为 Open 传入的参数。</summary>
        protected virtual void OnOpen(object args)
        {
        }

        /// <summary>面板再次打开且已处于显示状态时调用。</summary>
        protected virtual void Refresh(object args)
        {
        }

        /// <summary>面板被关闭时调用。</summary>
        protected virtual void OnClose()
        {
        }

        protected void AddButton(Button button, UnityAction callback)
        {
            _lifetime?.AddButton(button, callback);
        }

        protected void AddEvent(Action<Action> subscribe, Action<Action> unsubscribe, Action handler)
        {
            _lifetime?.AddEvent(subscribe, unsubscribe, handler);
        }

        protected void AddEvent<T>(Action<Action<T>> subscribe, Action<Action<T>> unsubscribe, Action<T> handler)
        {
            _lifetime?.AddEvent(subscribe, unsubscribe, handler);
        }

        protected void AddGameEvent(GameEventId id, Action handler)
        {
            _lifetime?.AddGameEvent(id, handler);
        }

        protected void AddGameEvent<T>(GameEventId id, Action<T> handler)
        {
            _lifetime?.AddGameEvent(id, handler);
        }

        protected void AddTimer(float delaySeconds, Action callback)
        {
            _lifetime?.AddTimer(delaySeconds, callback);
        }

        protected void AddIntervalTimer(float intervalSeconds, Action callback)
        {
            _lifetime?.AddIntervalTimer(intervalSeconds, callback);
        }

        protected void AddAsync(Func<Task> taskFunc)
        {
            _lifetime?.RunAsync(taskFunc);
        }

        protected void AddAsync(Func<CancellationToken, Task> taskFunc)
        {
            _lifetime?.RunAsync(taskFunc);
        }
    }
}
