using System;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Features.Battle;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Simulation;
using OperationBlacktide.BattleServer;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OperationBlacktide.EditorTools
{
    public static class BuildBattleArena
    {
        public const string ServerScene = "Assets/Scenes/BattleServer.unity";
        public const string MapPrefab = "Assets/Art/Maps/DustII/Prefabs/DustII.prefab";
        public const string ShotMaterialPath = "Assets/Art/Combat/NetworkTracer.mat";
        [MenuItem("Operation Blacktide/Battle/Generate arena scenes")]
        public static void Prepare() { PrepareScenes(true); }
        public static void PrepareArenaOnly() { PrepareScenes(false); }
        private static void PrepareScenes(bool includeDemolition)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log("LAUNCHER_PROGRESS 50 准备战斗界面和场景");
            BuildDemolitionUI.Build();
            Debug.Log("LAUNCHER_PROGRESS 55 战斗界面已生成，正在生成客户端场景");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var map = CreateMap();
            Vector3[] bombSites = includeDemolition ? BuildDemolitionMap.FindSites(map) : Array.Empty<Vector3>();
            string[] clientCollision = CollisionSignature(map);
            Vector3[] spawns = FindSpawns();
            float killHeight = map.GetComponentsInChildren<Collider>().Min(c => c.bounds.min.y) - 10;
            map.AddComponent<ArenaMap>().Configure(spawns, killHeight);
            ConfigureLighting();
            var client = new GameObject("Network Battle Arena").AddComponent<BattleArenaClient>();
            client.SetFont(AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BlacktideUI.ttf"));
            var shotMaterial = AssetDatabase.LoadAssetAtPath<Material>(ShotMaterialPath);
            if (shotMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) throw new InvalidOperationException("Network tracer shader is missing.");
                shotMaterial = new Material(shader) { name = "NetworkTracer", color = Color.white };
                AssetDatabase.CreateAsset(shotMaterial, ShotMaterialPath);
            }
            client.SetShotMaterial(shotMaterial);
            new GameObject("Demolition battle client").AddComponent<OperationBlacktide.Client.Features.Demolition.DemolitionBattleClient>()
                .Configure(AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BlacktideUI.ttf"), shotMaterial);
            EditorSceneManager.SaveScene(scene, GameSceneFlow.BattleScenePath);
            Debug.Log("LAUNCHER_PROGRESS 60 客户端场景已保存，正在生成服务端场景");
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            map = CreateMap();
            // Preserve the prefab transforms, layers and colliders; the server needs no rendering assets.
            PrefabUtility.UnpackPrefabInstance(map, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var renderer in map.GetComponentsInChildren<Renderer>(true)) UnityEngine.Object.DestroyImmediate(renderer);
            foreach (var filter in map.GetComponentsInChildren<MeshFilter>(true)) UnityEngine.Object.DestroyImmediate(filter);
            if (!clientCollision.SequenceEqual(CollisionSignature(map)))
                throw new InvalidOperationException("Dust II client/server collision geometry differs.");
            map.AddComponent<ArenaMap>().Configure(spawns, killHeight);
            ValidateSpawns(spawns);
            map.AddComponent<BattleServerEntry>();
            Debug.Log("LAUNCHER_PROGRESS 65 服务端碰撞检查完成，正在烘焙导航");
            BuildBattleNavigation.Bake(map, spawns);
            Debug.Log("LAUNCHER_PROGRESS 70 导航烘焙完成，正在保存场景");
            if (includeDemolition)
            {
                var demolition = BuildDemolitionMap.Configure(map, bombSites);
                var attackSpawns = demolition.AttackSpawns; var defendSpawns = demolition.DefendSpawns; var sites = demolition.Sites;
                var attackYaw = demolition.AttackYaw; var defendYaw = demolition.DefendYaw;
                var routes = demolition.OpeningRoutes; var holds = demolition.SiteHolds;
                EditorSceneManager.SaveScene(scene, ServerScene);
                scene = EditorSceneManager.OpenScene(GameSceneFlow.BattleScenePath);
                var clientRules = UnityEngine.Object.FindObjectOfType<ArenaMap>().gameObject.AddComponent<DemolitionMap>();
                clientRules.AttackSpawns = attackSpawns; clientRules.DefendSpawns = defendSpawns; clientRules.Sites = sites;
                clientRules.AttackYaw = attackYaw; clientRules.DefendYaw = defendYaw;
                clientRules.OpeningRoutes = routes; clientRules.SiteHolds = holds;
                EditorSceneManager.SaveScene(scene);
            }
            else EditorSceneManager.SaveScene(scene, ServerScene);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ServerScene && s.path != GameSceneFlow.BattleScenePath).ToList();
            scenes.Add(new EditorBuildSettingsScene(GameSceneFlow.BattleScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("BATTLE_MAP_PASS map=" + BattleProtocol.MapId + " spawns=" + spawns.Length);
        }

        private static GameObject CreateMap()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefab);
            if (prefab == null) throw new InvalidOperationException("Dust II prefab is missing: " + MapPrefab);
            var map = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Physics.SyncTransforms();
            return map;
        }

        private static Vector3[] FindSpawns()
        {
            // Nearby slots make it easy for two test clients to find each other.
            var points = new Vector3[BattleProtocol.MaxPlayers];
            float[] lanes = { 0, 6, -2, 8, -4, 10, -6, 4 };
            for (int i = 0; i < points.Length; i++)
            {
                var origin = new Vector3(-10, 5, lanes[i]);
                if (!Physics.Raycast(origin, Vector3.down, out var ground, 15, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    || ground.normal.y < .85f)
                    throw new InvalidOperationException("Dust II spawn has no walkable ground: " + i);
                points[i] = ground.point + Vector3.up * .08f;
            }
            ValidateSpawns(points);
            return points;
        }

        private static string[] CollisionSignature(GameObject map)
        {
            return map.GetComponentsInChildren<Collider>(true).Select(c =>
            {
                var mesh = c as MeshCollider;
                return c.GetType().Name + ":" + c.gameObject.layer + ":" + c.enabled + ":" + c.isTrigger + ":" +
                    c.gameObject.activeInHierarchy + ":" + c.transform.localToWorldMatrix.ToString("R") + ":" +
                    (mesh != null ? mesh.sharedMesh.GetInstanceID() + ":" + mesh.convex + ":" + mesh.cookingOptions : c.bounds.ToString("R"));
            }).ToArray();
        }

        private static void ValidateSpawns(Vector3[] points)
        {
            Physics.SyncTransforms();
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 p = points[i];
                if (Physics.CheckCapsule(p + Vector3.up * .35f, p + Vector3.up * 1.45f, .35f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    throw new InvalidOperationException("Dust II spawn is obstructed: " + i + " " + p);
                if (!Physics.Raycast(p + Vector3.up * .1f, Vector3.down, out var ground, .25f)
                    || ground.normal.y < .85f)
                    throw new InvalidOperationException("Dust II client/server spawn ground mismatch: " + i);
            }
        }

        private static void ConfigureLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.47f, .55f, .65f);
            RenderSettings.ambientEquatorColor = new Color(.32f, .33f, .34f);
            RenderSettings.ambientGroundColor = new Color(.2f, .18f, .15f);
            RenderSettings.fog = false;
            var light = new GameObject("Dust II Daylight").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(55, -25, 0);
            light.shadows = LightShadows.Soft;
            RenderSettings.sun = light;
        }
        [MenuItem("Operation Blacktide/Battle/Build headless server")]
        public static void BuildHeadless() { Prepare(); BuildPreparedScenes(new[] { ServerScene }, "BattleServer/Blacktide.BattleServer.exe", false); }
        [MenuItem("Operation Blacktide/Demolition/Build server")]
        public static void BuildDemolition() { Prepare(); BuildPreparedScenes(new[] { ServerScene }, "DemolitionServer/Blacktide.DemolitionServer.exe", false); }
        public static void BuildHeadlessArenaOnly() { PrepareArenaOnly(); BuildPreparedScenes(new[] { ServerScene }, "BattleServer/Blacktide.BattleServer.exe", false); }
        [MenuItem("Operation Blacktide/Battle/Build client")]
        public static void BuildClient() { Prepare(); BuildPreparedScenes(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(), "Client/Blacktide.exe", false); }
        public static void BuildClientArenaOnly() { PrepareArenaOnly(); BuildPreparedScenes(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(), "Client/Blacktide.exe", false); }
        [MenuItem("Operation Blacktide/Battle/Build dedicated server (module required)")]
        public static void BuildDedicated() { Prepare(); BuildPreparedScenes(new[] { ServerScene }, "BattleServer/Blacktide.BattleServer.exe", true); }
        public static void BuildDedicatedArenaOnly() { PrepareArenaOnly(); BuildPreparedScenes(new[] { ServerScene }, "BattleServer/Blacktide.BattleServer.exe", true); }
        public static void BuildPreparedScenes(string[] scenes, string relative, bool dedicated, bool reportProgress = true)
        {
            string[] args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, "--output-root");
            string output = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.GetFullPath("Builds");
            var backend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone);
            bool server = dedicated || scenes.Contains(ServerScene);
            int quality = QualitySettings.GetQualityLevel();
            var pipeline = GraphicsSettings.defaultRenderPipeline;
            var qualityPipelines = Enumerable.Range(0, QualitySettings.names.Length).Select(QualitySettings.GetRenderPipelineAssetAt).ToArray();
            try
            {
                // A headless server must not deserialize client renderer features from the quality profiles.
                if (server)
                {
                    GraphicsSettings.defaultRenderPipeline = null;
                    for (int level = 0; level < qualityPipelines.Length; level++)
                    { QualitySettings.SetQualityLevel(level, false); QualitySettings.renderPipeline = null; }
                    QualitySettings.SetQualityLevel(quality, false);
                }
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
                if (reportProgress) Debug.Log("LAUNCHER_PROGRESS 75 正在编译并打包：" + relative);
                var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = Path.Combine(output, relative), target = BuildTarget.StandaloneWindows64,
                    subtarget = (int)(dedicated ? StandaloneBuildSubtarget.Server : StandaloneBuildSubtarget.Player), extraScriptingDefines = server ? new[] { "BLACKTIDE_SERVER" } : Array.Empty<string>(), options = BuildOptions.Development });
                if (result.summary.result != BuildResult.Succeeded) throw new Exception("Battle build failed: " + result.summary.result);
                if (reportProgress) Debug.Log("LAUNCHER_PROGRESS 95 构建产物已生成：" + relative);
                Debug.Log("BATTLE_BUILD_PASS " + relative);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, backend);
                if (server)
                {
                    GraphicsSettings.defaultRenderPipeline = pipeline;
                    for (int level = 0; level < qualityPipelines.Length; level++)
                    { QualitySettings.SetQualityLevel(level, false); QualitySettings.renderPipeline = qualityPipelines[level]; }
                    QualitySettings.SetQualityLevel(quality, false);
                }
            }
        }
    }
}
