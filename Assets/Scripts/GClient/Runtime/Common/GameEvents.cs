using System;
using System.Collections.Generic;
using UnityEngine;

namespace CrossFire2048.Client.Common
{
    /// <summary>
    /// 轻量全局事件总线。用于模块间、UI 间的解耦通信。
    /// 面板内订阅请优先使用 UIPanel.AddGameEvent，以便面板关闭时自动取消订阅。
    /// </summary>
    public static class GameEvents
    {
        private static readonly Dictionary<GameEventId, List<Delegate>> Handlers =
            new Dictionary<GameEventId, List<Delegate>>();

        public static void Subscribe(GameEventId id, Action handler)
        {
            SubscribeInternal(id, handler);
        }

        public static void Subscribe<T>(GameEventId id, Action<T> handler)
        {
            SubscribeInternal(id, handler);
        }

        public static void Unsubscribe(GameEventId id, Action handler)
        {
            UnsubscribeInternal(id, handler);
        }

        public static void Unsubscribe<T>(GameEventId id, Action<T> handler)
        {
            UnsubscribeInternal(id, handler);
        }

        public static void Publish(GameEventId id)
        {
            if (!Handlers.TryGetValue(id, out List<Delegate> list))
            {
                return;
            }

            Delegate[] snapshot = list.ToArray();
            foreach (Delegate handler in snapshot)
            {
                try
                {
                    (handler as Action)?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        public static void Publish<T>(GameEventId id, T payload)
        {
            if (!Handlers.TryGetValue(id, out List<Delegate> list))
            {
                return;
            }

            Delegate[] snapshot = list.ToArray();
            foreach (Delegate handler in snapshot)
            {
                try
                {
                    (handler as Action<T>)?.Invoke(payload);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        /// <summary>主要用于测试或切场景时清理。</summary>
        public static void ClearAll()
        {
            Handlers.Clear();
        }

        private static void SubscribeInternal(GameEventId id, Delegate handler)
        {
            if (handler == null)
            {
                return;
            }

            if (!Handlers.TryGetValue(id, out List<Delegate> list))
            {
                list = new List<Delegate>();
                Handlers[id] = list;
            }

            if (!list.Contains(handler))
            {
                list.Add(handler);
            }
        }

        private static void UnsubscribeInternal(GameEventId id, Delegate handler)
        {
            if (handler == null)
            {
                return;
            }

            if (!Handlers.TryGetValue(id, out List<Delegate> list))
            {
                return;
            }

            list.Remove(handler);

            if (list.Count == 0)
            {
                Handlers.Remove(id);
            }
        }
    }
}
