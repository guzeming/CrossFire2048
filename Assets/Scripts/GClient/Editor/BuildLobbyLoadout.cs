using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Account;
using OperationBlacktide.Client.Features.Lobby;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OperationBlacktide.Client.Editor
{
    /// <summary>Imports the lobby export manifest, builds typed catalog assets and wires the existing lobby.</summary>
    public static class BuildLobbyLoadout
    {
        private const string Art = "Assets/Art/Loadout";
        private const string CatalogPath = "Assets/Resources/Loadout/LobbyCatalog.asset";
        [Serializable] private class Manifest { public Entry[] agents, weapons, profiles; }
        [Serializable] private class Entry
        {
            public string id, name, slot, profile;
            public int team, category;
            public bool held;
            public MaterialInfo[] materials;
            public ClipInfo[] clips;
        }
        [Serializable] private class MaterialInfo { public string name, color, normal, orm; }
        [Serializable] private class ClipInfo { public string name; public float first, last; }
        private static Font font;
        private static readonly Color Ink = new Color32(13, 22, 29, 244);
        private static readonly Color Accent = new Color32(255, 164, 54, 255);
        private static readonly Color Muted = new Color32(169, 182, 191, 255);

        [MenuItem("OperationBlacktide/Lobby/Build Character and Equipment Selection")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before importing lobby assets.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Art + "/manifest.json"));
            if (manifest.agents.Length != 10 || manifest.weapons.Length != 47 || manifest.profiles.Length != 26)
                throw new InvalidOperationException("Loadout export is incomplete. Run export_lobby_loadout.py first.");
            foreach (string folder in new[] { Art + "/Materials", Art + "/Prefabs", Art + "/Thumbnails", "Assets/Resources/Loadout" }) Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var catalog = AssetDatabase.LoadAssetAtPath<LobbyLoadoutCatalog>(CatalogPath);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<LobbyLoadoutCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
            // Opening the thumbnail scene can unload assets referenced only by editor locals.
            catalog.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var poses = new List<LobbyPose>();
            foreach (var entry in manifest.profiles)
            {
                string path = Art + "/Animations/" + entry.id + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                ConfigureModel(importer, true);
                importer.clipAnimations = entry.clips.Select(c => new ModelImporterClipAnimation
                {
                    name = c.name, takeName = "Scene", firstFrame = c.first, lastFrame = c.last,
                    loopTime = c.name == "Idle", keepOriginalPositionY = true
                }).ToArray();
                importer.SaveAndReimport();
                var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().ToArray();
                poses.Add(new LobbyPose { id = entry.id, idle = clips.Single(c => c.name == "Idle"), inspect = clips.Single(c => c.name == "Inspect") });
            }
            catalog.poses = poses.ToArray();
            catalog.controller = Controller(catalog.Pose("m4"));
            Debug.Log("LOADOUT_IMPORT: animation profiles ready");
            var agents = new List<LobbyAgent>();
            foreach (var entry in manifest.agents)
            {
                var model = ImportModel(entry, "Characters");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                var animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = catalog.controller;
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                catalog.Pose("m4").idle.SampleAnimation(instance, 0);
                var bounds = MeshBounds(instance);
                float scale = 1.95f / bounds.size.y;
                var wrapper = new GameObject(entry.id);
                instance.transform.SetParent(wrapper.transform, false);
                instance.transform.localScale *= scale;
                instance.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
                foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.updateWhenOffscreen = true;
                var prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, Art + "/Prefabs/" + entry.id + ".prefab");
                agents.Add(new LobbyAgent { id = entry.id, displayName = entry.name, team = (LobbyTeam)entry.team, prefab = prefab });
                Object.DestroyImmediate(wrapper);
            }
            catalog.agents = agents.ToArray();
            var weapons = new List<LobbyWeapon>();
            foreach (var entry in manifest.weapons)
            {
                var model = ImportModel(entry, "Weapons");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                GameObject offhand = null;
                var leftMount = instance.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "WeaponMountLeft");
                if (leftMount != null)
                {
                    leftMount.SetParent(null, false);
                    leftMount.name = entry.id + "_left";
                    offhand = PrefabUtility.SaveAsPrefabAsset(leftMount.gameObject, Art + "/Prefabs/" + entry.id + "_left.prefab");
                    Object.DestroyImmediate(leftMount.gameObject);
                }
                var mount = instance.GetComponentsInChildren<Transform>().Single(t => t.name == "WeaponMount");
                // Retain the FBX-authored local hand attachment, including its units and bone-axis correction.
                var position = mount.localPosition; var rotation = mount.localRotation; var scale = mount.localScale;
                mount.SetParent(null, false);
                mount.localPosition = position; mount.localRotation = rotation; mount.localScale = scale;
                mount.name = entry.id;
                var prefab = PrefabUtility.SaveAsPrefabAsset(mount.gameObject, Art + "/Prefabs/" + entry.id + ".prefab");
                weapons.Add(new LobbyWeapon { id = entry.id, displayName = entry.name, category = (LoadoutCategory)entry.category,
                    slot = entry.slot, teams = entry.team, profile = entry.profile, held = entry.held, prefab = prefab, offhandPrefab = offhand });
                Object.DestroyImmediate(mount.gameObject); Object.DestroyImmediate(instance);
            }
            catalog.weapons = weapons.OrderBy(w => (int)w.category).ThenBy(w => SlotOrder(w.slot)).ThenBy(w => w.id).ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("LOADOUT_IMPORT: all models ready");
            BuildThumbnails(catalog);
            BuildUI(catalog);
            EditorSceneManager.OpenScene("Assets/Scenes/LobbyScene.unity");
            catalog.hideFlags = HideFlags.None;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("LOADOUT_BUILD_PASS: 10 agents, 47 inventory items, 26 shared idle/inspection profiles, UI and scene.");
        }

        private static int SlotOrder(string slot)
        {
            string[] order = { "pistol.start", "pistol.dual", "pistol.p250", "pistol.fast", "pistol.heavy", "rifle.budget", "rifle.main", "rifle.scope", "rifle.scout", "rifle.awp", "rifle.auto", "smg.light", "smg.silenced", "smg.ump", "smg.p90", "smg.bizon", "heavy.pump", "heavy.auto", "heavy.close", "heavy.m249", "heavy.negev", "gear.knife", "gear.flash", "gear.he", "gear.smoke", "gear.decoy", "gear.fire", "gear.taser", "gear.armor", "gear.helmet", "gear.defuser", "gear.c4" };
            return Array.IndexOf(order, slot);
        }

        private static void ConfigureModel(ModelImporter importer, bool animation)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importAnimation = animation;
            importer.importCameras = importer.importLights = false;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.animationRotationError = importer.animationPositionError = .05f;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }

        private static GameObject ImportModel(Entry entry, string folder)
        {
            string path = Art + "/" + folder + "/" + entry.id + ".fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            ConfigureModel(importer, false);
            foreach (var info in entry.materials)
            {
                string name = string.Concat((entry.id + "_" + info.name).Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
                string materialPath = Art + "/Materials/" + name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, materialPath); }
                material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_BaseMap", Texture(info.color, false));
                material.SetTexture("_BumpMap", Texture(info.normal, true));
                material.SetFloat("_BumpScale", .7f);
                if (!string.IsNullOrEmpty(info.normal)) material.EnableKeyword("_NORMALMAP");
                material.SetFloat("_Metallic", folder == "Weapons" ? .55f : .08f);
                material.SetFloat("_Smoothness", .35f);
                if (info.name.Contains("lenses")) { material.SetColor("_BaseColor", new Color(.045f,.065f,.075f)); material.SetFloat("_Metallic", .75f); material.SetFloat("_Smoothness", .9f); }
                EditorUtility.SetDirty(material);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), info.name), material);
            }
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static Texture2D Texture(string name, bool normal)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string path = Art + "/Textures/" + name;
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type || importer.maxTextureSize != 2048 || importer.sRGBTexture == normal)
            {
                importer.textureType = type; importer.sRGBTexture = !normal; importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static AnimatorController Controller(LobbyPose pose)
        {
            string path = Art + "/LobbyLoadout.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            if (controller.layers.Length == 0) controller.AddLayer("Base Layer");
            var machine = controller.layers[0].stateMachine;
            foreach (var child in machine.states) machine.RemoveState(child.state);
            var idle = machine.AddState("Idle"); idle.motion = pose.idle;
            var inspect = machine.AddState("Inspect"); inspect.motion = pose.inspect;
            machine.defaultState = idle;
            foreach (var transition in new[] { idle.AddTransition(inspect), inspect.AddTransition(idle) })
            { transition.hasExitTime = true; transition.exitTime = 1; transition.hasFixedDuration = true; transition.duration = .35f; transition.canTransitionToSelf = false; }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static Bounds MeshBounds(GameObject instance)
        {
            var bounds = new Bounds(); bool first = true;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                Mesh mesh;
                if (renderer is SkinnedMeshRenderer skin) { mesh = new Mesh(); skin.BakeMesh(mesh, true); }
                else mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                foreach (var vertex in mesh.vertices)
                {
                    var point = renderer.transform.TransformPoint(vertex);
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                }
                if (renderer is SkinnedMeshRenderer) Object.DestroyImmediate(mesh);
            }
            return bounds;
        }

        private static void BuildThumbnails(LobbyLoadoutCatalog catalog)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.55f,.58f,.65f);
            var camera = new GameObject("Catalog Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.065f,.09f,.12f,1);
            camera.orthographic = true; camera.nearClipPlane = .01f; camera.farClipPlane = 50;
            var light = new GameObject("Catalog Light").AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 2; light.transform.rotation = Quaternion.Euler(30,-35,0);
            foreach (var agent in catalog.agents) agent.portrait = Thumbnail(agent.id, agent.prefab, true, camera, catalog.Pose("m4").idle);
            foreach (var weapon in catalog.weapons) weapon.thumbnail = Thumbnail(weapon.id, weapon.prefab, false, camera, null);
            EditorUtility.SetDirty(catalog);
        }

        [MenuItem("OperationBlacktide/Lobby/Refresh Equipment Thumbnails")]
        public static void RefreshThumbnails()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before refreshing thumbnails.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var catalog = AssetDatabase.LoadAssetAtPath<LobbyLoadoutCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("Build the loadout catalog first.");
            catalog.hideFlags = HideFlags.DontUnloadUnusedAsset;
            BuildThumbnails(catalog);
            catalog.hideFlags = HideFlags.None;
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        private static Sprite Thumbnail(string id, GameObject prefab, bool character, Camera camera, AnimationClip clip)
        {
            string path = Art + "/Thumbnails/" + id + ".png";
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                if (character) clip.SampleAnimation(instance.GetComponentInChildren<Animator>().gameObject, 0);
                var bounds = MeshBounds(instance);
                if (!character)
                {
                    // Hand mounts have arbitrary bone axes; keep inventory silhouettes horizontal.
                    instance.transform.localScale /= Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    bounds = MeshBounds(instance);
                    Vector3 longAxis = bounds.size.y > bounds.size.x && bounds.size.y > bounds.size.z ? Vector3.up :
                        bounds.size.z > bounds.size.x ? Vector3.forward : Vector3.right;
                    instance.transform.rotation = Quaternion.FromToRotation(longAxis, Vector3.right) * instance.transform.rotation;
                    bounds = MeshBounds(instance);
                }
                var focus = character ? new Vector3(bounds.center.x, bounds.max.y - .4f, bounds.center.z) : bounds.center;
                bool viewFromY = !character && bounds.size.y < bounds.size.z;
                camera.transform.position = focus + (character ? new Vector3(.15f,.10f,4) : viewFromY ? Vector3.up*4 : Vector3.back*4);
                camera.transform.LookAt(focus, viewFromY ? Vector3.forward : Vector3.up);
                camera.orthographicSize = character ? .52f : Mathf.Max((viewFromY ? bounds.size.z : bounds.size.y)*.65f, bounds.size.x*.29f, .04f);
                camera.aspect = character ? 1f : 2f;
                var rt = new RenderTexture(character ? 256 : 384, character ? 256 : 192, 24);
                camera.targetTexture = rt;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                var previous = RenderTexture.active; RenderTexture.active = rt;
                var pixels = new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); pixels.Apply();
                File.WriteAllBytes(path,pixels.EncodeToPNG());
                RenderTexture.active = previous; camera.targetTexture = null;
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(pixels); Object.DestroyImmediate(instance);
                AssetDatabase.ImportAsset(path);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void BuildUI(LobbyLoadoutCatalog catalog)
        {
            const string path = "Assets/Resources/UI/Panels/LobbyPanel.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BlacktideUI.ttf");
            if (font == null) throw new InvalidOperationException("The lobby UI font is missing. Restore Assets/UI/Fonts/BlacktideUI.ttf before rebuilding.");
            foreach (string name in new[] { "LoadoutTeams", "ChooseAgent", "AgentSelection", "EquipmentSelection" })
            { var old = root.transform.Find(name); if (old != null) Object.DestroyImmediate(old.gameObject); }
            var view = root.GetComponent<LobbyLoadoutView>();
            if (view == null) view = root.AddComponent<LobbyLoadoutView>();
            view.catalog = catalog;
            view.roleName = root.transform.Find("Briefing/Page0/RoleName").GetComponent<Text>();
            view.playControls = root.transform.Find("PlayControls").gameObject;
            var teams = Rect(root.transform,"LoadoutTeams",new Vector2(0,1),new Vector2(235,-226),new Vector2(390,46));
            view.teamControls = teams.gameObject;
            view.ctButton = Button(teams,"CT","CT  反恐精英",new Vector2(-100,0),new Vector2(190,44));
            view.tButton = Button(teams,"T","T  恐怖分子",new Vector2(100,0),new Vector2(190,44));
            view.chooseAgentButton = Button(root.transform,"ChooseAgent","选择角色  ›",new Vector2(235,-286),new Vector2(390,48),new Vector2(0,1));
            var agentPanel = Box(root.transform,"AgentSelection",new Vector2(0,1),new Vector2(235,-624),new Vector2(390,606));
            view.agentsPanel = agentPanel.gameObject;
            Label(agentPanel,"Title","选择角色",new Vector2(-20,267),new Vector2(320,44),24);
            view.closeAgentsButton = Button(agentPanel,"Close","×",new Vector2(163,270),new Vector2(46,38));
            view.agentContent = List(agentPanel,"Agents",new Vector2(0,-12),new Vector2(366,518));
            view.agentTemplate = Row(view.agentContent,"AgentTemplate",366,96,true);
            agentPanel.gameObject.SetActive(false);
            var oldPage = root.transform.Find("Briefing/Page1");
            foreach (Transform child in oldPage.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            var page = Rect(root.transform,"EquipmentSelection",Vector2.zero,Vector2.zero,Vector2.zero);
            page.anchorMin = Vector2.zero; page.anchorMax = Vector2.one; page.offsetMin = page.offsetMax = Vector2.zero;
            view.equipmentPage = page.gameObject;
            Label(page,"Title","装备配置",new Vector2(235,-161),new Vector2(390,48),30,new Vector2(0,1));
            ConfigureCategoryButtons(view, page);
            var slots = Box(page,"Slots",new Vector2(0,1),new Vector2(235,-629),new Vector2(390,610));
            view.slotContent = List(slots,"SlotList",Vector2.zero,new Vector2(374,594));
            view.slotTemplate = Row(view.slotContent,"SlotTemplate",374,84,false);
            var detail = Box(page,"WeaponDetails",new Vector2(1,1),new Vector2(-249,-520),new Vector2(418,760));
            view.selectedName = Label(detail,"WeaponName","M4A1-S",new Vector2(0,332),new Vector2(370,52),32);
            view.selectionHint = Label(detail,"Hint","",new Vector2(0,278),new Vector2(370,52),16);
            view.selectionHint.color = Muted;
            view.optionContent = List(detail,"Options",new Vector2(0,62),new Vector2(378,348));
            view.optionTemplate = Row(view.optionContent,"OptionTemplate",370,152,false);
            view.equipButton = Button(detail,"Equip","装备至 CT",new Vector2(0,-171),new Vector2(370,54));
            view.equipButton.GetComponent<Image>().color = new Color(.34f,.23f,.10f,1);
            view.bothTeamsButton = Button(detail,"EquipBoth","装备至 CT 和 T",new Vector2(0,-229),new Vector2(370,44));
            view.displayButton = Button(detail,"Display","设为大厅展示",new Vector2(-95,-285),new Vector2(180,44));
            view.inspectButton = Button(detail,"Inspect","检视武器",new Vector2(95,-285),new Vector2(180,44));
            view.statusText = Label(detail,"Status","",new Vector2(0,-341),new Vector2(370,38),16); view.statusText.color = Accent;
            page.gameObject.SetActive(false);
            var panel = new SerializedObject(root.GetComponent<LobbyPanel>());
            panel.FindProperty("loadoutView").objectReferenceValue = view; panel.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,path); PrefabUtility.UnloadPrefabContents(root);
        }

        // Patch only the category tabs/catalog, preserving the existing lobby layout and references.
        [MenuItem("OperationBlacktide/Lobby/Update Weapon Categories")]
        public static void UpdateWeaponCategories()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before updating weapon categories.");
            var catalog = AssetDatabase.LoadAssetAtPath<LobbyLoadoutCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("Build the loadout catalog first.");
            foreach (var weapon in catalog.weapons)
                if (weapon.id.StartsWith("weapon_snip_", StringComparison.Ordinal)) weapon.category = LoadoutCategory.Snipers;
            EditorUtility.SetDirty(catalog);
            const string path = "Assets/Resources/UI/Panels/LobbyPanel.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BlacktideUI.ttf");
                if (font == null) throw new InvalidOperationException("Missing lobby UI font.");
                ConfigureCategoryButtons(root.GetComponent<LobbyLoadoutView>(), root.transform.Find("EquipmentSelection"));
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("LOADOUT_CATEGORIES_UPDATED: separate sniper tab, original equipment slot IDs preserved.");
        }

        private static void ConfigureCategoryButtons(LobbyLoadoutView view, Transform page)
        {
            string[] names = { "手枪", "步枪", "微冲", "重型", "装备", "狙击" };
            var order = new[] { LoadoutCategory.Pistols, LoadoutCategory.Rifles, LoadoutCategory.Snipers,
                LoadoutCategory.SMGs, LoadoutCategory.Heavy, LoadoutCategory.Gear };
            // The serialized array remains indexed by enum value for click binding/highlighting.
            Array.Resize(ref view.categories, names.Length);
            for (int column = 0; column < order.Length; column++)
            {
                int index = (int)order[column];
                var position = new Vector2(72.5f + column * 65, -289);
                var size = new Vector2(62, 46);
                var button = view.categories[index];
                if (button == null) button = view.categories[index] = Button(page, "Category" + index, names[index], position, size, new Vector2(0, 1));
                var rect = (RectTransform)button.transform;
                rect.anchoredPosition = position; rect.sizeDelta = size;
                var label = button.GetComponentInChildren<Text>();
                label.text = names[index]; label.rectTransform.sizeDelta = size - new Vector2(12, 4);
            }
        }

        private static RectTransform Rect(Transform parent,string name,Vector2 anchor,Vector2 position,Vector2 size)
        {
            var go = new GameObject(name,typeof(RectTransform)); go.layer=5;
            var rect=(RectTransform)go.transform; rect.SetParent(parent,false); rect.anchorMin=rect.anchorMax=anchor;
            rect.anchoredPosition=position; rect.sizeDelta=size; return rect;
        }
        private static RectTransform Box(Transform parent,string name,Vector2 anchor,Vector2 position,Vector2 size)
        { var rect=Rect(parent,name,anchor,position,size); rect.gameObject.AddComponent<Image>().color=Ink; return rect; }
        private static Text Label(Transform parent,string name,string value,Vector2 position,Vector2 size,int fontSize,Vector2? anchor=null)
        {
            var rect=Rect(parent,name,anchor??new Vector2(.5f,.5f),position,size);
            var text=rect.gameObject.AddComponent<Text>(); text.font=font; text.text=value; text.fontSize=fontSize;
            text.color=Color.white; text.alignment=TextAnchor.MiddleLeft; text.raycastTarget=false; return text;
        }
        private static Button Button(Transform parent,string name,string value,Vector2 position,Vector2 size,Vector2? anchor=null)
        {
            var rect=Box(parent,name,anchor??new Vector2(.5f,.5f),position,size);
            var button=rect.gameObject.AddComponent<Button>(); button.targetGraphic=rect.GetComponent<Image>();
            var colors=button.colors; colors.highlightedColor=new Color(1.3f,1.3f,1.3f,1); colors.pressedColor=new Color(.7f,.7f,.7f,1); button.colors=colors;
            Label(rect,"Text",value,Vector2.zero,size-new Vector2(24,4),20).alignment=TextAnchor.MiddleCenter;
            return button;
        }
        private static RectTransform List(Transform parent,string name,Vector2 position,Vector2 size)
        {
            var rect=Rect(parent,name,new Vector2(.5f,.5f),position,size);
            rect.gameObject.AddComponent<Image>().color=new Color(0,0,0,.01f);
            rect.gameObject.AddComponent<RectMask2D>();
            var content=Rect(rect,"Content",new Vector2(.5f,1),Vector2.zero,new Vector2(size.x,0)); content.pivot=new Vector2(.5f,1);
            var layout=content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing=8; layout.childControlWidth=true; layout.childControlHeight=true; layout.childForceExpandHeight=false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            var scroll=rect.gameObject.AddComponent<ScrollRect>(); scroll.viewport=rect; scroll.content=content; scroll.horizontal=false; scroll.movementType=ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity=28; return content;
        }
        private static LobbyLoadoutRow Row(RectTransform parent,string name,float width,float height,bool portrait)
        {
            var button=Button(parent,name,"",Vector2.zero,new Vector2(width,height));
            Object.DestroyImmediate(button.transform.Find("Text").gameObject);
            var element=button.gameObject.AddComponent<LayoutElement>(); element.preferredWidth=width; element.preferredHeight=height;
            var row=button.gameObject.AddComponent<LobbyLoadoutRow>(); row.button=button;
            float imageWidth=portrait?82:132;
            var icon=Rect(button.transform,"Icon",new Vector2(0,.5f),new Vector2(imageWidth*.5f+8,0),new Vector2(imageWidth,height-10));
            row.icon=icon.gameObject.AddComponent<Image>(); row.icon.preserveAspect=true; row.icon.raycastTarget=false;
            float textWidth=width-imageWidth-34;
            row.title=Label(button.transform,"Name","",new Vector2(imageWidth+20+textWidth*.5f,13),new Vector2(textWidth,40),21,new Vector2(0,.5f));
            row.subtitle=Label(button.transform,"Detail","",new Vector2(imageWidth+20+textWidth*.5f,-20),new Vector2(textWidth,30),15,new Vector2(0,.5f)); row.subtitle.color=Muted;
            var accent=Rect(button.transform,"Accent",new Vector2(0,.5f),new Vector2(2,0),new Vector2(3,height-12)); row.accent=accent.gameObject.AddComponent<Image>(); row.accent.color=Accent; row.accent.raycastTarget=false;
            row.gameObject.SetActive(false); return row;
        }
    }
}
