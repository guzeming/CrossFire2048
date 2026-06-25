using System.Collections.Generic;
using UnityEngine;

namespace CrossFire2048.Client.UI
{
    /// <summary>
    /// UI 管理器。按 UILayer 使用栈管理面板：Push 入栈并显示，Pop/Back 出栈并恢复上一层。
    /// </summary>
    public sealed class UIManager : MonoBehaviour
    {
        [SerializeField] private UIPanelEntry[] panelEntries;

        private readonly Dictionary<string, UIPanelEntry> _entryMap = new Dictionary<string, UIPanelEntry>();
        private readonly Dictionary<string, UIPanel> _instances = new Dictionary<string, UIPanel>();
        private readonly Dictionary<UILayer, Transform> _layerRoots = new Dictionary<UILayer, Transform>();
        private readonly Dictionary<UILayer, Stack<string>> _layerStacks = new Dictionary<UILayer, Stack<string>>();

        private ToastPanel _toastPanel;
        private readonly Dictionary<UILayer, UIModalBlocker> _modalBlockers = new Dictionary<UILayer, UIModalBlocker>();

        public static UIManager Instance { get; private set; }

        private void Awake()
        {
            UIRoot uiRoot = GetComponent<UIRoot>();
            if (uiRoot != null && UIRoot.Instance != null && UIRoot.Instance != uiRoot)
            {
                return;
            }

            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[UIManager] 场景中存在多个 UIManager，销毁重复实例。");
                Destroy(this);
                return;
            }

            Instance = this;
            BuildEntryMap();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>由 UIRoot 在层级节点创建完成后调用。</summary>
        public void Initialize(Dictionary<UILayer, Transform> layerRoots)
        {
            _layerRoots.Clear();

            foreach (KeyValuePair<UILayer, Transform> pair in layerRoots)
            {
                _layerRoots[pair.Key] = pair.Value;
            }
        }

        /// <summary>入栈并打开面板。同层当前栈顶会被关闭隐藏。</summary>
        public UIPanel Push(PanelId panelId, object args = null)
        {
            if (PanelIds.IsOverlayOnly(panelId))
            {
                return PushOverlayPanel(panelId, args);
            }

            return Push(PanelIds.Key(panelId), args);
        }

        public T Push<T>(PanelId panelId, object args = null) where T : UIPanel
        {
            return Push(panelId, args) as T;
        }

        /// <summary>入栈并打开面板。同层当前栈顶会被关闭隐藏。</summary>
        public UIPanel Push(string panelId, object args = null)
        {
            if (PanelIds.TryParse(panelId, out PanelId typedId) && PanelIds.IsOverlayOnly(typedId))
            {
                return PushOverlayPanel(typedId, args);
            }

            if (string.IsNullOrWhiteSpace(panelId))
            {
                Debug.LogError("[UIManager] panelId 不能为空。");
                return null;
            }

            if (!_entryMap.TryGetValue(panelId, out UIPanelEntry entry))
            {
                Debug.LogError($"[UIManager] 未注册面板：{panelId}");
                return null;
            }

            UILayer layer = entry.Layer;
            Stack<string> stack = GetStack(layer);

            if (stack.Count > 0 && stack.Peek() == panelId)
            {
                UIPanel currentTop = GetOrCreatePanel(panelId, entry);
                currentTop.OpenInternal(args);
                BringToFront(currentTop);
                SyncModalBlocker(layer);
                return currentTop;
            }

            if (IsInStack(stack, panelId))
            {
                PopUntilTopIs(panelId, layer);
                UIPanel existing = GetOrCreatePanel(panelId, entry);
                existing.OpenInternal(args);
                BringToFront(existing);
                SyncModalBlocker(layer);
                return existing;
            }

            if (stack.Count > 0)
            {
                ClosePanelInstance(stack.Peek());
            }

            stack.Push(panelId);
            UIPanel panel = GetOrCreatePanel(panelId, entry);
            panel.OpenInternal(args);
            BringToFront(panel);
            SyncModalBlocker(layer);
            return panel;
        }

        public T Push<T>(string panelId, object args = null) where T : UIPanel
        {
            return Push(panelId, args) as T;
        }

        /// <summary>兼容旧接口，等同于 Push。</summary>
        public UIPanel Open(PanelId panelId, object args = null)
        {
            return Push(panelId, args);
        }

        public T Open<T>(PanelId panelId, object args = null) where T : UIPanel
        {
            return Push<T>(panelId, args);
        }

        /// <summary>兼容旧接口，等同于 Push。</summary>
        public UIPanel Open(string panelId, object args = null)
        {
            return Push(panelId, args);
        }

        public T Open<T>(string panelId, object args = null) where T : UIPanel
        {
            return Push<T>(panelId, args);
        }

        /// <summary>Overlay 层轻提示，不参与栈管理。</summary>
        public void ShowToast(string message, float duration = 2f)
        {
            EnsureToastPanel();
            if (_toastPanel == null)
            {
                return;
            }

            _toastPanel.Show(message, duration);
        }

