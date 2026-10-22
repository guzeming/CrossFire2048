using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Common;
using OperationBlacktide.Client.Editor;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class VerifySceneLoading
{
    const string Active = "SceneLoading.Verify";
    static GameSceneFlow flow;
    static SceneLoadingPanel panel;
    static int stage, backCount, loadedCount;
    static double started, next;
    static string error, previousTitle;
    static float previousProgress;
    static bool expectedMissingScene, sawCompletion, redirecting, blockedPointer;
    static readonly List<string> Report = new List<string>();
    static readonly string[] Paths = { GameSceneFlow.LoginScenePath, GameSceneFlow.LobbyScenePath, GameSceneFlow.TrainingScenePath, GameSceneFlow.BattleScenePath };

    static VerifySceneLoading()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += (message, trace, type) => {
            if (!SessionState.GetBool(Active, false) || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
            if (expectedMissingScene && message.Contains("场景未加入 Build Settings")) return;
            error = message + "\n" + trace;
        };
    }
    public static void Run()
    {
        try
        {
            BuildSceneLoadingUI.Build();
            Directory.CreateDirectory("Assets/Scenes");
            foreach (string path in Paths)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var probe = new GameObject("Scene startup probe").AddComponent<LoadingSceneProbe>();
                probe.waitForSignal = path == GameSceneFlow.BattleScenePath;
                EditorSceneManager.SaveScene(scene, path);
            }
            EditorBuildSettings.scenes = Paths.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Active, true); EditorApplication.EnterPlaymode();
        }
        catch (Exception exception) { Fail(exception); }
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
        if (started == 0) { started = EditorApplication.timeSinceStartup; next = started + 1; }
        try
        {
            Check(error == null, error);
            Check(EditorApplication.timeSinceStartup - started < 90, "Loading verification timed out at " + stage);
            if (flow != null && flow.IsLoading && panel != null)
            {
                Check(panel.IsOpen && panel.gameObject.activeInHierarchy, "Loading overlay disappeared during transition");
                Check(!EventSystem.current.sendNavigationEvents, "Submit/navigation not blocked");
                var title = panel.transform.Find("Content/Destination").GetComponent<Text>().text;
                if (previousTitle == title) Check(panel.Progress >= previousProgress, "Progress moved backwards");
                previousProgress = panel.Progress; previousTitle = title;
                if (panel.Progress == 1) sawCompletion = true;
                UIRoot.Instance.HandleBackInput(); Check(backCount == 0, "Esc escaped the loading screen");
                CheckPointerBlocker();
            }
            if (EditorApplication.timeSinceStartup < next) return;
            switch (stage)
            {
                case 0:
                    // Rendering the actual prefab using the built-in UI pass avoids unrelated world render features.
                    GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
                    new GameObject("UIRoot").AddComponent<UIRoot>();
                    UIRoot.Instance.UnhandledBackRequested += () => backCount++;
                    panel = UIManager.Instance.Push<SceneLoadingPanel>(UIPanelId.Loading);
                    Check(panel != null, "Loading prefab failed to load");
                    panel.SetDestination("大厅", false); panel.SetProgress(.64f, "正在加载场景");
                    Capture("lobby-1920x1080", 1920, 1080);
                    panel.SetDestination("训练场 · 沙 2", true); panel.SetProgress(.72f, "正在加载场景");
                    Capture("training-1280x800", 1280, 800);
                    panel.SetDestination("沙 2", true); panel.SetProgress(.95f, "正在连接对局");
                    Capture("battle-2560x1080", 2560, 1080);
                    UIManager.Instance.Close(UIPanelId.Loading);
                    Check(EventSystem.current.sendNavigationEvents, "Preview did not restore navigation");
                    Report.Add("Rendered the production prefab at 16:9, 16:10 and ultrawide; text and bar remain inside the screen.");
                    flow = new GameObject("Game").AddComponent<GameSceneFlow>();
                    flow.Auth.Session.Set("fixture", "fixture", "local-fixture");
                    SceneManager.sceneLoaded += (_, __) => loadedCount++;
                    flow.ReturnToLobby(); CheckStarted();
                    flow.EnterTrainingGround(); flow.ReturnToLobby(); flow.EnterBattleArena();
                    break;
                case 1:
                    if (flow.IsLoading) return;
                    Check(SceneManager.GetActiveScene().path == GameSceneFlow.LobbyScenePath && loadedCount == 1, "Duplicate input loaded extra scenes");
                    CheckFinished(); Check(LoadingSceneProbe.Starts == 1, "Closed before destination Start");
                    LoadingSceneProbe.Release = false; flow.EnterBattleArena(); CheckStarted();
                    break;
                case 2:
                    if (SceneManager.GetActiveScene().path != GameSceneFlow.BattleScenePath || panel.Progress < .95f) return;
                    Check(flow.IsLoading && panel.Progress < 1, "Completed before match readiness");
                    Check(!LoadingSceneProbe.Release, "Fixture unexpectedly ready");
                    next = EditorApplication.timeSinceStartup + .2;
                    break;
                case 3:
                    Check(flow.IsLoading && panel.Progress == .95f, "Network waiting fabricated progress");
                    LoadingSceneProbe.Release = true;
                    Report.Add("Async scene progress is monotonic; 95% waits for readiness; duplicate input and Esc cannot interrupt loading.");
                    break;
                case 4:
                    if (flow.IsLoading) return;
                    CheckFinished();
                    Time.timeScale = 0; flow.ReturnToLobby(); CheckStarted();
                    break;
                case 5:
                    if (flow.IsLoading) return;
                    Check(SceneManager.GetActiveScene().path == GameSceneFlow.LobbyScenePath, "Return to lobby failed while paused");
                    CheckFinished(); Time.timeScale = 1;
                    flow.EnterTrainingGround(); CheckStarted();
                    break;
                case 6:
                    if (flow.IsLoading) return;
                    Check(SceneManager.GetActiveScene().path == GameSceneFlow.TrainingScenePath, "Training entry bypassed loader");
                    CheckFinished(); flow.ReturnToLobby(); CheckStarted();
                    break;
                case 7:
                    if (flow.IsLoading) return;
                    CheckFinished(); LoadingSceneProbe.Release = false;
                    flow.EnterBattleArena(); CheckStarted();
                    break;
                case 8:
                    if (SceneManager.GetActiveScene().path != GameSceneFlow.BattleScenePath || panel.Progress < .95f) return;
                    flow.Auth.Session.Clear(); GameEvents.Publish(GameEventId.NetworkDisconnected, "fixture disconnect");
                    redirecting = true;
                    break;
                case 9:
                    if (flow.IsLoading) return;
                    Check(redirecting && SceneManager.GetActiveScene().path == GameSceneFlow.LoginScenePath, "Disconnect did not redirect to login");
                    CheckFinished();
                    Check(loadedCount == 7, "Unexpected scene transition count: " + loadedCount);
                    flow.EnterTrainingGround(); Check(!flow.IsLoading, "Same-scene login request opened overlay");
                    flow.Auth.Session.Set("fixture", "fixture", "local-fixture");
                    flow.ReturnToLobby(); CheckStarted();
                    Report.Add("Lobby, training, match and login transitions share the loader; pause and mid-load disconnect restore input and reuse one overlay.");
                    break;
                case 10:
                    if (flow.IsLoading) return;
                    CheckFinished();
                    // Unity caches the runtime build list; editing EditorBuildSettings in Play Mode
                    // would not create a missing-scene failure. Exercise the same private entry with an absent path.
                    expectedMissingScene = true;
                    typeof(GameSceneFlow).GetMethod("LoadScene", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(flow, new object[] { "Assets/Scenes/MissingLoadingFixture.unity" });
                    expectedMissingScene = false;
                    Check(!flow.IsLoading && !panel.IsOpen && EventSystem.current.sendNavigationEvents, "Missing scene stranded input behind overlay");
                    Check(SceneManager.GetActiveScene().path == GameSceneFlow.LobbyScenePath, "Missing scene left lobby");
                    Report.Add("Missing build scene fails before locking UI; subsequent transitions and navigation remain usable.");
                    File.WriteAllLines("scene-loading-verification.txt", new[] { "PASS: scene loading integration (real LoadSceneAsync with lightweight fixture scenes)" }.Concat(Report));
                    SessionState.SetBool(Active, false); Debug.Log("SCENE_LOADING_VERIFY_PASS"); EditorApplication.Exit(0); return;
            }
            stage++;
        }
        catch (Exception exception) { Fail(exception); }
    }
    static void CheckStarted()
    {
        previousTitle = null; previousProgress = 0; sawCompletion = false; blockedPointer = false;
        Check(flow.IsLoading && panel.IsOpen && panel.Progress == 0, "Loader did not open synchronously");
        Check(UIManager.Instance.GetStackCount(UILayer.Overlay) == 0, "Loading polluted UI navigation stack");
    }
    static void CheckPointerBlocker()
    {
        Canvas.ForceUpdateCanvases();
        var blocker = panel.transform.Find("Blocker").GetComponent<Image>();
        // UGUI registers a re-enabled Graphic with its Canvas during the first render pass.
        if (blocker.depth < 0) return;
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = new Vector2(Screen.width / 2f, Screen.height / 2f) }, hits);
        Check(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(panel.transform),
            $"Pointer raycast passes through loading UI: screen {Screen.width}x{Screen.height}, depth {blocker.depth}, rect {blocker.rectTransform.rect}, hits " + string.Join(",", hits.Select(h => h.gameObject.name)));
        blockedPointer = true;
    }
    static void CheckFinished()
    {
        Check(!panel.IsOpen && !panel.gameObject.activeSelf && EventSystem.current.sendNavigationEvents, "Loading/input cleanup failed");
        Check(sawCompletion, "Loading never presented 100% after readiness");
        Check(blockedPointer, "No rendered loading frame intercepted pointer input");
        Check(Object.FindObjectsOfType<SceneLoadingPanel>(true).Length == 1, "Repeated transition duplicated loading panel");
    }
    static void Capture(string name, int width, int height)
    {
        Directory.CreateDirectory("screenshots");
        var canvas = UIRoot.Instance.GetComponentInChildren<Canvas>();
        var scaler = canvas.GetComponent<CanvasScaler>();
        float originalScale = canvas.scaleFactor;
        var camera = new GameObject("UI capture camera").AddComponent<Camera>();
        var target = new RenderTexture(width, height, 24);
        var previous = RenderTexture.active;
        Texture2D pixels = null;
        try
        {
            scaler.enabled = false; canvas.scaleFactor = Mathf.Sqrt((float)width / 1920 * height / 1080);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.magenta;
            camera.cullingMask = 1 << 5; camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases(); panel.SendMessage("LateUpdate");
            Canvas.ForceUpdateCanvases(); camera.Render();
            foreach (var label in panel.GetComponentsInChildren<Text>())
            {
                var corners = new Vector3[4]; label.rectTransform.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var p = camera.WorldToScreenPoint(corner);
                    Check(p.x >= -1 && p.x <= width + 1 && p.y >= -1 && p.y <= height + 1, "Offscreen loading text: " + label.name);
                }
                Check(label.cachedTextGenerator.characterCountVisible >= label.text.Length, "Clipped loading text: " + label.name);
            }
            RenderTexture.active = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            File.WriteAllBytes("screenshots/" + name + ".png", pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
            canvas.scaleFactor = originalScale; scaler.enabled = true;
            camera.targetTexture = null; Object.DestroyImmediate(camera.gameObject);
            target.Release(); Object.DestroyImmediate(target); if (pixels != null) Object.DestroyImmediate(pixels);
        }
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Fail(Exception exception)
    {
        SessionState.SetBool(Active, false); Time.timeScale = 1;
        File.WriteAllText("scene-loading-failure.txt", exception.ToString()); Debug.LogException(exception); EditorApplication.Exit(1);
    }
}
