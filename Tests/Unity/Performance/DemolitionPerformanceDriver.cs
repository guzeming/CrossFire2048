#if BLACKTIDE_PERFORMANCE && !BLACKTIDE_SERVER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Features.Demolition;
using OperationBlacktide.Client.Gameplay.Cameras;
using OperationBlacktide.Client.Gameplay.Vision;
using OperationBlacktide.Client.Network;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Shared.Modes.Demolition;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

[DefaultExecutionOrder(-250)]
public sealed class DemolitionPerformanceDriver : MonoBehaviour
{
    static readonly string[] Phases = { "idle", "traverse_orbit", "combat" };
    string output, variant, viewMode;
    DemolitionBattleClient client;
    DemolitionConnection connection;
    PerformanceCapture capture;
    readonly List<Vector3> trail = new List<Vector3>(2048);
    int waypoint, phase, shots, lastFirePulse = -1, leader = 2, lastRound;
    double startAt, deadline, sampleAfter, phaseStarted;
    bool finished, sampling;
    Vector3 previousPosition;
    float previousCameraYaw;
    readonly int[] phaseShots = new int[3];
    float distance, orbitDegrees;
    readonly List<ScriptableRendererFeature> disabledFeatures = new List<ScriptableRendererFeature>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindObjectOfType<DemolitionPerformanceDriver>() != null) return;
        var go = new GameObject("Demolition performance driver"); DontDestroyOnLoad(go); go.AddComponent<DemolitionPerformanceDriver>();
    }
    void Awake()
    {
        output = PerformanceCapture.Argument("--perf-output"); variant = PerformanceCapture.Argument("--perf-variant", "baseline");
        viewMode = PerformanceCapture.Argument("--perf-view", "topdown");
        Directory.CreateDirectory(output);
        deadline = Time.realtimeSinceStartupAsDouble + 240;
        Application.runInBackground = true;
        Application.logMessageReceived += OnLog;
    }
    IEnumerator Start()
    {
        var routine = Run();
        while (!finished)
        {
            object next;
            try { if (!routine.MoveNext()) break; next = routine.Current; }
            catch (Exception ex) { Fail(ex.ToString()); yield break; }
            yield return next;
        }
    }
    IEnumerator Run()
    {
        QualitySettings.SetQualityLevel(variant == "balanced" ? 1 : 2, true);
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        // DisplaySettings may restore a user's saved resolution before this scene. Override in memory only.
        Screen.SetResolution(int.Parse(PerformanceCapture.Argument("-screen-width", "1600")),
            int.Parse(PerformanceCapture.Argument("-screen-height", "900")), FullScreenMode.Windowed);
        var manifest = JsonUtility.FromJson<DemolitionManifest>(File.ReadAllText(PerformanceCapture.Argument("--demolition-manifest")));
        var assignment = new MatchAssignment { MatchId = manifest.MatchId, Host = "127.0.0.1", Port = manifest.Port, HasAI = true, Seats = manifest.Seats,
            Ticket = DemolitionTicket.Issue(File.ReadAllBytes(manifest.KeyFile), manifest.MatchId, manifest.Seats[0].UserId, DateTimeOffset.UtcNow.ToUnixTimeSeconds()) };
        typeof(DemolitionGateway).GetProperty("PendingMatch").GetSetMethod(true).Invoke(null, new object[] { assignment });
        new GameObject("Performance UI root").AddComponent<UIRoot>();
        yield return SceneManager.LoadSceneAsync(GameSceneFlow.BattleScenePath);
        client = FindObjectOfType<DemolitionBattleClient>();
        if (client == null || !client.enabled) throw new InvalidOperationException("Demolition client did not consume the test assignment.");
        client.InputProvider = Controls;
        yield return new WaitUntil(() => client.Ready);
        connection = (DemolitionConnection)typeof(DemolitionBattleClient).GetField("connection", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(client);
        connection.Shot += OnShot;
        connection.Disconnected += Fail;
        previousPosition = client.LocalPlayer.transform.position;
        previousCameraYaw = client.FollowCamera.transform.eulerAngles.y;
        client.FollowCamera.SetFirstPerson(viewMode == "firstperson");
        ApplyVariant();
        capture = new PerformanceCapture();
        startAt = Time.realtimeSinceStartupAsDouble;
        float seconds = float.Parse(PerformanceCapture.Argument("--perf-seconds", "20"), System.Globalization.CultureInfo.InvariantCulture);
        deadline = startAt + 3 * (seconds + 3) + 50;
        yield return new WaitForSecondsRealtime(12);
        if (Screen.width != int.Parse(PerformanceCapture.Argument("-screen-width", "1600")) ||
            Screen.height != int.Parse(PerformanceCapture.Argument("-screen-height", "900")))
            throw new InvalidOperationException("Client render resolution differs from requested resolution.");
        for (phase = 0; phase < Phases.Length; phase++)
        {
            sampling = false; phaseStarted = Time.realtimeSinceStartupAsDouble;
            Debug.Log("PERFORMANCE_PHASE_START " + variant + " " + Phases[phase]);
            ScreenCapture.CaptureScreenshot(Path.Combine(output, Phases[phase] + ".png"));
            yield return new WaitForSecondsRealtime(3); // Exclude capture, phase transition and shader/resource warmup.
            sampleAfter = Time.realtimeSinceStartupAsDouble + .1;
            int shotsBefore = shots;
            sampling = true;
            yield return new WaitForSecondsRealtime(seconds);
            phaseShots[phase] = shots - shotsBefore;
        }
        sampling = false;
        capture.Save(output, "client", Phases);
        File.WriteAllText(Path.Combine(output, "coverage.json"), JsonUtility.ToJson(new Coverage {
            variant = variant, view = viewMode, shots = shots, traveledMetres = distance, orbitDegrees = orbitDegrees,
            actors = client.State.Actors.Length, matchSeed = manifest.MatchId, phases = Phases,
            phaseShotEvents = phaseShots,
            movementCovered = distance > 3, combatCovered = phaseShots[2] > 0, rotationCovered = orbitDegrees > 90,
            notes = "One scripted local player and nine real server bots. Original round/damage rules. Authentication/lobby skipped; match UDP, prediction, map, HUD and effects are production code. Bot outcomes vary with timing; variants are diagnostic ablations, not exact replay. Death/spectating is retained and recorded per frame."
        }, true));
        if (client.State.Actors.Length != 10 || phaseShots[2] == 0 || distance < 3 || orbitDegrees < 90)
            throw new InvalidOperationException("Incomplete workload coverage; inspect coverage.json and raw frames.");
        // Optional native trace is captured AFTER numeric sampling so its overhead cannot contaminate the baseline.
        if (PerformanceCapture.Argument("--perf-trace", "0") == "1")
        {
            phase = 2;
            UnityEngine.Profiling.Profiler.logFile = Path.Combine(output, "client-profiler.raw");
            UnityEngine.Profiling.Profiler.enableBinaryLog = true;
            UnityEngine.Profiling.Profiler.enabled = true;
            yield return new WaitForSecondsRealtime(5);
            UnityEngine.Profiling.Profiler.enabled = false;
            UnityEngine.Profiling.Profiler.enableBinaryLog = false;
        }
        finished = true;
        File.WriteAllText(Path.Combine(output, "client-result.txt"), "DEMOLITION_PERFORMANCE_PASS");
        Debug.Log("DEMOLITION_PERFORMANCE_PASS " + output);
        Application.Quit(0);
    }
    void OnShot(int shooter, int hit, DemolitionPoint from, DemolitionPoint to) { shots++; }
    void ApplyVariant()
    {
        if (client?.FollowCamera == null) return;
        if (variant == "no-vision") client.FollowCamera.GetComponent<CharacterVision>().enabled = false;
        if (variant == "no-cutaway") client.FollowCamera.GetComponent<CameraCutaway>().enabled = false;
        if (variant == "no-glass" && disabledFeatures.Count == 0)
        {
            var data = (ScriptableRendererData)typeof(UniversalRenderPipelineAsset).GetProperty("scriptableRendererData", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(QualitySettings.renderPipeline);
            if (data == null) throw new InvalidOperationException("Cannot access active renderer data for glass diagnostic.");
            foreach (var feature in data.rendererFeatures) if (feature is UIGlassRendererFeature) { feature.SetActive(false); disabledFeatures.Add(feature); }
        }
    }
    void Update()
    {
        if (finished) return;
        if (Time.realtimeSinceStartupAsDouble > deadline) { Fail("Performance run timed out."); return; }
        if (client == null || !client.Ready || client.LocalPlayer == null) return;
        ApplyVariant();
        var state = client.State;
        var position = client.LocalPlayer.transform.position;
        if (lastRound == state.Round) distance += Vector3.Distance(position, previousPosition);
        else { trail.Clear(); waypoint = 0; lastRound = state.Round; }
        previousPosition = position;
        float cameraYaw = client.FollowCamera.transform.eulerAngles.y;
        // Quaternion.Angle returns zero for very small rotations; at high FPS that undercounts an entire sweep.
        if (phase == 1) orbitDegrees += Mathf.Abs(Mathf.DeltaAngle(previousCameraYaw, cameraYaw));
        previousCameraYaw = cameraYaw;
        var local = state.Actors[0];
        for (int i = 0; i < state.Actors.Length; i++)
        {
            var actor = state.Actors[i];
            if (actor.Motion.Id == 1) local = actor;
            if (actor.Motion.Id != leader || actor.Motion.Health <= 0) continue;
            var point = new Vector3(actor.Motion.X, actor.Motion.Y, actor.Motion.Z);
            if (trail.Count < 2048 && (trail.Count == 0 || (trail[trail.Count - 1] - point).sqrMagnitude > .6f)) trail.Add(point);
        }
        if (!sampling || Time.realtimeSinceStartupAsDouble < sampleAfter) return;
        capture.Capture(new PerformanceCapture.Row { frame = Time.frameCount - 1, elapsed = Time.realtimeSinceStartupAsDouble - startAt,
            phase = phase, frameMs = Time.unscaledDeltaTime * 1000, focused = Application.isFocused,
            round = state.Round, alive = local.Motion.Health > 0 ? 1 : 0, actors = state.Actors.Length, shots = shots, snapshotTick = state.Tick,
            x = position.x, y = position.y, z = position.z, cameraYaw = cameraYaw });
    }
    void LateUpdate()
    {
        if (finished || client == null || !client.Ready || client.LocalPlayer == null) return;
        ApplyVariant();
        bool orbit = phase == 1;
        if (orbit)
        {
            float degrees = 35 * Time.unscaledDeltaTime;
            if (client.FollowCamera.IsFirstPerson) client.FollowCamera.RotateFirstPerson(new Vector2(degrees / 2, 0));
            else
            {
                // Native polling releases the middle button in Update; inject both press and drag through the same API.
                client.FollowCamera.ProcessOrbit(true, true, Vector2.zero, true);
                client.FollowCamera.ProcessOrbit(true, false, new Vector2(degrees / 3, 0), true);
            }
        }
        else client.FollowCamera.ProcessOrbit(false, false, Vector2.zero, false);
    }
    DemolitionBattleClient.Controls Controls()
    {
        if (phase == 0 || client == null || client.LocalPlayer == null) return default;
        var player = client.LocalPlayer;
        Vector3 position = player.transform.position;
        while (waypoint < trail.Count - 1 && Vector3.ProjectOnPlane(trail[waypoint] - position, Vector3.up).sqrMagnitude < 1.4f) waypoint++;
        Vector3 direction = waypoint < trail.Count ? Vector3.ProjectOnPlane(trail[waypoint] - position, Vector3.up).normalized : Vector3.zero;
        var localDirection = player.transform.InverseTransformDirection(direction);
        var result = new DemolitionBattleClient.Controls { Move = new Vector2(localDirection.x, localDirection.z) };
        if (phase != 2) return result;
        DemolitionActorState self = null, nearest = null;
        foreach (var actor in client.State.Actors) if (actor.Motion.Id == 1) self = actor;
        if (self == null || self.Motion.Health <= 0) return default;
        float closest = float.MaxValue;
        foreach (var actor in client.State.Actors)
        {
            if (actor.Motion.Team == self.Motion.Team || actor.Motion.Health <= 0) continue;
            float d = (new Vector3(actor.Motion.X, actor.Motion.Y, actor.Motion.Z) - position).sqrMagnitude;
            if (d < closest) { closest = d; nearest = actor; }
        }
        if (nearest != null)
        {
            Vector3 point = player.ViewCamera.WorldToScreenPoint(new Vector3(nearest.Motion.X, nearest.Motion.Y + 1.2f, nearest.Motion.Z));
            if (point.z > 0 && !client.FollowCamera.IsFirstPerson)
                result.MouseDelta = Vector2.ClampMagnitude(((Vector2)point - player.AimScreenPosition) / 10, 40 * Time.unscaledDeltaTime);
        }
        int pulse = (int)((Time.realtimeSinceStartupAsDouble - phaseStarted) * 4);
        result.Fire = true; result.FirePressed = pulse != lastFirePulse; lastFirePulse = pulse;
        result.Reload = self.Motion.Ammo == 0 && self.Motion.ReloadTicks == 0;
        return result;
    }
    void OnLog(string message, string stack, LogType kind)
    { if (kind == LogType.Exception || kind == LogType.Error || kind == LogType.Assert) Fail(message + "\n" + stack); }
    void Fail(string message)
    {
        if (finished) return; finished = true; sampling = false;
        File.WriteAllText(Path.Combine(output, "failure.txt"), message);
        try { capture?.Save(output, "client", Phases); } catch (Exception) { /* Preserve the original failure. */ }
        Application.Quit(1);
    }
    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        if (connection != null) { connection.Shot -= OnShot; connection.Disconnected -= Fail; }
        foreach (var feature in disabledFeatures) if (feature != null) feature.SetActive(true);
        capture?.Dispose();
    }
    [Serializable] sealed class Coverage
    {
        public string variant, view, matchSeed, notes;
        public int shots, actors;
        public int[] phaseShotEvents;
        public float traveledMetres, orbitDegrees;
        public bool movementCovered, combatCovered, rotationCovered;
        public string[] phases;
    }
}
#endif