        /// <summary>处理返回键：先 Pop Popup，再 Back Normal。</summary>
        public bool HandleBackInput()
        {
            if (GetStackCount(UILayer.Popup) > 0)
            {
                Pop(UILayer.Popup);
                return true;
            }

            if (GetStackCount(UILayer.Normal) > 1)
            {
                Back();
                return true;
            }

            return false;
        }

        /// <summary>弹出指定层栈顶面板，并恢复下一层面板。</summary>
        public void Pop(UILayer layer = UILayer.Normal)
        {
            Stack<string> stack = GetStack(layer);
            if (stack.Count == 0)
            {
                return;
            }

            string closingId = stack.Pop();
            ClosePanelInstance(closingId);
            RestoreStackTop(layer, stack);
            SyncModalBlocker(layer);
        }

        /// <summary>返回 Normal 层上一面板。</summary>
        public void Back()
        {
            Pop(UILayer.Normal);
        }

        public void Close(PanelId panelId)
        {
            Close(PanelIds.Key(panelId));
        }

        /// <summary>关闭指定面板。若为栈顶则 Pop，否则回退到该面板之前的栈状态。</summary>
        public void Close(string panelId)
        {
            if (string.IsNullOrWhiteSpace(panelId))
            {
                return;
            }

            if (!_entryMap.TryGetValue(panelId, out UIPanelEntry entry))
            {
                return;
            }

            UILayer layer = entry.Layer;
            Stack<string> stack = GetStack(layer);

            if (stack.Count == 0)
            {
                ClosePanelInstance(panelId);
                return;
            }

            if (stack.Peek() == panelId)
            {
                Pop(layer);
                return;
            }

            if (!IsInStack(stack, panelId))
            {
                ClosePanelInstance(panelId);
                return;
            }

            PopUntilTopIs(panelId, layer);

            if (stack.Count > 0 && stack.Peek() == panelId)
            {
                stack.Pop();
            }

            ClosePanelInstance(panelId);
            RestoreStackTop(layer, stack);
            SyncModalBlocker(layer);
        }

        public void Close(UIPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            Close(panel.PanelId);
        }

        /// <summary>回退到指定面板，关闭其上方所有面板。</summary>
        public UIPanel PopTo(PanelId panelId)
        {
            return PopTo(PanelIds.Key(panelId));
        }

        /// <summary>回退到指定面板，关闭其上方所有面板。</summary>
        public UIPanel PopTo(string panelId)
        {
            if (string.IsNullOrWhiteSpace(panelId) || !_entryMap.TryGetValue(panelId, out UIPanelEntry entry))
            {
                return null;
            }

            UILayer layer = entry.Layer;
            Stack<string> stack = GetStack(layer);

            if (!IsInStack(stack, panelId))
            {
                return null;
            }

            PopUntilTopIs(panelId, layer);

            if (stack.Count == 0 || stack.Peek() != panelId)
            {
                return null;
            }

            UIPanel panel = GetOrCreatePanel(panelId, entry);
            panel.OpenInternal(null);
            BringToFront(panel);
            SyncModalBlocker(layer);
            return panel;
        }

        public void CloseAll(UILayer layer)
        {
            Stack<string> stack = GetStack(layer);
            while (stack.Count > 0)
            {
                Pop(layer);
            }
        }

        public bool TryGetPanel(string panelId, out UIPanel panel)
        {
            return _instances.TryGetValue(panelId, out panel);
        }

        public T GetPanel<T>(PanelId panelId) where T : UIPanel
        {
            return GetPanel<T>(PanelIds.Key(panelId));
        }

        public T GetPanel<T>(string panelId) where T : UIPanel
        {
            _instances.TryGetValue(panelId, out UIPanel panel);
            return panel as T;
        }

        public bool TryGetTopPanelId(UILayer layer, out string panelId)
        {
            Stack<string> stack = GetStack(layer);
            if (stack.Count == 0)
            {
                panelId = string.Empty;
                return false;
            }

            panelId = stack.Peek();
            return true;
        }

        public int GetStackCount(UILayer layer)
        {
            return GetStack(layer).Count;
        }

        private Stack<string> GetStack(UILayer layer)
        {
            if (!_layerStacks.TryGetValue(layer, out Stack<string> stack))
            {
                stack = new Stack<string>();
                _layerStacks[layer] = stack;
            }

            return stack;
        }

        private UIPanel GetOrCreatePanel(string panelId, UIPanelEntry entry)
        {
            if (_instances.TryGetValue(panelId, out UIPanel existing))
            {
                return existing;
            }

            Transform parent = GetLayerRoot(entry.Layer);
            UIPanel panel = Instantiate(entry.Prefab, parent);
            panel.name = entry.Prefab.name;
            _instances[panelId] = panel;
            return panel;
        }

