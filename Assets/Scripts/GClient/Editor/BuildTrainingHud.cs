using System;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.Features.Training;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OperationBlacktide.Client.Editor
{
    /// <summary>Builds only the HUD and weapon silhouettes, without modifying the open gameplay scene.</summary>
    public static class BuildTrainingHud
    {
        public const string PrefabPath = "Assets/Resources/UI/Panels/TrainingPanel.prefab";
        public const string IconFolder = "Assets/UI/Hud/Weapons";
        private static readonly Vector2 Center = new Vector2(.5f, .5f);
        private static readonly Color Ink = new Color32(12, 20, 25, 170);
        private static readonly Color White = new Color32(238, 244, 241, 255);
        private static readonly Color Muted = new Color32(174, 189, 191, 255);
        private static readonly Color Accent = new Color32(240, 190, 100, 255);
        private static Font font;
        private static Sprite whiteSprite;

        [MenuItem("OperationBlacktide/Training/Build HUD")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before building the HUD.");
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BlacktideUI.ttf");
            if (font == null) throw new InvalidOperationException("Missing Blacktide UI font.");
            BuildIcons();
            const string whitePath = "Assets/UI/Hud/White.png";
            var white = new Texture2D(2,2,TextureFormat.RGBA32,false);
            white.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); white.Apply();
            File.WriteAllBytes(whitePath,white.EncodeToPNG()); Object.DestroyImmediate(white);
            AssetDatabase.ImportAsset(whitePath);
            var whiteImporter = (TextureImporter)AssetImporter.GetAtPath(whitePath);
            whiteImporter.textureType = TextureImporterType.Sprite; whiteImporter.spriteImportMode = SpriteImportMode.Single;
            whiteImporter.mipmapEnabled = false; whiteImporter.textureCompression = TextureImporterCompression.Uncompressed; whiteImporter.SaveAndReimport();
            whiteSprite = AssetDatabase.LoadAssetAtPath<Sprite>(whitePath);
            var root = Rect("TrainingPanel", null, Center, Vector2.zero, Vector2.zero);
            Stretch(root);
            try
            {
                var panel = root.gameObject.AddComponent<TrainingPanel>();
                var fields = new SerializedObject(panel);
                fields.FindProperty("panelId").stringValue = "Training";
                fields.FindProperty("layer").intValue = (int)UILayer.Normal;
                var safe = Rect("SafeArea", root, Center, Vector2.zero, Vector2.zero);
                Stretch(safe); Bind(fields, "safeArea", safe);

                var location = Card("Location", safe, new Vector2(0,1), new Vector2(158,-62), new Vector2(252,68));
                Box("Accent", location, new Vector2(-124,0), new Vector2(3,68), Accent);
                Label("Map", location, "DUST II", new Vector2(0,12), new Vector2(212,28), 24, White);
                Label("Mode", location, "沙 2  /  训练场", new Vector2(0,-15), new Vector2(212,24), 16, Muted);

                var timer = Card("Session", safe, new Vector2(.5f,1), new Vector2(0,-62), new Vector2(158,76));
                Bind(fields, "sessionCard", timer.gameObject);
                Bind(fields, "sessionText", Label("Mode", timer, "自由训练", new Vector2(0,20), new Vector2(142,23), 15, Accent, TextAnchor.MiddleCenter));
                Bind(fields, "timerText", Label("Time", timer, "00:00", new Vector2(0,-9), new Vector2(148,42), 32, White, TextAnchor.MiddleCenter));

                var menu = Card("Return", safe, Vector2.one, new Vector2(-116,-51), new Vector2(168,46));
                menu.GetComponent<Image>().raycastTarget = true;
                var button = menu.gameObject.AddComponent<Button>(); button.targetGraphic = menu.GetComponent<Image>();
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                var colors = button.colors; colors.highlightedColor = new Color(1.4f,1.4f,1.4f); button.colors = colors;
                Label("Label", menu, "Esc  训练菜单", Vector2.zero, new Vector2(152,38), 18, White, TextAnchor.MiddleCenter);
                Bind(fields, "returnButton", button);

                var vitals = Card("Vitals", safe, Vector2.zero, new Vector2(212,83), new Vector2(360,102));
                Glyph("HealthIcon", vitals, new Vector2(-143,9), new Vector2(28,28), TrainingHudGlyph.Shape.Health, White);
                Bind(fields, "healthText", Label("Health", vitals, "100", new Vector2(-79,1), new Vector2(92,64), 44, White));
                Label("HealthLabel", vitals, "生命值", new Vector2(-100,38), new Vector2(92,18), 12, Muted);
                Bind(fields, "healthFill", Bar("HealthBar", vitals, new Vector2(-92,-29), new Vector2(138,4), White));
                Box("Divider", vitals, new Vector2(0,2), new Vector2(1,66), new Color(1,1,1,.12f));
                Glyph("ArmorIcon", vitals, new Vector2(35,9), new Vector2(26,29), TrainingHudGlyph.Shape.Armor, Muted);
                Bind(fields, "armorText", Label("Armor", vitals, "0", new Vector2(100,1), new Vector2(92,60), 40, Muted));
                Label("ArmorLabel", vitals, "护甲", new Vector2(101,38), new Vector2(92,18), 12, Muted);
                Bind(fields, "armorFill", Bar("ArmorBar", vitals, new Vector2(90,-29), new Vector2(138,4), Muted));

                var gun = Card("EquippedWeapon", safe, new Vector2(1,0), new Vector2(-180,230), new Vector2(296,120));
                Box("EquippedAccent", gun, new Vector2(146,0), new Vector2(3,120), Accent);
                Bind(fields, "slotText", Label("Slot", gun, "1  主武器", new Vector2(-57,43), new Vector2(152,20), 13, Muted));
                var icon = Rect("WeaponIcon", gun, Center, new Vector2(0,2), new Vector2(254,69)).gameObject.AddComponent<Image>();
                icon.preserveAspect = true; icon.color = White; icon.raycastTarget = false;
                Bind(fields, "weaponIcon", icon);
                Bind(fields, "weaponText", Label("WeaponName", gun, "M4A1-S", new Vector2(-1,-40), new Vector2(260,30), 19, White, TextAnchor.MiddleRight));

                var ammo = Card("Ammunition", safe, new Vector2(1,0), new Vector2(-180,92), new Vector2(296,120));
                var numbers = Rect("AmmoNumbers", ammo, Center, Vector2.zero, new Vector2(296,120));
                Bind(fields, "ammoNumbers", numbers.gameObject);
                Glyph("AmmoIcon", numbers, new Vector2(-119,15), new Vector2(21,27), TrainingHudGlyph.Shape.Ammo, Muted);
                Bind(fields, "magazineText", Label("Magazine", numbers, "20", new Vector2(-54,16), new Vector2(94,66), 56, White, TextAnchor.MiddleRight));
                Label("Slash", numbers, "/", new Vector2(11,13), new Vector2(24,50), 34, Muted, TextAnchor.MiddleCenter);
                Bind(fields, "reserveText", Label("Reserve", numbers, "∞", new Vector2(66,12), new Vector2(82,50), 38, Muted));
                Bind(fields, "ammoFill", Bar("MagazineBar", numbers, new Vector2(0,-25), new Vector2(256,3), White));
                Bind(fields, "ammoStatus", Label("Status", ammo, "R  换弹  ·  无限备弹", new Vector2(0,-43), new Vector2(264,25), 16, Accent, TextAnchor.MiddleRight));
                var reload = Rect("Reload", safe, new Vector2(1,0), new Vector2(-180,159), new Vector2(296,5));
                Bind(fields, "reloadTrack", reload.gameObject);
                Bind(fields, "reloadFill", Bar("Progress", reload, Vector2.zero, new Vector2(296,5), Accent));
                reload.gameObject.SetActive(false);

                var controls = Rect("Controls", safe, new Vector2(.5f,0), new Vector2(0,65), new Vector2(560,70));
                var help = Label("Instructions", controls, "", Vector2.zero, new Vector2(560,70), 16, new Color32(216,225,225,210), TextAnchor.MiddleCenter);
                Bind(fields, "controlsText", help);
                var shadow = help.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0,0,0,.9f); shadow.effectDistance = new Vector2(1,-1);

                var aim = Rect("Crosshair", root, Center, Vector2.zero, new Vector2(30,30));
                Bind(fields, "crosshair", aim);
                for (int i = 0; i < 4; i++)
                {
                    bool horizontal = i < 2;
                    Vector2 position = horizontal ? new Vector2(i == 0 ? -9 : 9, 0) : new Vector2(0, i == 2 ? -9 : 9);
                    Vector2 size = horizontal ? new Vector2(8,2) : new Vector2(2,8);
                    Box("Outline" + i, aim, position, size + new Vector2(2,2), new Color(0,0,0,.8f));
                    Box("Arm" + i, aim, position, size, new Color32(180,239,206,255));
                }
                aim.gameObject.SetActive(false);
                fields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("TRAINING_HUD_BUILD_PASS");
            }
            finally { Object.DestroyImmediate(root.gameObject); }
            BuildTrainingMenu.Build();
        }

        private static void BuildIcons()
        {
            Directory.CreateDirectory(IconFolder); AssetDatabase.Refresh();
            var loadout = Resources.Load<LobbyLoadoutCatalog>("Loadout/LobbyCatalog");
            var weapons = Resources.Load<TrainingWeaponCatalog>("Training/TrainingWeapons");
            if (loadout == null || weapons == null) throw new InvalidOperationException("Build the loadout and training weapon catalogs first.");
            var preview = EditorSceneManager.NewPreviewScene();
            var cameraObject = new GameObject("HUD Icon Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            var camera = cameraObject.AddComponent<Camera>();
            camera.scene = preview;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(preview);
            camera.enabled = false; camera.cameraType = CameraType.Preview;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.orthographic = true; camera.nearClipPlane = .01f; camera.farClipPlane = 10; camera.aspect = 4;
            var cameraData = camera.GetUniversalAdditionalCameraData(); cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.SetColor("_BaseColor", Color.white);
            try
            {
                foreach (var definition in weapons.weapons)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(loadout.Weapon(definition.id).prefab, preview);
                    var renderers = instance.GetComponentsInChildren<Renderer>();
                    foreach (var renderer in renderers) renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
                    Bounds bounds = BoundsOf(renderers);
                    Vector3 longAxis = bounds.size.y > bounds.size.x && bounds.size.y > bounds.size.z ? Vector3.up :
                        bounds.size.z > bounds.size.x ? Vector3.forward : Vector3.right;
                    instance.transform.rotation = Quaternion.FromToRotation(longAxis, Vector3.right) * instance.transform.rotation;
                    bounds = BoundsOf(renderers);
                    bool fromY = bounds.size.y < bounds.size.z;
                    camera.transform.position = bounds.center + (fromY ? Vector3.up : Vector3.back) * 4;
                    camera.transform.LookAt(bounds.center, fromY ? Vector3.forward : Vector3.up);
                    camera.orthographicSize = Mathf.Max((fromY ? bounds.size.z : bounds.size.y) * .56f, bounds.size.x * .14f);
                    var target = new RenderTexture(512,128,24,RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                    var pixels = new Texture2D(512,128,TextureFormat.RGBA32,false);
                    var previous = RenderTexture.active;
                    try
                    {
                        camera.targetTexture = target;
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                        RenderTexture.active = target; pixels.ReadPixels(new Rect(0,0,512,128),0,0); pixels.Apply();
                        var colors = pixels.GetPixels32();
                        if (colors.Count(p => p.a > 128) < 200 || colors.Count(p => p.a < 8) < 200)
                            throw new InvalidOperationException("Weapon silhouette render is empty or lacks transparency: " + definition.id);
                        string path = IconFolder + "/" + definition.id + ".png";
                        File.WriteAllBytes(path, pixels.EncodeToPNG()); AssetDatabase.ImportAsset(path);
                        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                        importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
                        importer.SaveAndReimport(); definition.hudIcon = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    }
                    finally
                    {
                        RenderTexture.active = previous; camera.targetTexture = null; target.Release();
                        Object.DestroyImmediate(target); Object.DestroyImmediate(pixels); Object.DestroyImmediate(instance);
                    }
                }
                EditorUtility.SetDirty(weapons);
            }
            finally { Object.DestroyImmediate(material); EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static Bounds BoundsOf(Renderer[] renderers)
        { var bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds); return bounds; }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.gameObject.layer = 5;
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static RectTransform Card(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        { var rect = Rect(name,parent,anchor,position,size); var image = rect.gameObject.AddComponent<Image>(); image.color = Ink; image.raycastTarget = false; return rect; }
        private static Image Box(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        { var image = Rect(name,parent,Center,position,size).gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image; }
        private static Image Bar(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        {
            var track = Box(name, parent, position, size, new Color(1,1,1,.13f));
            var fill = Box("Fill", track.transform, Vector2.zero, size, color);
            fill.sprite = whiteSprite;
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0; return fill;
        }
        private static Text Label(string name, Transform parent, string value, Vector2 position, Vector2 size, int fontSize, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var label = Rect(name,parent,Center,position,size).gameObject.AddComponent<Text>(); label.font = font; label.text = value;
            label.fontSize = fontSize; label.color = color; label.alignment = alignment; label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow; label.verticalOverflow = VerticalWrapMode.Truncate;
            label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(size.y, label.preferredHeight + 2));
            return label;
        }
        private static void Glyph(string name, Transform parent, Vector2 position, Vector2 size, TrainingHudGlyph.Shape shape, Color color)
        { var glyph = Rect(name,parent,Center,position,size).gameObject.AddComponent<TrainingHudGlyph>(); glyph.Icon = shape; glyph.color = color; glyph.raycastTarget = false; }
        private static void Bind(SerializedObject fields, string name, Object value)
        { fields.FindProperty(name).objectReferenceValue = value; }
    }
}
