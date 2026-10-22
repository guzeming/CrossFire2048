using OperationBlacktide.Client.Features.Lobby;
using System;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Client.Features.Demolition;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OperationBlacktide.Client.Editor
{
    /// <summary>Idempotent upgrade: preserves prefab identities, serialized bindings and gameplay.</summary>
    public static class BuildBlackGoldUI
    {
        public const string MaterialPath = "Assets/Resources/UI/Theme/BlackGoldGlass.mat";
        [MenuItem("OperationBlacktide/UI/Apply Black Gold Design")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before rebuilding UI.");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resources/UI/Theme/BlackGoldGlass.shader");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Black Gold glass shader is missing or invalid.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }
            material.shader = shader; EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
            foreach (var path in Directory.GetFiles("Assets/Resources/UI/Panels", "*.prefab"))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    RefineLayout(root);
                    BlackGoldTheme.Apply(root);
                    foreach (var effect in root.GetComponentsInChildren<UIGlassEffect>(true))
                        effect.GetComponent<Graphic>().material = material;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets/Settings" }))
            {
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (renderer.rendererFeatures.OfType<UIGlassRendererFeature>().Any()) continue;
                var feature = ScriptableObject.CreateInstance<UIGlassRendererFeature>();
                feature.name = "Black Gold / Glass backdrop";
                AssetDatabase.AddObjectToAsset(feature, renderer);
                renderer.rendererFeatures.Add(feature); renderer.SetDirty(); EditorUtility.SetDirty(renderer);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("BLACK_GOLD_BUILD_PASS: all 7 panels, shared controls, glass material and quality renderers upgraded.");
        }

        public static void RefineLayout(GameObject root)
        {
            var panel = root.GetComponent<UIPanel>();
            string id = panel != null ? panel.PanelId : "";
            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                if (button.targetGraphic == null) continue;
                bool primary = BlackGoldTheme.IsGold(button.colors.normalColor) || BlackGoldTheme.IsGold(button.targetGraphic.color) ||
                    button.name == "LoginButton" || button.name == "EnterGameButton" || button.name == "Equip";
                BlackGoldTheme.Style(button, primary);
            }
            if (id == "TrainingMenu") Training(root.transform);
            if (id == "Training") TrainingHud(root.transform);
            if (id == "Login")
            {
                Remove(root.transform, "Footer");
                var card = root.transform.Find("LoginCard") as RectTransform;
                if (card != null)
                {
                    Plate(card, "GlassPlate", new Vector2(524, 588));
                    // A faded backing reads as part of the scene instead of a framed form card.
                    card.Find("GlassPlate").GetComponent<UIGlassEffect>().SetKind(UIGlassEffect.Kind.Rail);
                    var title = card.Find("Heading")?.GetComponent<Text>();
                    if (title != null) { title.text = "登录"; title.fontSize = 32; }
                }
                var art = root.transform.Find("TacticalBackground")?.GetComponent<Image>();
                if (art != null) art.color = new Color(.74f, .72f, .68f, 1);
                foreach (var t in root.GetComponentsInChildren<Text>(true))
                {
                    if (t.name == "ChevronTop" || t.name == "ChevronBottom") t.gameObject.SetActive(false);
                }
                var register = root.transform.Find("LoginCard/RegisterButton")?.GetComponent<Button>();
                if (register != null) Tab(register);
            }
            if (id == "Lobby")
            {
                Remove(root.transform, "Footer", "PlayControls/Caption", "Briefing/Page2/Tag", "Briefing/Page2/Note",
                    "Briefing/Page2/Name", "EquipmentSelection/WeaponDetails/Hint");
                foreach (string path in new[] { "TopBar" })
                {
                    var image = root.transform.Find(path)?.GetComponent<Graphic>();
                    if (image != null) BlackGoldTheme.SurfaceFor(image);
                }
                foreach (var control in root.GetComponentsInChildren<Button>(true))
                    if (control.name.StartsWith("Tab", StringComparison.Ordinal))
                    {
                        BlackGoldTheme.Style(control, false);
                        control.GetComponent<UIGlassEffect>().SetKind(UIGlassEffect.Kind.Tab);
                        var rect = (RectTransform)control.transform;
                        rect.sizeDelta = new Vector2(rect.sizeDelta.x, 56);
                    }
                var username = root.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == "WelcomeText");
                if (username != null) { username.resizeTextForBestFit = true; username.resizeTextMinSize = 14; username.resizeTextMaxSize = 20; }
                var options = root.transform.Find("EquipmentSelection/WeaponDetails/Options") as RectTransform;
                if (options != null) { options.anchoredPosition = new Vector2(0, 91); options.sizeDelta = new Vector2(378, 406); }
                TacticalLobby(root.transform);
            }
            if (id == "ReturnToLobby")
            {
                var stripe = root.transform.Find("Accent") as RectTransform;
                if (stripe != null) { stripe.sizeDelta = new Vector2(490, 2); stripe.anchoredPosition = new Vector2(0, 117); }
                ((RectTransform)root.transform).sizeDelta = new Vector2(570, 240);
                Remove(root.transform, "Message");
                Position(root.transform, "Title", 48);
                Position(root.transform, "Continue", -50); Position(root.transform, "Confirm", -50);
            }
        }

        private static void Training(Transform root)
        {
            var sheet = root.Find("Sheet") as RectTransform;
            if (sheet == null) return;
            Layout(sheet, new Vector2(0, .5f), new Vector2(324, 0), new Vector2(520, 408));
            BlackGoldTheme.SurfaceFor(sheet.GetComponent<Graphic>(), UIGlassEffect.Kind.Rail);
            var accent = sheet.Find("Accent") as RectTransform;
            accent.sizeDelta = new Vector2(416, 1); accent.anchoredPosition = new Vector2(0, -107);
            Remove(sheet, "Mode", "Home/Hint", "Shortcuts", "Escape", "FooterLine", "WeaponPicker/Selected");
            SetText(sheet, "Title", "训练菜单", 34, -63);
            sheet.Find("Title").GetComponent<Text>().alignment = TextAnchor.MiddleLeft;
            ((RectTransform)sheet.Find("Title")).sizeDelta = new Vector2(416, 54);
            var home = sheet.Find("Home");
            Position(home, "Continue", 36); Position(home, "Weapons", -36); Position(home, "Return", -108);
            foreach (string name in new[] { "Continue", "Weapons", "Return" })
            {
                var button = home.Find(name) as RectTransform;
                button.sizeDelta = new Vector2(416, 58);
                Tab(button.GetComponent<Button>());
                var label = button.GetComponentInChildren<Text>(); label.fontSize = 24;
                label.alignment = TextAnchor.MiddleLeft; label.rectTransform.sizeDelta = new Vector2(370, 54);
            }
            var viewport = sheet.Find("WeaponPicker/Viewport") as RectTransform;
            viewport.offsetMin = new Vector2(50, 114); viewport.offsetMax = new Vector2(-50, -202);
            var grid = viewport.Find("Content").GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(280, 112); grid.spacing = new Vector2(16, 14);
            var back = sheet.Find("WeaponPicker/Back") as RectTransform;
            back.anchoredPosition = new Vector2(0, 58);
            back.GetComponentInChildren<Text>().text = "返回";
            for (int i = 1; i <= 4; i++)
            {
                Position(sheet, "WeaponPicker/Category" + i, -154);
                Tab(sheet.Find("WeaponPicker/Category" + i).GetComponent<Button>());
            }
        }

        private static void TacticalLobby(Transform root)
        {
            // Reserve the center for the live character. UI follows screen edges.
            var top = root.Find("TopBar") as RectTransform;
            top.sizeDelta = new Vector2(0, 72); top.anchoredPosition = new Vector2(0, -52);
            HidePlate(top);
            Remove(top, "Rule");
            Layout(top.Find("Logo"), new Vector2(0, .5f), new Vector2(118, 0), new Vector2(148, 54));
            var navigation = top.Find("Navigation");
            var agentTab = navigation.Find("Tab3");
            if (agentTab == null)
            {
                agentTab = Object.Instantiate(navigation.Find("Tab1").gameObject, navigation).transform;
                agentTab.name = "Tab3";
            }
            agentTab.GetComponentInChildren<Text>(true).text = "选角";
            var bindings = new SerializedObject(root.GetComponent<OperationBlacktide.Client.Features.Lobby.LobbyPanel>());
            foreach (var field in new[] { "tabButtons", "tabLabels", "tabIndicators", "tabPages" })
                bindings.FindProperty(field).arraySize = 4;
            bindings.FindProperty("tabButtons").GetArrayElementAtIndex(3).objectReferenceValue = agentTab.GetComponent<Button>();
            bindings.FindProperty("tabLabels").GetArrayElementAtIndex(3).objectReferenceValue = agentTab.GetComponentInChildren<Text>(true);
            bindings.FindProperty("tabIndicators").GetArrayElementAtIndex(3).objectReferenceValue = Child(agentTab, "Selection").gameObject;
            bindings.FindProperty("tabPages").GetArrayElementAtIndex(3).objectReferenceValue = root.Find("AgentSelection").gameObject;
            bindings.ApplyModifiedPropertiesWithoutUndo();
            Layout(navigation, new Vector2(0, .5f), new Vector2(524, 0), new Vector2(560, 48));
            int[] tabOrder = { 0, 3, 1, 2 };
            for (int column = 0; column < tabOrder.Length; column++)
            {
                var button = navigation.Find("Tab" + tabOrder[column]).GetComponent<Button>();
                button.transform.SetSiblingIndex(column);
                Layout(button.transform, new Vector2(.5f, .5f), new Vector2((column - 1.5f) * 140, 0), new Vector2(136, 46));
                Tab(button); LabelStyle(button.GetComponentInChildren<Text>(), 20, TextAnchor.MiddleCenter);
                // Keep serialized indicator objects for navigation, render one underline in the shader.
                foreach (var graphic in button.GetComponentsInChildren<Image>(true))
                    if (graphic != button.targetGraphic) graphic.enabled = false;
            }
            Layout(top.Find("WelcomeText"), new Vector2(1, .5f), new Vector2(-221, 0), new Vector2(218, 34));
            LabelStyle(top.Find("WelcomeText").GetComponent<Text>(), 17, TextAnchor.MiddleRight);
            Layout(top.Find("OnlineDot"), new Vector2(1, .5f), new Vector2(-349, 0), new Vector2(20, 20));
            Layout(top.Find("LogoutButton"), new Vector2(1, .5f), new Vector2(-66, 0), new Vector2(76, 38));
            Tab(top.Find("LogoutButton").GetComponent<Button>());
            BuildDisplaySettings.EnsureLobbyButton(root);

            var topShade = Shade(root, "TacticalTopShade", new Color(0, 0, 0, .72f), Color.clear, true);
            topShade.anchorMin = new Vector2(0, 1); topShade.anchorMax = Vector2.one;
            topShade.pivot = new Vector2(.5f, 1); topShade.sizeDelta = new Vector2(0, 174); topShade.anchoredPosition = Vector2.zero;
            topShade.SetAsFirstSibling();
            var bottomShade = Shade(root, "TacticalBottomShade", Color.clear, new Color(0, 0, 0, .7f), true);
            bottomShade.anchorMin = Vector2.zero; bottomShade.anchorMax = new Vector2(1, 0);
            bottomShade.pivot = new Vector2(.5f, 0); bottomShade.sizeDelta = new Vector2(0, 300); bottomShade.anchoredPosition = Vector2.zero;
            bottomShade.SetAsFirstSibling();

            var briefing = (RectTransform)root.Find("Briefing");
            Stretch(briefing); HidePlate(briefing);
            Layout(briefing.Find("Page0"), Vector2.zero, new Vector2(244, 236), new Vector2(392, 48));
            Layout(briefing.Find("Page0/RoleName"), new Vector2(.5f, .5f), Vector2.zero, new Vector2(392, 42));
            LabelStyle(briefing.Find("Page0/RoleName").GetComponent<Text>(), 26, TextAnchor.MiddleLeft);
            Layout(briefing.Find("Page2"), new Vector2(0, 1), new Vector2(244, -246), new Vector2(392, 220));
            Layout(root.Find("LoadoutTeams"), Vector2.zero, new Vector2(244, 174), new Vector2(392, 44));
            Tab(root.Find("LoadoutTeams/CT").GetComponent<Button>()); Tab(root.Find("LoadoutTeams/T").GetComponent<Button>());
            Remove(root, "ChooseAgent", "AgentSelection/Close");
            var agents = root.Find("AgentSelection");
            Layout(agents, new Vector2(0, 1), new Vector2(244, -482), new Vector2(392, 760));
            HidePlate(agents);
            Layout(agents.Find("Title"), new Vector2(.5f, .5f), new Vector2(0, 332), new Vector2(392, 42));
            LabelStyle(agents.Find("Title").GetComponent<Text>(), 28, TextAnchor.MiddleLeft);
            Layout(agents.Find("Agents"), new Vector2(.5f, .5f), new Vector2(0, -88), new Vector2(366, 550));

            var play = (RectTransform)root.Find("PlayControls");
            Layout(play, new Vector2(1, 0), new Vector2(-246, 152), new Vector2(396, 200));
            HidePlate(play);
            Layout(play.Find("ModeButton"), new Vector2(.5f, .5f), new Vector2(0, 38), new Vector2(396, 124));
            Layout(play.Find("ModeButton/ModeName"), new Vector2(.5f, .5f), new Vector2(-10, -30), new Vector2(336, 40));
            LabelStyle(play.Find("ModeButton/ModeName").GetComponent<Text>(), 24, TextAnchor.MiddleLeft);
            Layout(play.Find("EnterGameButton"), new Vector2(.5f, .5f), new Vector2(0, -65), new Vector2(396, 70));
            var enter = play.Find("EnterGameButton").GetComponentInChildren<Text>();
            enter.text = "开始游戏"; LabelStyle(enter, 28, TextAnchor.MiddleCenter);
            Remove(play, "ModeButton/Accent");
            var preview = Child(play, "MapPreview");
            Layout(preview, new Vector2(.5f, .5f), new Vector2(0, 38), new Vector2(396, 124));
            var art = preview.GetComponent<RawImage>(); if (art == null) art = preview.gameObject.AddComponent<RawImage>();
            art.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Art/Demolition/DustIIOverview.png");
            art.uvRect = new Rect(0, .17f, 1, .68f); art.color = new Color(.72f, .77f, .77f, .9f); art.raycastTarget = false;
            preview.SetAsFirstSibling();
            var modeShade = Shade(play, "ModeShade", new Color(0, 0, 0, .08f), new Color(0, 0, 0, .84f), true);
            Layout(modeShade, new Vector2(.5f, .5f), new Vector2(0, 38), new Vector2(396, 124)); modeShade.SetSiblingIndex(1);
            Layout(root.Find("ModePicker/Menu"), new Vector2(1, 0), new Vector2(-246, 390), new Vector2(396, 236));
            for (int i = 0; i < 3; i++)
            {
                var button = root.Find("ModePicker/Menu/ModeOption" + i).GetComponent<Button>();
                Layout(button.transform, new Vector2(.5f, .5f), new Vector2(0, 36 - i * 56), new Vector2(364, 48));
                Tab(button);
            }
            Layout(root.Find("ModePicker/Menu/Title"), new Vector2(.5f, .5f), new Vector2(0, 88), new Vector2(350, 28));

            // Equipment stays in two narrow edge columns; lists carry the structure, not nested cards.
            var backdrop = Child(root, "EquipmentBackdrop"); Stretch(backdrop); backdrop.SetAsFirstSibling();
            var leftShade = Shade(backdrop, "Left", new Color(0, 0, 0, .92f), Color.clear, false);
            leftShade.anchorMin = Vector2.zero; leftShade.anchorMax = new Vector2(0, 1); leftShade.pivot = new Vector2(0, .5f);
            leftShade.anchoredPosition = Vector2.zero; leftShade.sizeDelta = new Vector2(600, 0);
            var rightShade = Shade(backdrop, "Right", Color.clear, new Color(0, 0, 0, .92f), false);
            rightShade.anchorMin = new Vector2(1, 0); rightShade.anchorMax = Vector2.one; rightShade.pivot = new Vector2(1, .5f);
            rightShade.anchoredPosition = Vector2.zero; rightShade.sizeDelta = new Vector2(600, 0);
            root.GetComponent<OperationBlacktide.Client.Features.Lobby.LobbyLoadoutView>().equipmentBackdrop = backdrop.gameObject;
            backdrop.gameObject.SetActive(false);
            Layout(root.Find("EquipmentSelection/Title"), new Vector2(0, 1), new Vector2(244, -150), new Vector2(392, 42));
            LabelStyle(root.Find("EquipmentSelection/Title").GetComponent<Text>(), 28, TextAnchor.MiddleLeft);
            int[] categoryOrder = { 0, 1, 5, 2, 3, 4 };
            for (int column = 0; column < categoryOrder.Length; column++)
            {
                var category = root.Find("EquipmentSelection/Category" + categoryOrder[column]);
                Layout(category, new Vector2(0, 1), new Vector2(80 + column * 65, -274), new Vector2(62, 40));
                Tab(category.GetComponent<Button>());
            }
            HidePlate(root.Find("EquipmentSelection/Slots"));
            Layout(root.Find("EquipmentSelection/Slots"), new Vector2(0, 1), new Vector2(244, -620), new Vector2(392, 620));
            var details = root.Find("EquipmentSelection/WeaponDetails");
            Layout(details, new Vector2(1, 1), new Vector2(-246, -516), new Vector2(396, 760));
            HidePlate(details);
            Layout(details.Find("WeaponName"), new Vector2(.5f, .5f), new Vector2(0, 330), new Vector2(372, 48));
            LabelStyle(details.Find("WeaponName").GetComponent<Text>(), 28, TextAnchor.MiddleLeft);
            Layout(details.Find("Options"), new Vector2(.5f, .5f), new Vector2(0, 90), new Vector2(378, 402));
            var template = details.Find("Options/Content/OptionTemplate");
            template.GetComponent<LayoutElement>().preferredHeight = 108;
            Layout(template.Find("Icon"), new Vector2(0, .5f), new Vector2(74, 0), new Vector2(132, 92));
            Tab(details.Find("EquipBoth").GetComponent<Button>());
            Tab(details.Find("Display").GetComponent<Button>());
            Tab(details.Find("Inspect").GetComponent<Button>());
        }

        private static RectTransform Child(Transform parent, string name)
        {
            var found = parent.Find(name) as RectTransform;
            if (found != null) return found;
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false); return rect;
        }
        private static RectTransform Shade(Transform parent, string name, Color from, Color to, bool vertical)
        {
            var rect = Child(parent, name); var shade = rect.GetComponent<DemolitionGradient>();
            if (shade == null) shade = rect.gameObject.AddComponent<DemolitionGradient>();
            shade.From = from; shade.To = to; shade.Vertical = vertical; shade.raycastTarget = false; shade.SetAllDirty(); return rect;
        }
        private static void Layout(Transform target, Vector2 anchor, Vector2 position, Vector2 size)
        {
            if (!(target is RectTransform rect)) return;
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void HidePlate(Transform target)
        { var graphic = target != null ? target.GetComponent<Graphic>() : null; if (graphic != null) graphic.enabled = false; }
        private static void LabelStyle(Text label, int size, TextAnchor alignment)
        { if (label == null) return; label.fontSize = size; label.fontStyle = FontStyle.Normal; label.alignment = alignment; }
        private static void Tab(Button button)
        {
            BlackGoldTheme.Style(button, false); button.GetComponent<UIGlassEffect>().SetKind(UIGlassEffect.Kind.Tab);
            button.GetComponentInChildren<Text>(true).color = BlackGoldTheme.Text;
        }

        private static void TrainingHud(Transform root)
        {
            Remove(root, "SafeArea/Controls", "SafeArea/Location/Mode", "SafeArea/Session/Mode",
                "SafeArea/Vitals/HealthLabel", "SafeArea/Vitals/ArmorLabel");
            SetText(root, "SafeArea/Location/Map", "DUST II", 24, 0);
            Position(root, "SafeArea/Session/Time", 0);
            var location = root.Find("SafeArea/Location") as RectTransform;
            if (location != null) location.sizeDelta = new Vector2(252, 52);
            var accent = root.Find("SafeArea/Location/Accent") as RectTransform;
            if (accent != null) accent.sizeDelta = new Vector2(3, 52);
            var timer = root.Find("SafeArea/Session") as RectTransform;
            if (timer != null) timer.sizeDelta = new Vector2(158, 60);
            var status = root.Find("SafeArea/Ammunition/Status")?.GetComponent<Text>();
            if (status != null) status.text = "";
        }

        private static void Remove(Transform parent, params string[] paths)
        {
            foreach (var path in paths)
            {
                var child = parent.Find(path);
                if (child != null) Object.DestroyImmediate(child.gameObject);
            }
        }

        private static void Position(Transform parent, string path, float y)
        {
            var rect = parent.Find(path) as RectTransform;
            if (rect != null) rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
        }
        private static void SetText(Transform parent, string path, string value, int size, float y)
        {
            var label = parent.Find(path)?.GetComponent<Text>();
            if (label == null) return; label.text = value; label.fontSize = size; Position(parent, path, y);
        }
        private static void Plate(RectTransform parent, string name, Vector2 size)
        {
            var found = parent.Find(name);
            var go = found != null ? found.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false); rect.SetAsFirstSibling();
            go.layer = 5; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size; rect.anchoredPosition = Vector2.zero;
            var image = go.GetComponent<Image>(); image.raycastTarget = false;
            BlackGoldTheme.SurfaceFor(image);
        }
    }
}