        private void ClosePanelInstance(string panelId)
        {
            if (!_instances.TryGetValue(panelId, out UIPanel panel) || panel == null)
            {
                return;
            }

            if (panel.IsOpen)
            {
                panel.CloseInternal();
            }
            else
            {
                panel.gameObject.SetActive(false);
            }
        }

        private void RestoreStackTop(UILayer layer, Stack<string> stack)
        {
            if (stack.Count == 0)
            {
                return;
            }

            string previousId = stack.Peek();
            if (!_entryMap.TryGetValue(previousId, out UIPanelEntry entry))
            {
                stack.Pop();
                RestoreStackTop(layer, stack);
                return;
            }

            UIPanel panel = GetOrCreatePanel(previousId, entry);
            panel.OpenInternal(null);
            BringToFront(panel);
        }

        private UIModalBlocker EnsureModalBlocker(UILayer layer)
        {
            if (_modalBlockers.TryGetValue(layer, out UIModalBlocker existing) && existing != null)
            {
                return existing;
            }

            Transform layerRoot = GetLayerRoot(layer);
            if (layerRoot == null)
            {
                return null;
            }

            UIModalBlocker blocker = UIModalBlocker.Create(layerRoot);
            _modalBlockers[layer] = blocker;
            return blocker;
        }

        private void SyncModalBlocker(UILayer layer)
        {
            UIModalBlocker blocker = EnsureModalBlocker(layer);
            if (blocker == null)
            {
                return;
            }

            Stack<string> stack = GetStack(layer);
            if (stack.Count == 0)
            {
                blocker.Hide();
                return;
            }

            if (!_instances.TryGetValue(stack.Peek(), out UIPanel topPanel)
                || topPanel == null
                || !topPanel.IsOpen
                || !topPanel.IsModal)
            {
                blocker.Hide();
                return;
            }

            blocker.ShowAbove(topPanel);
        }

        private void PopUntilTopIs(string panelId, UILayer layer)
        {
            Stack<string> stack = GetStack(layer);

            while (stack.Count > 0 && stack.Peek() != panelId)
            {
                string closingId = stack.Pop();
                ClosePanelInstance(closingId);
            }
        }

        private static bool IsInStack(Stack<string> stack, string panelId)
        {
            foreach (string id in stack)
            {
                if (id == panelId)
                {
                    return true;
                }
            }

            return false;
        }

        private UIPanel PushOverlayPanel(PanelId panelId, object args)
        {
            if (panelId == PanelId.Toast)
            {
                ShowToastFromArgs(args);
                return _toastPanel;
            }

            Debug.LogWarning($"[UIManager] Overlay 面板 {panelId} 请使用专用 API 打开。");
            return null;
        }

        private void ShowToastFromArgs(object args)
        {
            if (args is ToastOpenArgs toastArgs)
            {
                ShowToast(toastArgs.Message, toastArgs.Duration);
                return;
            }

            if (args is string message)
            {
                ShowToast(message);
                return;
            }

            ShowToast(string.Empty);
        }

        private void EnsureToastPanel()
        {
            if (_toastPanel != null)
            {
                return;
            }

            string toastKey = PanelIds.Key(PanelId.Toast);
            if (!_entryMap.TryGetValue(toastKey, out UIPanelEntry entry))
            {
                Debug.LogWarning("[UIManager] 未注册 Toast 面板，无法显示提示。");
                return;
            }

            Transform parent = GetLayerRoot(UILayer.Overlay);
            UIPanel panel = Instantiate(entry.Prefab, parent);
            panel.name = entry.Prefab.name;
            _toastPanel = panel as ToastPanel;

            if (_toastPanel == null)
            {
                Debug.LogError("[UIManager] Toast 预制体必须挂载 ToastPanel 组件。");
                Destroy(panel.gameObject);
                return;
            }

            _toastPanel.gameObject.SetActive(false);
        }

        private void BuildEntryMap()
        {
            _entryMap.Clear();

            if (panelEntries == null)
            {
                return;
            }

            foreach (UIPanelEntry entry in panelEntries)
            {
                if (entry == null || !entry.IsValid)
                {
                    continue;
                }

                if (_entryMap.ContainsKey(entry.PanelId))
                {
                    Debug.LogWarning($"[UIManager] 重复注册面板：{entry.PanelId}");
                    continue;
                }

                _entryMap.Add(entry.PanelId, entry);
            }
        }

        private Transform GetLayerRoot(UILayer layer)
        {
            if (_layerRoots.TryGetValue(layer, out Transform root) && root != null)
            {
                return root;
            }

            Debug.LogWarning($"[UIManager] 未找到层级 {layer}，回退到 Normal。");
            return _layerRoots.TryGetValue(UILayer.Normal, out Transform normal) ? normal : transform;
        }

        private static void BringToFront(UIPanel panel)
        {
            if (panel != null)
            {
                panel.transform.SetAsLastSibling();
            }
        }
    }
}
