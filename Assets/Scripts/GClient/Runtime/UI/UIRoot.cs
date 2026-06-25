using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CrossFire2048.Client.UI
{
    /// <summary>
    /// UI 根节点。负责创建 Canvas、EventSystem 和各层级容器，并初始化 UIManager。
    /// 场景中挂一个 UIRoot 即可作为整个客户端 UI 入口。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(UIManager))]
    public sealed class UIRoot : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;
        [SerializeField] private UIManager uiManager;
        [SerializeField] private bool enableBackKey = true;
        [SerializeField] private bool dontDestroyOnLoad = true;

        private readonly Dictionary<UILayer, Transform> _layerRoots = new Dictionary<UILayer, Transform>();
        private GameObject _eventSystemObject;

        public static UIRoot Instance { get; private set; }

        public bool DontDestroyOnLoadEnabled => dontDestroyOnLoad;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[UIRoot] 场景中存在多个 UIRoot，销毁重复实例。");
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (dontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }

            if (uiManager == null)
            {
                uiManager = GetComponent<UIManager>();
            }

            EnsureEventSystem();
            EnsureCanvas();
            BuildLayerRoots();
            uiManager.Initialize(_layerRoots);
        }

        private void Update()
        {
            if (!enableBackKey || uiManager == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                uiManager.HandleBackInput();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public Transform GetLayerRoot(UILayer layer)
        {
            _layerRoots.TryGetValue(layer, out Transform root);
            return root;
        }

        private void EnsureEventSystem()
        {
            if (_eventSystemObject != null)
            {
                return;
            }

            EventSystem existing = FindObjectOfType<EventSystem>();
            if (existing != null)
            {
                _eventSystemObject = existing.gameObject;
                if (dontDestroyOnLoad)
                {
                    DontDestroyOnLoad(_eventSystemObject);
                }

                return;
            }

            _eventSystemObject = new GameObject("EventSystem");
            _eventSystemObject.AddComponent<EventSystem>();
            _eventSystemObject.AddComponent<StandaloneInputModule>();

            if (dontDestroyOnLoad)
            {
                DontDestroyOnLoad(_eventSystemObject);
            }
        }

        private void EnsureCanvas()
        {
            if (canvas != null)
            {
                return;
            }

            canvas = GetComponentInChildren<Canvas>();
            if (canvas != null)
            {
                return;
            }

            GameObject canvasObject = new GameObject("Canvas");
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();
        }

        private void BuildLayerRoots()
        {
            _layerRoots.Clear();

            Transform canvasTransform = canvas.transform;
            CreateLayerRoot(canvasTransform, UILayer.Background);
            CreateLayerRoot(canvasTransform, UILayer.Normal);
            CreateLayerRoot(canvasTransform, UILayer.Popup);
            CreateLayerRoot(canvasTransform, UILayer.Overlay);
        }

        private void CreateLayerRoot(Transform parent, UILayer layer)
        {
            string objectName = $"Layer_{layer}";
            Transform existing = parent.Find(objectName);
            if (existing != null)
            {
                _layerRoots[layer] = existing;
                StretchFullScreen(existing as RectTransform);
                return;
            }

            GameObject layerObject = new GameObject(objectName, typeof(RectTransform));
            layerObject.transform.SetParent(parent, false);
            RectTransform rect = layerObject.GetComponent<RectTransform>();
            StretchFullScreen(rect);
            _layerRoots[layer] = layerObject.transform;
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
