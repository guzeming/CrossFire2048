using OperationBlacktide.Client.Features.Account;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Editor
{
    public static class BuildDisplaySettings
    {
        private static Font font;

        [MenuItem("OperationBlacktide/UI/Build Display Settings")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Stop Play Mode before building settings.");
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BlacktideUI.ttf");
            BuildMenu();
            BuildSettings();
            BuildPerformanceStats();
            const string lobbyPath = "Assets/Resources/UI/Panels/LobbyPanel.prefab";
            var lobby = PrefabUtility.LoadPrefabContents(lobbyPath);
            try
            {
                EnsureLobbyButton(lobby.transform);
                PrefabUtility.SaveAsPrefabAsset(lobby, lobbyPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(lobby); }
            AssetDatabase.SaveAssets();
            Debug.Log("DISPLAY_SETTINGS_BUILD_PASS");
        }

        // Also called by the lobby layout builder so rebuilding the lobby preserves this entry.
        public static void EnsureLobbyButton(Transform lobby)
        {
            var top = lobby.Find("TopBar");
            if (top == null) return;
            if (font == null) font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BlacktideUI.ttf");
            var existing = top.Find("SettingsButton");
            var button = existing != null ? existing.GetComponent<Button>() : Button("SettingsButton", top, "设置", new Vector2(-160, 0), new Vector2(84, 38));
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, .5f);
            rect.anchoredPosition = new Vector2(-160, 0);
            button.GetComponentInChildren<Text>().fontSize = 18;
            BlackGoldTheme.Style(button, false);
            button.GetComponent<UIGlassEffect>().SetKind(UIGlassEffect.Kind.Tab);
            var username = top.Find("WelcomeText") as RectTransform;
            username.anchoredPosition = new Vector2(-330, 0); username.sizeDelta = new Vector2(218, 34);
            (top.Find("OnlineDot") as RectTransform).anchoredPosition = new Vector2(-458, 0);
            var fields = new SerializedObject(lobby.GetComponent<LobbyPanel>());
            Bind(fields, "settingsButton", button); fields.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildMenu()
        {
            var root = Rect("LobbyMenuPanel", null, Vector2.zero, Vector2.zero); Stretch(root);
            try
            {
                var fields = Panel<LobbyMenuPanel>(root, "LobbyMenu");
                var sheet = Rect("Sheet", root, new Vector2(324, 0), new Vector2(520, 408));
                sheet.anchorMin = sheet.anchorMax = new Vector2(0, .5f);
                BlackGoldTheme.SurfaceFor(Box(sheet, BlackGoldTheme.Ink), UIGlassEffect.Kind.Rail);
                Label("Title", sheet, "菜单", new Vector2(-18, 141), new Vector2(380, 54), 34);
                Bind(fields, "closeButton", Button("Close", sheet, "×", new Vector2(212, 145), new Vector2(48, 48)));
                Line(sheet, 98, 416);
                string[] names = { "Settings", "Login", "Quit" }, labels = { "设置", "返回登录界面", "退出游戏" }, bindings = { "settingsButton", "loginButton", "quitButton" };
                for (int i = 0; i < names.Length; i++)
                {
                    var button = Button(names[i], sheet, labels[i], new Vector2(0, 36 - 72 * i), new Vector2(416, 58));
                    BlackGoldTheme.Style(button, false); button.GetComponent<UIGlassEffect>().SetKind(UIGlassEffect.Kind.Tab);
                    button.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                    Bind(fields, bindings[i], button);
                }
                Save(root, fields, "LobbyMenuPanel");
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        private static void BuildSettings()
        {
            var root = Rect("SettingsPanel", null, Vector2.zero, Vector2.zero); Stretch(root);
            try
            {
                var fields = Panel<SettingsPanel>(root, "Settings");
                var pageBackground = Rect("Background", root, Vector2.zero, Vector2.zero); Stretch(pageBackground);
                Box(pageBackground, new Color32(10, 12, 15, 255));
                var sheet = Rect("Sheet", root, Vector2.zero, Vector2.zero); Stretch(sheet);
                var controls = sheet.gameObject.AddComponent<CanvasGroup>(); Bind(fields, "controls", controls);
                Anchor(Label("Title", sheet, "设置", Vector2.zero, new Vector2(360, 64), 42).rectTransform,
                    new Vector2(0, 1), new Vector2(244, -78));
                Rule(sheet, "HeaderDivider", 1, -132);
                Rule(sheet, "FooterDivider", 0, 132);

                var categories = Rect("Categories", sheet, Vector2.zero, Vector2.zero);
                categories.anchorMin = Vector2.zero; categories.anchorMax = new Vector2(0, 1);
                categories.offsetMin = new Vector2(64, 164); categories.offsetMax = new Vector2(328, -172);
                var category = Button("Display", categories, "显示", Vector2.zero, new Vector2(264, 64));
                Anchor((RectTransform)category.transform, new Vector2(.5f, 1), new Vector2(0, -32));
                category.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                BlackGoldTheme.Style(category, false); category.GetComponent<UIGlassEffect>().SetKind(UIGlassEffect.Kind.Tab);
                var sidebarRule = Rect("CategoryDivider", sheet, Vector2.zero, Vector2.zero);
                sidebarRule.anchorMin = Vector2.zero; sidebarRule.anchorMax = new Vector2(0, 1);
                sidebarRule.offsetMin = new Vector2(360, 164); sidebarRule.offsetMax = new Vector2(361, -172);
                Box(sidebarRule, new Color(BlackGoldTheme.Gold.r, BlackGoldTheme.Gold.g, BlackGoldTheme.Gold.b, .22f)).raycastTarget = false;

                // Each category owns a page in this shared content region; the footer and draft remain shared.
                var pages = Rect("Content", sheet, Vector2.zero, Vector2.zero); Stretch(pages);
                pages.offsetMin = new Vector2(416, 164); pages.offsetMax = new Vector2(-64, -172);
                var display = Rect("Display", pages, Vector2.zero, Vector2.zero); Stretch(display);
                var buttons = fields.FindProperty("categoryButtons"); buttons.arraySize = 1;
                buttons.GetArrayElementAtIndex(0).objectReferenceValue = category;
                var categoryPages = fields.FindProperty("categoryPages"); categoryPages.arraySize = 1;
                categoryPages.GetArrayElementAtIndex(0).objectReferenceValue = display.gameObject;
                Anchor(Label("Title", display, "显示", Vector2.zero, new Vector2(400, 52), 32).rectTransform,
                    new Vector2(0, 1), new Vector2(200, -26));
                var fullscreenRow = SettingsRow(display, "FullscreenRow", "全屏显示", -134);
                var toggle = DefaultControls.CreateToggle(new DefaultControls.Resources()).GetComponent<Toggle>();
                toggle.name = "Fullscreen"; toggle.transform.SetParent(fullscreenRow, false);
                Place((RectTransform)toggle.transform, Vector2.zero, new Vector2(320, 48));
                Anchor((RectTransform)toggle.transform, new Vector2(1, .5f), new Vector2(-184, 0));
                toggle.GetComponentInChildren<Text>().text = "开启";
                var background = (RectTransform)toggle.transform.Find("Background");
                background.anchorMin = background.anchorMax = new Vector2(0, .5f);
                background.anchoredPosition = new Vector2(18, 0); background.sizeDelta = new Vector2(32, 32);
                toggle.targetGraphic.color = BlackGoldTheme.Surface;
                foreach (int side in new[] { -1, 1 })
                {
                    Box(Rect("Border", background, new Vector2(side * 16, 0), new Vector2(1, 32)), BlackGoldTheme.Gold).raycastTarget = false;
                    Box(Rect("Border", background, new Vector2(0, side * 16), new Vector2(32, 1)), BlackGoldTheme.Gold).raycastTarget = false;
                }
                var check = toggle.graphic.rectTransform;
                check.anchorMin = check.anchorMax = new Vector2(.5f, .5f); check.anchoredPosition = Vector2.zero; check.sizeDelta = new Vector2(18, 18);
                ((Image)toggle.graphic).sprite = null; toggle.graphic.color = BlackGoldTheme.Gold;
                var toggleLabel = toggle.GetComponentInChildren<Text>().rectTransform;
                toggleLabel.offsetMin = new Vector2(52, 0); toggleLabel.offsetMax = Vector2.zero;
                Bind(fields, "fullscreenToggle", toggle);
                var resolutionRow = SettingsRow(display, "ResolutionRow", "分辨率", -234);
                var dropdownObject = DefaultControls.CreateDropdown(new DefaultControls.Resources());
                var defaultDropdown = dropdownObject.GetComponent<Dropdown>();
                var target = defaultDropdown.targetGraphic; var template = defaultDropdown.template;
                var caption = defaultDropdown.captionText; var itemText = defaultDropdown.itemText;
                Object.DestroyImmediate(defaultDropdown);
                var dropdown = dropdownObject.AddComponent<SettingsResolutionDropdown>();
                dropdown.targetGraphic = target; dropdown.template = template;
                dropdown.captionText = caption; dropdown.itemText = itemText;
                dropdown.name = "Resolution"; dropdown.transform.SetParent(resolutionRow, false);
                Place((RectTransform)dropdown.transform, Vector2.zero, new Vector2(320, 52));
                Anchor((RectTransform)dropdown.transform, new Vector2(1, .5f), new Vector2(-184, 0));
                dropdown.targetGraphic.color = BlackGoldTheme.Surface;
                dropdown.template.sizeDelta = new Vector2(0, 220);
                dropdown.template.GetComponent<Image>().color = BlackGoldTheme.Ink;
                var scrollbar = dropdown.template.GetComponentInChildren<Scrollbar>(true);
                scrollbar.GetComponent<Image>().color = BlackGoldTheme.Surface;
                scrollbar.targetGraphic.color = BlackGoldTheme.Gold;
                var item = dropdown.itemText.transform.parent as RectTransform; item.sizeDelta = new Vector2(0, 44);
                var content = item.parent as RectTransform; content.sizeDelta = new Vector2(0, 48);
                var itemToggle = item.GetComponent<Toggle>(); itemToggle.targetGraphic.color = BlackGoldTheme.Surface;
                var itemCheck = itemToggle.graphic as Image; itemCheck.sprite = null; itemCheck.color = BlackGoldTheme.Gold;
                itemCheck.rectTransform.sizeDelta = new Vector2(6, 20);
                Object.DestroyImmediate(dropdown.transform.Find("Arrow").gameObject);
                Label("Arrow", dropdown.transform, "▾", new Vector2(136, 0), new Vector2(32, 42), 22, TextAnchor.MiddleCenter);
                Bind(fields, "resolutionDropdown", dropdown);
                var performanceRow = SettingsRow(display, "PerformanceStatsRow", "显示性能统计数据", -334);
                var performanceToggle = Object.Instantiate(toggle, performanceRow, false);
                performanceToggle.name = "PerformanceStats";
                performanceToggle.isOn = true;
                Bind(fields, "performanceStatsToggle", performanceToggle);
                var status = Label("Status", sheet, "", Vector2.zero, new Vector2(400, 36), 20, TextAnchor.MiddleRight);
                Anchor(status.rectTransform, new Vector2(1, 0), new Vector2(-616, 72)); status.color = BlackGoldTheme.Gold;
                Bind(fields, "statusText", status);
                var back = Button("Back", sheet, "返回", Vector2.zero, new Vector2(220, 60));
                Anchor((RectTransform)back.transform, Vector2.zero, new Vector2(174, 72)); Bind(fields, "backButton", back);
                var save = Button("Save", sheet, "保存", Vector2.zero, new Vector2(320, 60), true);
                Anchor((RectTransform)save.transform, new Vector2(1, 0), new Vector2(-224, 72)); Bind(fields, "saveButton", save);

                // Keep the draft alive while this child dialog is open; pushing a new panel would close/reopen settings.
                var prompt = Rect("UnsavedPrompt", root, Vector2.zero, Vector2.zero); Stretch(prompt);
                Box(prompt, new Color(0, 0, 0, .7f));
                var dialog = Rect("Dialog", prompt, Vector2.zero, new Vector2(740, 292)); Box(dialog, BlackGoldTheme.Ink);
                Label("Title", dialog, "保存修改？", new Vector2(0, 85), new Vector2(636, 52), 30);
                Label("Message", dialog, "显示设置已修改，离开前是否保存？", new Vector2(0, 20), new Vector2(636, 48), 22);
                Bind(fields, "cancelButton", Button("Cancel", dialog, "继续编辑", new Vector2(-220, -82), new Vector2(204, 56)));
                Bind(fields, "discardButton", Button("Discard", dialog, "不保存", new Vector2(0, -82), new Vector2(204, 56)));
                Bind(fields, "saveAndLeaveButton", Button("SaveAndLeave", dialog, "保存并离开", new Vector2(220, -82), new Vector2(204, 56), true));
                Bind(fields, "unsavedPrompt", prompt.gameObject); prompt.gameObject.SetActive(false);
                foreach (var label in root.GetComponentsInChildren<Text>(true))
                {
                    label.font = font; label.color = BlackGoldTheme.Text; label.raycastTarget = false;
                    label.verticalOverflow = VerticalWrapMode.Overflow;
                    if (label.fontSize < 18) label.fontSize = 22;
                }
                foreach (var transform in root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 5;
                Save(root, fields, "SettingsPanel");
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        private static void BuildPerformanceStats()
        {
            var root = Rect("PerformanceStatsOverlay", null, Vector2.zero, Vector2.zero); Stretch(root);
            try
            {
                var fields = new SerializedObject(root.gameObject.AddComponent<PerformanceStatsOverlay>());
                var group = root.gameObject.AddComponent<CanvasGroup>();
                group.interactable = group.blocksRaycasts = false;
                var card = Rect("Content", root, Vector2.zero, new Vector2(420, 156));
                card.pivot = new Vector2(1, 1);
                Anchor(card, Vector2.one, new Vector2(-24, -24));
                Box(card, new Color(0, 0, 0, .65f)).raycastTarget = false;
                var statistics = Label("Statistics", card, "", Vector2.zero, new Vector2(388, 116), 19, TextAnchor.UpperLeft);
                statistics.rectTransform.pivot = new Vector2(0, 1);
                Anchor(statistics.rectTransform, new Vector2(0, 1), new Vector2(16, -10));
                var device = Label("Device", card, "", Vector2.zero, new Vector2(388, 24), 16, TextAnchor.UpperLeft);
                device.rectTransform.pivot = new Vector2(0, 1);
                Anchor(device.rectTransform, new Vector2(0, 1), new Vector2(16, -126));
                device.resizeTextForBestFit = true; device.resizeTextMinSize = 12; device.resizeTextMaxSize = 16;
                device.verticalOverflow = VerticalWrapMode.Truncate;
                statistics.color = device.color = new Color32(80, 255, 100, 255);
                Bind(fields, "content", card.gameObject);
                Bind(fields, "statisticsText", statistics); Bind(fields, "deviceText", device);
                fields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root.gameObject, "Assets/Resources/UI/PerformanceStatsOverlay.prefab");
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        private static SerializedObject Panel<T>(RectTransform root, string id) where T : UIPanel
        {
            var fields = new SerializedObject(root.gameObject.AddComponent<T>());
            fields.FindProperty("panelId").stringValue = id; fields.FindProperty("layer").intValue = (int)UILayer.Popup;
            return fields;
        }
        private static void Save(RectTransform root, SerializedObject fields, string name)
        {
            fields.ApplyModifiedPropertiesWithoutUndo(); BlackGoldTheme.Apply(root.gameObject);
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, "Assets/Resources/UI/Panels/" + name + ".prefab");
        }
        private static void Bind(SerializedObject fields, string name, Object value) => fields.FindProperty(name).objectReferenceValue = value;
        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5; rect.SetParent(parent, false); Place(rect, position, size); return rect;
        }
        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        { rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = size; rect.anchoredPosition = position; }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position)
        { rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = position; }
        private static void Rule(Transform parent, string name, float anchorY, float y)
        {
            var rect = Rect(name, parent, new Vector2(0, y), new Vector2(-128, 1));
            rect.anchorMin = new Vector2(0, anchorY); rect.anchorMax = new Vector2(1, anchorY);
            Box(rect, new Color(BlackGoldTheme.Gold.r, BlackGoldTheme.Gold.g, BlackGoldTheme.Gold.b, .3f)).raycastTarget = false;
        }
        private static RectTransform SettingsRow(Transform parent, string name, string title, float y)
        {
            var row = Rect(name, parent, new Vector2(0, y), new Vector2(0, 84));
            row.anchorMin = new Vector2(0, 1); row.anchorMax = Vector2.one;
            var background = Rect("Background", row, Vector2.zero, Vector2.zero); Stretch(background);
            Box(background, new Color32(20, 23, 27, 255));
            Anchor(Label("Label", row, title, Vector2.zero, new Vector2(300, 48), 24).rectTransform,
                new Vector2(0, .5f), new Vector2(174, 0));
            return row;
        }
        private static Image Box(RectTransform rect, Color color)
        { var image = rect.gameObject.AddComponent<Image>(); image.color = color; return image; }
        private static void Line(Transform parent, float y, float width)
        { var image = Box(Rect("Divider", parent, new Vector2(0, y), new Vector2(width, 1)), BlackGoldTheme.Gold); image.raycastTarget = false; }
        private static Text Label(string name, Transform parent, string text, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var label = Rect(name, parent, position, size).gameObject.AddComponent<Text>();
            label.font = font; label.text = text; label.fontSize = fontSize; label.color = BlackGoldTheme.Text;
            label.alignment = alignment; label.raycastTarget = false; label.verticalOverflow = VerticalWrapMode.Overflow; return label;
        }
        private static Button Button(string name, Transform parent, string text, Vector2 position, Vector2 size, bool primary = false)
        {
            var rect = Rect(name, parent, position, size); var image = Box(rect, primary ? BlackGoldTheme.Gold : BlackGoldTheme.Surface);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            Label("Label", rect, text, Vector2.zero, size - new Vector2(24, 0), 24, TextAnchor.MiddleCenter);
            return button;
        }
    }
}
