using OperationBlacktide.Client.Features.Training;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Editor
{
    /// <summary>Author the training menu as a prefab so its layout remains editable in Unity.</summary>
    public static class BuildTrainingMenu
    {
        public const string PrefabPath = "Assets/Resources/UI/Panels/TrainingMenuPanel.prefab";
        private static readonly Color Ink = new Color32(12, 20, 25, 238);
        private static readonly Color Row = new Color32(43, 54, 63, 245);
        private static readonly Color Accent = new Color32(255, 164, 54, 255);
        private static readonly Color Muted = new Color32(174, 187, 198, 255);
        private static Font font;

        [MenuItem("OperationBlacktide/Training/Build Training Menu")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Stop Play Mode before building the menu.");
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BlacktideUI.ttf");
            var root = Rect("TrainingMenuPanel", null, Vector2.zero, Vector2.zero);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            try
            {
                var panel = root.gameObject.AddComponent<TrainingMenuPanel>();
                var fields = new SerializedObject(panel);
                fields.FindProperty("panelId").stringValue = "TrainingMenu";
                fields.FindProperty("layer").intValue = (int)UILayer.Popup;
                var sheet = Rect("Sheet", root, Vector2.zero, new Vector2(630, -32));
                sheet.anchorMin = new Vector2(.5f, 0); sheet.anchorMax = new Vector2(.5f, 1);
                Box(sheet, Ink, true);
                Top(Rect("Accent", sheet, Vector2.zero, new Vector2(630, 5)), -2.5f);
                Box((RectTransform)sheet.Find("Accent"), Accent);
                Top(Label("Mode", sheet, "DUST II  /  自由训练", Vector2.zero, new Vector2(550, 28), 16, Accent), -76);
                Top(Label("Title", sheet, "训练菜单", Vector2.zero, new Vector2(550, 54), 38, Color.white), -123);
                var home = Rect("Home", sheet, Vector2.zero, Vector2.zero); Stretch(home);
                Bind(fields, "home", home.gameObject);
                Bind(fields, "continueButton", Button("Continue", home, "继续训练", new Vector2(0, 148), new Vector2(536, 68), Accent, Ink));
                Bind(fields, "weaponsButton", Button("Weapons", home, "切换武器", new Vector2(0, 62), new Vector2(536, 68), Row, Color.white));
                Bind(fields, "returnButton", Button("Return", home, "返回大厅", new Vector2(0, -24), new Vector2(536, 68), Row, Color.white));
                Label("Hint", home, "选择武器，继续你的训练", new Vector2(0, -92), new Vector2(536, 32), 18, Muted);

                var picker = Rect("WeaponPicker", sheet, Vector2.zero, Vector2.zero); Stretch(picker);
                Bind(fields, "weaponPicker", picker.gameObject);
                var tabs = fields.FindProperty("categoryButtons"); tabs.arraySize = 4;
                for (int i = 0; i < 4; i++)
                {
                    var button = Button("Category" + (i + 1), picker, (i + 1) + "  " + TrainingWeaponSelection.SlotName(i + 1),
                        new Vector2(-210 + 140 * i, 0), new Vector2(132, 48), Row, Color.white, 18);
                    Top((RectTransform)button.transform, -209);
                    tabs.GetArrayElementAtIndex(i).objectReferenceValue = button;
                }
                var selected = Label("Selected", picker, "", Vector2.zero, new Vector2(550, 32), 17, Accent);
                Top(selected, -259); Bind(fields, "selectedText", selected.GetComponent<Text>());
                var viewport = Rect("Viewport", picker, Vector2.zero, Vector2.zero);
                Stretch(viewport); viewport.offsetMin = new Vector2(39, 208); viewport.offsetMax = new Vector2(-39, -292);
                Box(viewport, new Color(0, 0, 0, .08f), true);
                viewport.gameObject.AddComponent<RectMask2D>();
                var content = Rect("Content", viewport, Vector2.zero, Vector2.zero);
                content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
                var grid = content.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(268, 112); grid.spacing = new Vector2(16, 12);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 2;
                var fitter = content.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var scroll = viewport.gameObject.AddComponent<ScrollRect>();
                scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 34;
                Bind(fields, "content", content); Bind(fields, "scroll", scroll);
                var option = Rect("OptionTemplate", content, Vector2.zero, new Vector2(268, 112));
                var optionButton = option.gameObject.AddComponent<Button>();
                optionButton.targetGraphic = Box(option, Row, true);
                optionButton.navigation = new Navigation { mode = Navigation.Mode.None };
                Label("Name", option, "武器", new Vector2(-12, 35), new Vector2(220, 28), 20, Color.white, TextAnchor.MiddleLeft);
                var icon = Rect("Icon", option, new Vector2(-36, -11), new Vector2(174, 54)).gameObject.AddComponent<Image>();
                icon.preserveAspect = true; icon.raycastTarget = false;
                Label("State", option, "装备", new Vector2(91, -28), new Vector2(58, 28), 16, Accent);
                option.gameObject.SetActive(false); Bind(fields, "optionTemplate", optionButton);
                var back = Button("Back", picker, "返回训练菜单", Vector2.zero, new Vector2(552, 54), Row, Color.white, 20);
                Bottom((RectTransform)back.transform, 163); Bind(fields, "backButton", back);
                picker.gameObject.SetActive(false);
                var line = Rect("FooterLine", sheet, Vector2.zero, new Vector2(536, 1)); Bottom(line, 112); Box(line, new Color(1, 1, 1, .13f));
                var help = Label("Shortcuts", sheet, "1 主武器    2 副武器    3 近战    4 投掷\n重复按同一数字键，循环切换同类武器", Vector2.zero, new Vector2(556, 54), 17, Muted);
                Bottom(help, 73);
                Bottom(Label("Escape", sheet, "Esc  继续训练", Vector2.zero, new Vector2(550, 24), 16, Accent), 28);
                fields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("TRAINING_MENU_BUILD_PASS");
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = size; rect.anchoredPosition = position;
            return rect;
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void Top(RectTransform rect, float y)
        { rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y); }
        private static void Bottom(RectTransform rect, float y)
        { rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0); rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y); }
        private static Image Box(RectTransform rect, Color color, bool raycast = false)
        { var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = raycast; return image; }
        private static RectTransform Label(string name, Transform parent, string text, Vector2 position, Vector2 size, int fontSize, Color color, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var rect = Rect(name, parent, position, size); var label = rect.gameObject.AddComponent<Text>();
            label.font = font; label.text = text; label.fontSize = fontSize; label.color = color; label.alignment = alignment; label.raycastTarget = false;
            // This CJK font has tall ascenders; Unity's legacy Text truncation can discard an entire line.
            label.verticalOverflow = VerticalWrapMode.Overflow;
            rect.sizeDelta = new Vector2(size.x, Mathf.Max(size.y, label.preferredHeight + 4));
            return rect;
        }
        private static Button Button(string name, Transform parent, string text, Vector2 position, Vector2 size, Color background, Color foreground, int fontSize = 26)
        {
            var rect = Rect(name, parent, position, size); var image = Box(rect, background, true);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Label("Label", rect, text, Vector2.zero, size - new Vector2(12, 4), fontSize, foreground);
            return button;
        }
        private static void Bind(SerializedObject fields, string name, Object value) { fields.FindProperty(name).objectReferenceValue = value; }
    }
}
