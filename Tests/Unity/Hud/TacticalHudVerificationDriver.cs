#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using OperationBlacktide.Client.Content;
using OperationBlacktide.Client.Features.Demolition;
using OperationBlacktide.Client.Features.Training;
using OperationBlacktide.Client.Gameplay.UI;
using OperationBlacktide.Client.Gameplay.Vision;
using OperationBlacktide.Client.Gameplay.Combat;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Modes.Demolition;
using OperationBlacktide.Simulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class TacticalHudVerificationDriver : MonoBehaviour
{
    string output;
    bool done;
    float deadline;
    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        var args = Environment.GetCommandLineArgs(); output = args[Array.IndexOf(args, "--test-output") + 1];
        deadline = Time.realtimeSinceStartup + 180;
        Application.logMessageReceived += Log;
    }
    void Log(string text, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Fail(text + "\n" + trace); }
    void Update() { if (!done && Time.realtimeSinceStartup > deadline) Fail("HUD verification timeout"); }
    void Fail(string text) { if (done) return; done = true; File.WriteAllText(Path.Combine(output, "failure.txt"), text); EditorApplication.Exit(1); }
    static void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); Debug.Log("PASS " + text); }
    static void Set(object o, string name, object value) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(o, value);
    static void Invoke(object o, string name, params object[] args) => o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, args);
    static Text Label(Transform root, string path) => root.Find(path).GetComponent<Text>();
    static bool Overlap(RectTransform a, RectTransform b)
    {
        var ac = new Vector3[4]; var bc = new Vector3[4]; a.GetWorldCorners(ac); b.GetWorldCorners(bc);
        return ac[0].x < bc[2].x && ac[2].x > bc[0].x && ac[0].y < bc[2].y && ac[2].y > bc[0].y;
    }
    IEnumerator Capture(Camera camera, string name, int width, int height)
    {
        var canvases = FindObjectsOfType<Canvas>().Where(c => c.isRootCanvas).ToArray();
        var modes = canvases.Select(c => c.renderMode).ToArray(); var cameras = canvases.Select(c => c.worldCamera).ToArray();
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
        for (int i = 0; i < canvases.Length; i++) { canvases[i].renderMode = RenderMode.ScreenSpaceCamera; canvases[i].worldCamera = camera; canvases[i].planeDistance = .5f; }
        Canvas.ForceUpdateCanvases(); yield return null;
        foreach (var hud in FindObjectsOfType<CombatTacticalHud>())
        {
            if (hud.transform.parent.name == "Combat overlay") DemolitionUI.Fit((RectTransform)hud.transform.parent, 0);
            var roster = hud.transform.Find("Team rosters");
            Check(!Overlap((RectTransform)hud.transform.Find("Minimap"), (RectTransform)roster.Find("Player 1")), "radar and roster do not overlap at " + width + "x" + height);
            var clock = hud.transform.parent.Find("Round clock") as RectTransform;
            if (clock != null)
                foreach (Transform child in roster) if (child.name.StartsWith("Player ")) Check(!Overlap(clock, (RectTransform)child), "clock clear of " + child.name);
        }
        Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
        foreach (var hud in FindObjectsOfType<CombatTacticalHud>())
        {
            foreach (var label in hud.GetComponentsInChildren<Text>())
                if (!string.IsNullOrEmpty(label.text)) Check(label.canvasRenderer.GetMesh().vertexCount > 0, "visible text geometry for " + label.name + " at " + width + "x" + height);
            foreach (var marker in hud.GetComponentsInChildren<TacticalMapMarker>())
                Check(marker.canvasRenderer.GetMesh().vertexCount > 0 && !marker.canvasRenderer.cull, "visible radar marker geometry");
        }
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false); pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
        File.WriteAllBytes(Path.Combine(output, name), pixels.EncodeToPNG());
        RenderTexture.active = null; camera.targetTexture = null;
        for (int i = 0; i < canvases.Length; i++) { canvases[i].renderMode = modes[i]; canvases[i].worldCamera = cameras[i]; }
        Destroy(target); Destroy(pixels); yield return null;
    }

    IEnumerator Start()
    {
        yield return null;
        var map = FindObjectOfType<DemolitionMap>();
        Check(map != null && map.Sites.Length == 2 && map.AttackSpawns.Length >= 5 && map.DefendSpawns.Length >= 5, "HUD fixture has sites and player spawns");
        var layout = Resources.Load<CombatMapLayout>("Gameplay/DustIIRadar");
        Check(layout != null && layout.texture != null, "baked tactical map loads from Resources");
        foreach (var position in map.Sites.Concat(map.AttackSpawns).Concat(map.DefendSpawns))
        {
            var uv = layout.Project(position);
            Check(uv.x > 0 && uv.x < 1 && uv.y > 0 && uv.y < 1, "spawns and bomb sites lie inside the radar");
        }
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BlacktideUI.ttf");
        var catalog = Resources.Load<EquipmentCatalog>("Loadout/EquipmentCatalog");
        var battle = new GameObject("HUD snapshot fixture").AddComponent<DemolitionBattleClient>(); battle.enabled = false;
        var seats = Enumerable.Range(1, 10).Select(i => new MatchSeat { Id = i, Team = i <= 5 ? 0 : 1, Name = i == 1 ? "自己" : "队员 " + i, IsBot = i != 1 }).ToArray();
        Set(battle, "match", new MatchAssignment { Seats = seats }); Set(battle, "catalog", catalog); battle.Configure(font, null);
        Invoke(battle, "BuildHud");
        var state = new DemolitionSnapshot { Bomb = BombPhase.Planted, Site = 0, BombPosition = DemolitionMap.Point(map.Sites[0]), Interactor = 6, Progress = .25f,
            Actors = seats.Select(s => { var p = s.Id <= 5 ? map.AttackSpawns[s.Id - 1] : map.DefendSpawns[s.Id - 6]; return new DemolitionActorState {
                Motion = new BattleState { Id = s.Id, Team = s.Id <= 5 ? BattleTeam.T : BattleTeam.CT, Health = 100, X = p.x, Y = p.y, Z = p.z, Yaw = 45 }, Connected = true }; }).ToArray() };
        state.Actors[1].Motion.Health = 67; state.Actors[1].Motion.ReloadTicks = 60;
        state.Actors[2].Motion.Health = 21; state.Actors[3].Motion.Health = 0;
        state.Actors[4].Connected = false; state.Actors[7].Motion.Health = 0; state.Actors[8].Kit = true;
        Invoke(battle, "RefreshTacticalHud", state, 1);
        var hud = FindObjectOfType<CombatTacticalHud>(); var roster = hud.transform.Find("Team rosters"); var radar = hud.transform.Find("Minimap/Map");
        Check(roster.Cast<Transform>().Count(t => t.name.StartsWith("Player ")) == 10, "five portraits on each team");
        Check(roster.GetComponentsInChildren<Image>().Where(i => i.name == "Portrait").All(i => i.sprite != null), "portraits use existing character art");
        Check(Label(roster, "Player 2/Health value").text == "67" && Mathf.Abs(roster.Find("Player 2/Health fill").GetComponent<RectTransform>().sizeDelta.x - 68 * .67f) < .01f, "health number and health bar follow snapshot");
        Check(Label(roster, "Player 2/Status").text == "换弹中" && Label(roster, "Player 4/Status").text == "阵亡" && Label(roster, "Player 5/Status").text == "离线", "reload, death and disconnect states");
        Check(Label(roster, "Player 6/Status").text == "拆弹中" && Label(roster, "Player 9/Status").text == "拆弹器", "bomb interaction and defuse kit states");
        Check(radar.Find("Player marker 1").gameObject.activeSelf && !radar.Find("Player marker 4").gameObject.activeSelf && !radar.Find("Player marker 5").gameObject.activeSelf, "radar hides dead and offline allies");
        for (int i = 6; i <= 10; i++) Check(!radar.Find("Player marker " + i).gameObject.activeSelf, "enemy remains hidden without an active observer");
        Check(hud.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), "tactical HUD cannot block mouse aim or firing");
        var camera = new GameObject("HUD verification camera").AddComponent<Camera>(); camera.tag = "MainCamera"; camera.nearClipPlane = .08f; camera.farClipPlane = 300;
        camera.transform.SetPositionAndRotation(map.AttackSpawns[0] + new Vector3(0, 15, -9), Quaternion.Euler(60, 0, 0));
        var clock = hud.transform.parent.Find("Round clock"); var clockLabels = clock.GetComponentsInChildren<Text>();
        clockLabels[0].text = "03"; clockLabels[1].text = "02"; clockLabels[2].text = "01:24"; clockLabels[3].text = "第 06 回合 / 炸弹已安装";
        yield return Capture(camera, "tactical-1600x900.png", 1600, 900);
        yield return Capture(camera, "tactical-1280x720.png", 1280, 720);
        yield return Capture(camera, "tactical-1920x1200.png", 1920, 1200);
        var positionBefore = ((RectTransform)radar.Find("Player marker 1")).anchoredPosition;
        state.Actors[0].Motion.X += 8; state.Actors[0].Motion.Yaw = 180;
        state.Actors[3].Motion.Health = 100; state.Actors[4].Connected = true;
        foreach (var actor in state.Actors) actor.Motion.Team = actor.Motion.Team == BattleTeam.T ? BattleTeam.CT : BattleTeam.T;
        state.Bomb = BombPhase.Dropped;
        Invoke(battle, "RefreshTacticalHud", state, 1);
        Check(Label(roster, "Player 4/Status").text == "存活" && radar.Find("Player marker 4").gameObject.activeSelf && radar.Find("Player marker 5").gameObject.activeSelf, "respawn and reconnect restore cards and markers");
        Check(((RectTransform)roster.Find("Player 1")).anchoredPosition.x == 0 && ((RectTransform)roster.Find("Player 6")).anchoredPosition.x == 676, "half-time preserves friendly and enemy ordering");
        Check(Vector2.Distance(positionBefore, ((RectTransform)radar.Find("Player marker 1")).anchoredPosition) > 1 && Mathf.Abs(Mathf.DeltaAngle(radar.Find("Player marker 1").localEulerAngles.z, 180)) < .1f, "radar position and heading update");
        Check(!radar.Find("Bomb").gameObject.activeSelf, "defenders do not see dropped bomb position");
        hud.Present(new CombatHudParticipant[0], 1); yield return null;
        Check(roster.Cast<Transform>().All(t => !t.name.StartsWith("Player ")), "departed participant cards are removed");
        yield return VerifyRadarVisibility(hud);

        SceneManager.LoadScene("DustII"); yield return null;
        yield return new WaitUntil(() => FindObjectOfType<TrainingSceneController>()?.Player != null);
        yield return new WaitForSecondsRealtime(.5f);
        var training = FindObjectOfType<TrainingSceneController>(); hud = FindObjectOfType<CombatTacticalHud>();
        Check(hud != null && hud.transform.Find("Minimap/Map").GetComponent<RawImage>().texture != null, "training scene uses shared minimap");
        Check(training.HudParticipants.Count > 1, "training roster contains real target actors");
        training.Vitals.ApplyDamage(35, true); yield return null; yield return null;
        Check(Label(hud.transform, "Team rosters/Player 0/Health value").text == training.Vitals.Health.ToString(), "training damage updates roster health");
        yield return new WaitForSecondsRealtime(.6f);
        yield return Capture(Camera.main, "training-tactical.png", 1600, 900);
        var vision = Camera.main.GetComponent<CharacterVision>();
        var visibleTarget = FindObjectsOfType<TrainingTarget>().FirstOrDefault(t => vision.CanSeeTarget(t));
        Check(visibleTarget != null, "training provides a visible opposing target");
        var participant = training.HudParticipants.First(p => p.WorldTransform == visibleTarget.transform);
        var enemyMarker = hud.transform.Find("Minimap/Map/Player marker " + participant.Id).gameObject;
        Invoke(hud, "LateUpdate");
        Check(enemyMarker.activeSelf, "training visible enemy appears on radar");
        var world = training.Player.GetComponent<WeaponController>().Throwables.World;
        var throwableCatalog = Resources.Load<ThrowableCatalog>("Gameplay/Throwables");
        world.Detonate(throwableCatalog.items.First(t => t.kind == ThrowableKind.Smoke), training.Player.transform.position);
        for (int i = 0; i < 20; i++) world.Simulate(.05f);
        Invoke(hud, "LateUpdate"); Check(!enemyMarker.activeSelf, "smoke hides radar enemy without a new participant snapshot");
        for (int i = 0; i < 400; i++) world.Simulate(.05f);
        Invoke(hud, "LateUpdate"); Check(enemyMarker.activeSelf, "enemy returns when smoke clears");
        world.ApplyFlash(vision.EyePosition + training.Player.transform.forward, 18, 4);
        Invoke(hud, "LateUpdate"); Check(!enemyMarker.activeSelf, "flash blindness hides radar enemy");
        world.ResetSenses(); Invoke(hud, "LateUpdate"); Check(enemyMarker.activeSelf, "enemy returns when blindness ends");
        File.WriteAllText(Path.Combine(output, "result.txt"), "TACTICAL_HUD_VERIFY_PASS"); Debug.Log("TACTICAL_HUD_VERIFY_PASS"); done = true; EditorApplication.Exit(0);
    }

    IEnumerator VerifyRadarVisibility(CombatTacticalHud hud)
    {
        // Above the map: deterministic LOS fixtures, with real network capsules and real physics walls.
        var origin = new Vector3(-30, 100, -15);
        var observer = CharacterSimulation.CreatePawn(101); observer.transform.position = origin;
        var enemy = CharacterSimulation.CreatePawn(102); enemy.transform.position = origin + Vector3.forward * 10;
        var ally = CharacterSimulation.CreatePawn(103); ally.transform.position = origin + new Vector3(6, 0, -8);
        var sightCamera = new GameObject("Radar sight fixture").AddComponent<Camera>(); sightCamera.enabled = false;
        var vision = sightCamera.gameObject.AddComponent<CharacterVision>(); vision.Follow(observer.transform);
        var participants = new[] {
            new CombatHudParticipant { Id = 101, Team = 1, Name = "自己", Health = 100, MaxHealth = 100, HasSnapshot = true, Connected = true, WorldTransform = observer.transform },
            new CombatHudParticipant { Id = 102, Team = 2, Name = "敌人", Health = 100, MaxHealth = 100, HasSnapshot = true, Connected = true, WorldTransform = enemy.transform },
            new CombatHudParticipant { Id = 103, Team = 1, Name = "队友", Health = 100, MaxHealth = 100, HasSnapshot = true, Connected = true, WorldTransform = ally.transform }
        };
        hud.SetObserver(vision, true); Physics.SyncTransforms(); hud.Present(participants, 101);
        var marker = hud.transform.Find("Minimap/Map/Player marker 102").GetComponent<TacticalMapMarker>();
        var allyMarker = hud.transform.Find("Minimap/Map/Player marker 103").gameObject;
        Check(marker.gameObject.activeSelf && marker.color.r > marker.color.b, "visible network enemy uses red radar marker and ignores its own capsule");
        observer.transform.rotation = Quaternion.Euler(0, 180, 0);
        yield return null; yield return null;
        Check(!marker.gameObject.activeSelf && allyMarker.activeSelf, "turning away hides enemy next frame while teammates remain visible");
        observer.transform.rotation = Quaternion.identity;
        yield return null; yield return null;
        Check(marker.gameObject.activeSelf, "turning back reveals enemy without a new network snapshot");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Radar LOS wall";
        wall.transform.position = origin + new Vector3(0, 1.5f, 5); wall.transform.localScale = new Vector3(5, 3, .5f);
        Physics.SyncTransforms(); Invoke(hud, "LateUpdate"); Check(!marker.gameObject.activeSelf, "full-height cover hides enemy radar marker");
        wall.transform.position = origin + new Vector3(0, .3f, 5); wall.transform.localScale = new Vector3(5, .6f, .5f);
        Physics.SyncTransforms(); Invoke(hud, "LateUpdate"); Check(marker.gameObject.activeSelf, "visible head above low cover reveals enemy");
        wall.SetActive(false);
        enemy.transform.position = origin + Vector3.forward * 40;
        Physics.SyncTransforms(); Invoke(hud, "LateUpdate"); Check(!marker.gameObject.activeSelf, "enemy outside sight range stays hidden");
        enemy.transform.position = origin + new Vector3(10, 0, 0);
        Physics.SyncTransforms(); Invoke(hud, "LateUpdate"); Check(!marker.gameObject.activeSelf, "enemy outside view cone stays hidden");
        enemy.transform.position = origin + Vector3.forward * 10;
        Physics.SyncTransforms(); Invoke(hud, "LateUpdate");
        var before = marker.rectTransform.anchoredPosition;
        enemy.transform.position += Vector3.right * 3;
        Physics.SyncTransforms(); Invoke(hud, "LateUpdate");
        Check(marker.gameObject.activeSelf && Vector2.Distance(before, marker.rectTransform.anchoredPosition) > 1, "radar tracks rendered enemy movement between snapshots");
        participants[1].Health = 0; hud.Present(participants, 101); Check(!marker.gameObject.activeSelf, "visible dead enemy stays hidden");
        participants[1].Health = 100; participants[1].Connected = false; hud.Present(participants, 101); Check(!marker.gameObject.activeSelf, "visible disconnected enemy stays hidden");
        participants[1].Connected = true; hud.Present(participants, 101);
        hud.SetObserver(vision, false); Invoke(hud, "LateUpdate"); Check(!marker.gameObject.activeSelf, "dead observer cannot reveal enemies");
        ally.transform.LookAt(enemy.transform); vision.Follow(ally.transform); hud.SetObserver(vision, true);
        Physics.SyncTransforms(); Invoke(hud, "LateUpdate"); Check(marker.gameObject.activeSelf, "spectating uses the followed teammate's view");
        vision.Follow(null); Invoke(hud, "LateUpdate"); Check(!marker.gameObject.activeSelf, "missing sight observer never reveals enemies");
        hud.SetObserver(null, false); hud.Present(Array.Empty<CombatHudParticipant>(), 101);
        Destroy(wall); Destroy(observer.gameObject); Destroy(enemy.gameObject); Destroy(ally.gameObject); Destroy(sightCamera.gameObject);
        yield return null;
    }
    void OnDestroy() { Application.logMessageReceived -= Log; }
}
#endif
