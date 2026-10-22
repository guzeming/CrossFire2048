using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Editor
{
    public static class BuildSceneLoadingUI
    {
        [MenuItem("OperationBlacktide/UI/Build Scene Loading")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Stop Play Mode before building loading UI.");
            var root = Rect("SceneLoadingPanel", null, Vector2.zero, Vector2.one);
            try
            {
                var panel = root.gameObject.AddComponent<SceneLoadingPanel>();
                var fields = new SerializedObject(panel);
                fields.FindProperty("panelId").stringValue = "Loading";
                fields.FindProperty("layer").intValue = (int)UILayer.Overlay;
                // 独立排序覆盖战斗场景自行创建的 HUD Canvas。
                var canvas = root.gameObject.AddComponent<Canvas>();
                canvas.overrideSorting = true; canvas.sortingOrder = 32000;
                root.gameObject.AddComponent<GraphicRaycaster>();
                Box(Rect("Blocker", root, Vector2.zero, Vector2.one), new Color32(10, 12, 15, 255)).raycastTarget = true;
                var backdrop = Rect("Background", root, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
                backdrop.color = new Color(.72f, .72f, .72f, 1); backdrop.raycastTarget = false;
                var shade = Rect("Shade", root, Vector2.zero, Vector2.one).gameObject.AddComponent<UILoadingShade>();
                shade.color = new Color32(8, 10, 13, 255); shade.raycastTarget = false;

                var content = Rect("Content", root, Vector2.zero, new Vector2(1, 0));
                content.offsetMin = new Vector2(96, 72); content.offsetMax = new Vector2(-96, 340);
                var accent = Rect("Accent", content, new Vector2(0, 1), new Vector2(0, 1));
                accent.pivot = new Vector2(0, 1); accent.sizeDelta = new Vector2(48, 3);
                Box(accent, BlackGoldTheme.GoldLight);

                var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BlacktideUI.ttf");
                var title = Label("Destination", content, font, "大厅", 54, TextAnchor.MiddleLeft);
                title.rectTransform.offsetMin = new Vector2(0, 105); title.rectTransform.offsetMax = new Vector2(0, -30);
                var status = Label("Status", content, font, "正在加载场景", 22, TextAnchor.LowerLeft);
                status.color = BlackGoldTheme.Muted;
                status.rectTransform.offsetMin = new Vector2(0, 35); status.rectTransform.offsetMax = new Vector2(-150, -180);
                var percentage = Label("Percentage", content, font, "0%", 30, TextAnchor.LowerRight);
                percentage.color = BlackGoldTheme.GoldLight;
                percentage.rectTransform.offsetMin = new Vector2(0, 32); percentage.rectTransform.offsetMax = new Vector2(0, -180);

                var track = Rect("ProgressTrack", content, Vector2.zero, new Vector2(1, 0));
                track.pivot = new Vector2(.5f, 0); track.sizeDelta = new Vector2(0, 4);
                Box(track, new Color32(63, 58, 43, 255));
                var fill = Rect("ProgressFill", track, Vector2.zero, new Vector2(0, 1));
                Box(fill, BlackGoldTheme.Gold);
                var glow = Rect("Glow", fill, Vector2.zero, Vector2.one);
                glow.offsetMin = new Vector2(0, -5); glow.offsetMax = new Vector2(0, 5);
                Box(glow, new Color(1, .65f, .16f, .1f));
                var highlight = Rect("Highlight", fill, new Vector2(0, 1), Vector2.one);
                highlight.sizeDelta = new Vector2(0, 1); Box(highlight, BlackGoldTheme.GoldLight);

                Bind(fields, "background", backdrop); Bind(fields, "destination", title);
                Bind(fields, "status", status); Bind(fields, "percentage", percentage); Bind(fields, "progressFill", fill);
                Bind(fields, "lobbyBackground", AssetDatabase.LoadAssetAtPath<Texture>("Assets/Art/Lobby/Hangar/Textures/HangarBackground.png"));
                Bind(fields, "mapBackground", AssetDatabase.LoadAssetAtPath<Texture>("Assets/UI/Art/Demolition/DustIIOverview.png"));
                fields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root.gameObject, "Assets/Resources/UI/Panels/SceneLoadingPanel.prefab");
                AssetDatabase.SaveAssets();
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        private static void Bind(SerializedObject fields, string name, Object value) => fields.FindProperty(name).objectReferenceValue = value;
        private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5; rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
        private static Image Box(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
        }
        private static Text Label(string name, Transform parent, Font font, string text, int size, TextAnchor align)
        {
            var label = Rect(name, parent, Vector2.zero, Vector2.one).gameObject.AddComponent<Text>();
            label.font = font; label.fontSize = size; label.text = text; label.color = BlackGoldTheme.Text;
            label.alignment = align; label.raycastTarget = false; return label;
        }
    }
}
