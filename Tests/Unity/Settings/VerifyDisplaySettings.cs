using System;
using System.IO;
using System.Linq;
using System.Reflection;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Editor;
using OperationBlacktide.Client.Features.Account;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class VerifyDisplaySettings
{
    const string Active = "Display.Verify", Backup = "Display.Verify.Backup";
    static string Output
    {
        get
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--settings-output");
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : ".build-check/display-settings";
        }
    }
    static int step;
    static double next, started;
    static string error;
    static SettingsPanel settings;
    static DisplaySettings.Value before;
    static string savedBefore;
    static PerformanceStatsOverlay overlay;
    static VerifyDisplaySettings()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += (message, trace, type) =>
        {
            if (SessionState.GetBool(Active, false) && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) error = message;
        };
    }
    public static void Run()
    {
        try
        {
            SessionState.SetString(Backup, PlayerPrefs.GetString(DisplaySettings.PreferenceKey, ""));
            Directory.CreateDirectory(Output);
            BuildDisplaySettings.Build();
            Check(PlayerSettings.resizableWindow, "Window resizing disabled");
            Check(PlayerSettings.fullScreenMode == FullScreenMode.Windowed, "Default should be windowed");
            foreach (var id in new[] { UIPanelId.LobbyMenu, UIPanelId.Settings })
            {
                var prefab = UIPath.LoadPanel(id);
                Check(prefab != null && prefab.IsModal && prefab.Layer == UILayer.Popup, "Panel not registered/modal: " + id);
                foreach (var child in prefab.GetComponentsInChildren<Transform>(true))
                    Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0, "Missing script: " + child.name);
            }
            PlayerPrefs.SetString(DisplaySettings.PreferenceKey, "broken-json");
            Check(!DisplaySettings.TryLoad(out _), "Corrupt settings accepted");
            PlayerPrefs.SetString(DisplaySettings.PreferenceKey, "{\"width\":1280,\"height\":720,\"fullscreen\":false}");
            Check(DisplaySettings.TryLoad(out var legacy) && legacy.ShowPerformanceStats, "Older display saves must enable telemetry by default");
            legacy.ShowPerformanceStats = false;
            PlayerPrefs.SetString(DisplaySettings.PreferenceKey, JsonUtility.ToJson(legacy));
            RestoreRuntimePreferences();
            Check(!DisplaySettings.PerformanceStatsEnabled, "Saved telemetry preference did not restore");
            PlayerPrefs.DeleteKey(DisplaySettings.PreferenceKey);
            RestoreRuntimePreferences();
            Check(DisplaySettings.PerformanceStatsEnabled && PlayerSettings.enableFrameTimingStats, "Telemetry/timing must default on");
            SessionState.SetBool(Active, true);
            EditorSceneManager.OpenScene(GameSceneFlow.LoginScenePath);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e) { Fail(e); }
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
        if (started == 0) { started = EditorApplication.timeSinceStartup; next = started + 3; }
        try
        {
            Check(error == null, error);
            Check(EditorApplication.timeSinceStartup - started < 180, "Timed out at " + step);
            if (EditorApplication.timeSinceStartup < next) return;
            var ui = UIManager.Instance;
            var root = UIRoot.Instance;
            switch (step)
            {
                case 0:
                    Check(ui.GetPanel<LoginPanel>(UIPanelId.Login).IsOpen, "Login did not open");
                    overlay = Object.FindObjectOfType<PerformanceStatsOverlay>();
                    Check(overlay != null && overlay.IsVisible, "Default telemetry missing from login");
                    CheckTelemetry();
                    GameSceneFlow.Instance.Auth.Session.Set("settings-verification", "DisplayTester", "local-fixture");
                    SceneManager.LoadSceneAsync(GameSceneFlow.LobbyScenePath);
                    break;
                case 1:
                    var lobby = ui.GetPanel<LobbyPanel>(UIPanelId.Lobby);
                    if (lobby == null || !lobby.IsOpen) return;
                    CheckTelemetry();
                    Capture("01-lobby", 1920, 1080);
                    Click(lobby, "TopBar/SettingsButton");
                    settings = ui.GetPanel<SettingsPanel>(UIPanelId.Settings);
                    Check(settings.IsOpen && !settings.HasUnsavedChanges, "Direct settings entry failed");
                    Check(!Button(settings, "Sheet/Save").interactable, "Unchanged settings can save");
                    before = DisplaySettings.Current;
                    savedBefore = PlayerPrefs.GetString(DisplaySettings.PreferenceKey, "");
                    var choices = settings.GetComponentInChildren<SettingsResolutionDropdown>().options.Select(o => o.text).ToArray();
                    Check(choices.Length > 1 && choices.Distinct().Count() == choices.Length && choices.Contains($"{before.width} × {before.height}"), "Resolution list missing current/duplicate entries");
                    root.HandleBackInput();
                    Check(!settings.IsOpen && ui.GetStackCount(UILayer.Popup) == 0, "Clean settings did not close");
                    Click(lobby, "PlayControls/ModeButton"); root.HandleBackInput();
                    Check(!lobby.transform.Find("ModePicker").gameObject.activeSelf && ui.GetStackCount(UILayer.Popup) == 0, "Mode picker Esc also opened menu");
                    Click(lobby, "TopBar/Navigation/Tab3"); root.HandleBackInput();
                    Check(ui.GetPanel<LobbyMenuPanel>(UIPanelId.LobbyMenu).IsOpen && lobby.SelectedTabIndex == LobbyPanel.AgentTabIndex, "Esc menu did not preserve lobby tab");
                    Capture("02-menu", 1920, 1080); Capture("02-menu-4x3", 1024, 768);
                    Click(ui.GetPanel<LobbyMenuPanel>(UIPanelId.LobbyMenu), "Sheet/Settings");
                    Check(settings.IsOpen && ui.GetStackCount(UILayer.Popup) == 2, "Menu settings entry failed");
                    Capture("03-settings", 1920, 1080); Capture("03-settings-16x10", 1280, 800);
                    Capture("03-settings-4x3", 1024, 768); Capture("03-settings-ultrawide", 2560, 1080);
                    break;
                case 2:
                    if (!settings.GetComponentInChildren<SettingsResolutionDropdown>().IsExpanded)
                    {
                        settings.GetComponentInChildren<SettingsResolutionDropdown>().Show();
                        next = EditorApplication.timeSinceStartup + .4;
                        return;
                    }
                    Capture("04-resolutions", 1280, 800);
                    root.HandleBackInput();
                    Check(settings.IsOpen, "Esc closed settings through expanded dropdown");
                    break;
                case 3:
                    Check(!settings.GetComponentInChildren<SettingsResolutionDropdown>().IsExpanded, "Dropdown did not close");
                    var toggle = settings.GetComponentInChildren<Toggle>();
                    toggle.isOn = !toggle.isOn;
                    Click(settings, "Sheet/Categories/Display");
                    Check(settings.HasUnsavedChanges && Button(settings, "Sheet/Save").interactable, "Fullscreen change not tracked");
                    toggle.isOn = !toggle.isOn;
                    Check(!settings.HasUnsavedChanges, "Reverted change still dirty");
                    toggle.isOn = !toggle.isOn;
                    Check(PlayerPrefs.GetString(DisplaySettings.PreferenceKey, "") == savedBefore && DisplaySettings.Current.Equals(before), "Draft applied before Save");
                    root.HandleBackInput(); Check(Prompt(), "Esc did not warn");
                    Capture("05-unsaved", 1920, 1080); Capture("05-unsaved-4x3", 1024, 768);
                    Check(!settings.transform.Find("Sheet").GetComponent<CanvasGroup>().interactable, "Prompt does not block fields");
                    Click(settings, "UnsavedPrompt/Dialog/Cancel");
                    Check(!Prompt() && settings.HasUnsavedChanges, "Continue editing lost draft");
                    Click(settings, "Sheet/Back"); Check(Prompt(), "Back button did not warn");
                    root.HandleBackInput(); Check(!Prompt() && settings.IsOpen && settings.HasUnsavedChanges, "Esc should cancel prompt");
                    root.HandleBackInput(); Click(settings, "UnsavedPrompt/Dialog/Discard");
                    Check(!settings.IsOpen && ui.GetPanel<LobbyMenuPanel>(UIPanelId.LobbyMenu).IsOpen, "Discard did not return to menu");
                    Check(PlayerPrefs.GetString(DisplaySettings.PreferenceKey, "") == savedBefore && DisplaySettings.Current.Equals(before), "Discard wrote settings");
                    Click(ui.GetPanel<LobbyMenuPanel>(UIPanelId.LobbyMenu), "Sheet/Settings");
                    var dropdown = settings.GetComponentInChildren<SettingsResolutionDropdown>();
                    dropdown.value = (dropdown.value + 1) % dropdown.options.Count;
                    Check(settings.HasUnsavedChanges, "Resolution not tracked");
                    Click(settings, "Sheet/Save");
                    Check(DisplaySettings.TryLoad(out var saved) && !saved.Equals(before) && !settings.HasUnsavedChanges, "Save did not persist/clear dirty state");
                    Check(settings.IsOpen, "Save unexpectedly closed settings");
                    root.HandleBackInput(); Check(!settings.IsOpen, "Saved settings still warned");
                    break;
                case 4:
                    Click(ui.GetPanel<LobbyMenuPanel>(UIPanelId.LobbyMenu), "Sheet/Settings");
                    settings.GetComponentInChildren<Toggle>().isOn = !settings.GetComponentInChildren<Toggle>().isOn;
                    root.HandleBackInput(); Click(settings, "UnsavedPrompt/Dialog/SaveAndLeave");
                    Check(!settings.IsOpen && DisplaySettings.TryLoad(out _), "Save and leave failed");
                    root.HandleBackInput(); Check(ui.GetStackCount(UILayer.Popup) == 0, "Esc did not close menu");
                    for (int i = 0; i < 3; i++)
                    {
                        root.HandleBackInput();
                        Check(ui.GetStackCount(UILayer.Popup) == 1, "Duplicate Esc listener");
                        root.HandleBackInput();
                    }
                    root.HandleBackInput(); Click(ui.GetPanel<LobbyMenuPanel>(UIPanelId.LobbyMenu), "Sheet/Settings");
                    settings.GetComponentInChildren<Toggle>().isOn = !settings.GetComponentInChildren<Toggle>().isOn;
                    var wantsToQuit = typeof(SettingsPanel).GetMethod("OnWantsToQuit", BindingFlags.NonPublic | BindingFlags.Instance);
                    Check(!(bool)wantsToQuit.Invoke(settings, null) && Prompt(), "OS quit bypasses unsaved warning");
                    Click(settings, "UnsavedPrompt/Dialog/Cancel");
                    root.HandleBackInput(); Click(settings, "UnsavedPrompt/Dialog/Discard");
                    VerifyPerformanceSwitch();
                    Click(ui.GetPanel<LobbyMenuPanel>(UIPanelId.LobbyMenu), "Sheet/Login");
                    break;
                case 5:
                    if (SceneManager.GetActiveScene().path != GameSceneFlow.LoginScenePath) return;
                    Check(!GameSceneFlow.Instance.Auth.Session.IsLoggedIn && ui.GetPanel<LoginPanel>(UIPanelId.Login).IsOpen && ui.GetStackCount(UILayer.Popup) == 0, "Return to login failed");
                    CheckTelemetry();
                    RestorePreferences();
                    SessionState.SetBool(Active, false);
                    File.WriteAllText(Output + "/verification.txt", "PASS: registered modal prefabs; direct/menu entry; Esc routing; dropdown; draft/cancel/discard/save/save-and-leave; device persistence; OS quit guard; repeated opening; logout; telemetry default/old-save migration/restore/draft/discard/save/re-enable; green nonblocking overlay across scenes; live metrics; 16:9, 16:10, 4:3, ultrawide screenshots.\n");
                    Debug.Log("DISPLAY_SETTINGS_VERIFY_PASS"); EditorApplication.Exit(0); return;
            }
            step++; next = EditorApplication.timeSinceStartup + 1;
        }
        catch (Exception e) { Fail(e); }
    }
    static bool Prompt() => settings.transform.Find("UnsavedPrompt").gameObject.activeSelf;
    static void RestoreRuntimePreferences() => typeof(DisplaySettings).GetMethod("Restore", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
    static Toggle StatsToggle() => settings.transform.Find("Sheet/Content/Display/PerformanceStatsRow/PerformanceStats").GetComponent<Toggle>();
    static void VerifyPerformanceSwitch()
    {
        var menu = UIManager.Instance.GetPanel<LobbyMenuPanel>(UIPanelId.LobbyMenu);
        Click(menu, "Sheet/Settings");
        Check(StatsToggle().isOn && !settings.HasUnsavedChanges, "Telemetry did not default on in settings");
        StatsToggle().isOn = false;
        Check(settings.HasUnsavedChanges && overlay.IsVisible, "Telemetry draft applied before save");
        StatsToggle().isOn = true;
        Check(!settings.HasUnsavedChanges, "Reverting telemetry leaves dirty state");
        StatsToggle().isOn = false;
        UIRoot.Instance.HandleBackInput(); Check(Prompt(), "Telemetry changes bypass unsaved warning");
        Click(settings, "UnsavedPrompt/Dialog/Cancel");
        Check(!StatsToggle().isOn && overlay.IsVisible, "Cancel lost telemetry draft or applied it");
        UIRoot.Instance.HandleBackInput(); Click(settings, "UnsavedPrompt/Dialog/Discard");
        Click(menu, "Sheet/Settings");
        Check(StatsToggle().isOn && overlay.IsVisible, "Discard applied telemetry changes");
        StatsToggle().isOn = false; Click(settings, "Sheet/Save");
        Check(!overlay.IsVisible && !DisplaySettings.PerformanceStatsEnabled && !settings.HasUnsavedChanges
            && DisplaySettings.TryLoad(out var saved) && !saved.ShowPerformanceStats, "Telemetry Save did not hide/persist");
        Capture("06-statistics-off", 1920, 1080);
        UIRoot.Instance.HandleBackInput(); Click(menu, "Sheet/Settings");
        Check(!StatsToggle().isOn, "Reopened settings lost disabled telemetry preference");
        StatsToggle().isOn = true;
        UIRoot.Instance.HandleBackInput(); Click(settings, "UnsavedPrompt/Dialog/SaveAndLeave");
        Check(overlay.IsVisible && DisplaySettings.PerformanceStatsEnabled && !settings.IsOpen, "Save and leave failed to re-enable telemetry");
        RestoreRuntimePreferences();
        Check(overlay.IsVisible, "Telemetry runtime restore differs from saved value");
    }
    static void CheckTelemetry()
    {
        Check(Object.FindObjectsOfType<PerformanceStatsOverlay>().Length == 1 && overlay.IsVisible, "Telemetry duplicated/lost after scene change");
        string text = overlay.transform.Find("Content/Statistics").GetComponent<Text>().text;
        foreach (string metric in new[] { "FPS", "CPU", "内存", "GC", "GPU", "图形分配" }) Check(text.Contains(metric), "Missing telemetry: " + metric);
        Check(!text.Contains("NaN") && !text.Contains("Infinity") && !text.Contains("采样中"), "Telemetry did not sample valid metrics");
        Check(!text.Contains("CPU 进程：不可用") && !text.Contains("内存：不可用") && !text.Contains("内存：0 MB"), "Windows process telemetry unavailable/zero");
        foreach (var label in overlay.GetComponentsInChildren<Text>())
            Check(label.color.g > .9f && label.color.r < .5f && label.color.b < .5f && !label.raycastTarget, "Telemetry must be green and ignore clicks");
        foreach (var graphic in overlay.GetComponentsInChildren<Graphic>()) Check(!graphic.raycastTarget, "Telemetry blocks input");
        File.WriteAllText(Output + "/telemetry-sample.txt", text + "\n" + overlay.transform.Find("Content/Device").GetComponent<Text>().text);
    }
    static Button Button(UIPanel panel, string path) => panel.transform.Find(path).GetComponent<Button>();
    static void Click(UIPanel panel, string path) => Button(panel, path).onClick.Invoke();
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void RestorePreferences()
    {
        string saved = SessionState.GetString(Backup, "");
        if (saved.Length == 0) PlayerPrefs.DeleteKey(DisplaySettings.PreferenceKey);
        else PlayerPrefs.SetString(DisplaySettings.PreferenceKey, saved);
        PlayerPrefs.Save();
    }
    static void Fail(Exception e)
    {
        RestorePreferences(); SessionState.SetBool(Active, false); Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/failure.txt", e.ToString()); Debug.LogException(e); EditorApplication.Exit(1);
    }
    public static void BuildClient()
    {
        var backend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone);
        var args = Environment.GetCommandLineArgs(); int outputIndex = Array.IndexOf(args, "--settings-client-output");
        string output = outputIndex >= 0 && outputIndex + 1 < args.Length ? args[outputIndex + 1] : "Builds/Client";
        try
        {
            if (backend != ScriptingImplementation.Mono2x) PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(output, "Blacktide.exe"), target = BuildTarget.StandaloneWindows64,
                subtarget = (int)StandaloneBuildSubtarget.Player, options = BuildOptions.Development });
            Check(result.summary.result == BuildResult.Succeeded, "Client build failed");
            Debug.Log("DISPLAY_SETTINGS_CLIENT_BUILD_PASS");
        }
        finally { if (backend != ScriptingImplementation.Mono2x) PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, backend); }
    }

    static void Capture(string name, int width, int height)
    {
        var camera = Camera.main;
        var canvas = UIRoot.Instance.GetComponentInChildren<Canvas>();
        var scaler = canvas.GetComponent<CanvasScaler>();
        bool scalerEnabled = scaler.enabled; float previousScale = canvas.scaleFactor;
        scaler.enabled = false; canvas.scaleFactor = Mathf.Sqrt((float)width / 1920 * height / 1080);
        var sceneTarget = new RenderTexture(width, height, 24);
        var target = new RenderTexture(width, height, 24);
        int mask = camera.cullingMask, layer = canvas.gameObject.layer;
        var data = camera.GetUniversalAdditionalCameraData(); bool post = data.renderPostProcessing;
        GameObject background = null; Texture2D pixels = null;
        var oldTarget = RenderTexture.active;
        try
        {
            canvas.enabled = false; camera.targetTexture = sceneTarget; camera.aspect = (float)width / height;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = sceneTarget });
            background = new GameObject("CaptureBackground", typeof(RectTransform), typeof(RawImage));
            background.layer = 5; background.transform.SetParent(canvas.transform, false); background.transform.SetAsFirstSibling();
            var rect = (RectTransform)background.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            background.GetComponent<RawImage>().texture = sceneTarget;
            canvas.enabled = true; canvas.gameObject.layer = 5;
            camera.cullingMask = 1 << 5; data.renderPostProcessing = false; camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = .5f;
            Canvas.ForceUpdateCanvases();
            if (overlay.IsVisible)
            {
                // Best-fit labels are verified in the captures; preferredHeight measures their unfitted size.
                foreach (var label in overlay.GetComponentsInChildren<Text>().Where(label => !label.resizeTextForBestFit))
                    Check(label.preferredHeight <= label.rectTransform.rect.height + 1, "Telemetry text exceeds its row: " + label.name);
                var corners = new Vector3[4]; ((RectTransform)overlay.transform.Find("Content")).GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var point = camera.WorldToScreenPoint(corner);
                    Check(point.x >= 0 && point.x <= width && point.y >= 0 && point.y <= height, "Telemetry left the viewport");
                }
            }
            if (name.StartsWith("03-settings", StringComparison.Ordinal))
            {
                var page = (RectTransform)settings.transform.Find("Sheet");
                var fullBackground = settings.transform.Find("Background").GetComponent<Image>();
                var canvasRect = (RectTransform)canvas.transform;
                Check(Vector2.Distance(page.rect.size, canvasRect.rect.size) < 1, "Settings page does not fill the viewport");
                Check(fullBackground.color.a == 1 && fullBackground.GetComponent<UIGlassEffect>() == null, "Settings background reveals the lobby");
                var navigation = (RectTransform)page.Find("Categories");
                var content = (RectTransform)page.Find("Content");
                var navigationCorners = new Vector3[4]; var contentCorners = new Vector3[4];
                navigation.GetWorldCorners(navigationCorners); content.GetWorldCorners(contentCorners);
                Check(navigationCorners[2].x < contentCorners[0].x, "Categories overlap the content");
            }
            foreach (var button in canvas.GetComponentsInChildren<Button>().Where(b => b.IsActive()))
            {
                var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var point = camera.WorldToScreenPoint(corner);
                    Check(point.x >= -1 && point.x <= width + 1 && point.y >= -1 && point.y <= height + 1, "Offscreen button " + button.name + " at " + width + "x" + height);
                }
            }
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target; pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = oldTarget; canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
            canvas.gameObject.layer = layer; canvas.enabled = true; canvas.scaleFactor = previousScale; scaler.enabled = scalerEnabled;
            camera.targetTexture = null; camera.ResetAspect(); camera.cullingMask = mask; data.renderPostProcessing = post;
            if (background != null) Object.DestroyImmediate(background); if (pixels != null) Object.DestroyImmediate(pixels);
            sceneTarget.Release(); target.Release(); Object.DestroyImmediate(sceneTarget); Object.DestroyImmediate(target);
        }
    }
}
