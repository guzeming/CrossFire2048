using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrossFire2048.Client.Common;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CrossFire2048.Client.UI
{
    /// <summary>
    /// 面板生命周期管理器。由 UIPanel 内部持有，不对外暴露。
    /// </summary>
    internal sealed class PanelLifetime
    {
        private readonly MonoBehaviour _host;
        private readonly List<Action> _cleanups = new List<Action>();
        private readonly List<Coroutine> _coroutines = new List<Coroutine>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public PanelLifetime(MonoBehaviour host)
        {
            _host = host;
        }

        public CancellationToken Token => _cts.Token;

        public void AddButton(Button button, UnityAction callback)
        {
            if (button == null || callback == null)
            {
                return;
            }

            button.onClick.AddListener(callback);
            AddCleanup(() => button.onClick.RemoveListener(callback));
        }

        public void AddEvent(Action<Action> subscribe, Action<Action> unsubscribe, Action handler)
        {
            if (subscribe == null || unsubscribe == null || handler == null)
            {
                return;
            }

            subscribe(handler);
            AddCleanup(() => unsubscribe(handler));
        }

        public void AddEvent<T>(Action<Action<T>> subscribe, Action<Action<T>> unsubscribe, Action<T> handler)
        {
            if (subscribe == null || unsubscribe == null || handler == null)
            {
                return;
            }

            subscribe(handler);
            AddCleanup(() => unsubscribe(handler));
        }

        public void AddGameEvent(GameEventId id, Action handler)
        {
            if (handler == null)
            {
                return;
            }

            GameEvents.Subscribe(id, handler);
            AddCleanup(() => GameEvents.Unsubscribe(id, handler));
        }

        public void AddGameEvent<T>(GameEventId id, Action<T> handler)
        {
            if (handler == null)
            {
                return;
            }

            GameEvents.Subscribe(id, handler);
            AddCleanup(() => GameEvents.Unsubscribe(id, handler));
        }

        public void AddTimer(float delaySeconds, Action callback)
        {
            if (callback == null || delaySeconds < 0f)
            {
                return;
            }

            Coroutine coroutine = _host.StartCoroutine(TimerCoroutine(delaySeconds, callback));
            _coroutines.Add(coroutine);
        }

        public void AddIntervalTimer(float intervalSeconds, Action callback)
        {
            if (callback == null || intervalSeconds <= 0f)
            {
                return;
            }

            Coroutine coroutine = _host.StartCoroutine(IntervalTimerCoroutine(intervalSeconds, callback));
            _coroutines.Add(coroutine);
        }

        public void RunAsync(Func<Task> taskFunc)
        {
            if (taskFunc == null)
            {
                return;
            }

            RunAsyncInternal(taskFunc);
        }

        public void RunAsync(Func<CancellationToken, Task> taskFunc)
        {
            if (taskFunc == null)
            {
                return;
            }

            RunAsyncInternal(() => taskFunc(_cts.Token));
        }

        public void Dispose()
        {
            if (_cts.IsCancellationRequested == false)
            {
                _cts.Cancel();
            }

            for (int i = _coroutines.Count - 1; i >= 0; i--)
            {
                if (_coroutines[i] != null && _host != null)
                {
                    _host.StopCoroutine(_coroutines[i]);
                }
            }

            _coroutines.Clear();

            for (int i = _cleanups.Count - 1; i >= 0; i--)
            {
                try
                {
                    _cleanups[i]?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            _cleanups.Clear();
            _cts.Dispose();
        }

        private void AddCleanup(Action cleanup)
        {
            if (cleanup != null)
            {
                _cleanups.Add(cleanup);
            }
        }

        private IEnumerator TimerCoroutine(float delaySeconds, Action callback)
        {
            yield return new WaitForSeconds(delaySeconds);

            if (_cts.IsCancellationRequested)
            {
                yield break;
            }

            callback?.Invoke();
        }

        private IEnumerator IntervalTimerCoroutine(float intervalSeconds, Action callback)
        {
            WaitForSeconds wait = new WaitForSeconds(intervalSeconds);

            while (!_cts.IsCancellationRequested)
            {
                yield return wait;

                if (_cts.IsCancellationRequested)
                {
                    yield break;
                }

                callback?.Invoke();
            }
        }

        private async void RunAsyncInternal(Func<Task> taskFunc)
        {
            try
            {
                await taskFunc();
            }
            catch (OperationCanceledException)
            {
                // 面板关闭时取消，属于正常情况。
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
