using OperationBlacktide.Client.Content;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Editor;
using OperationBlacktide.Client.Features.Account;
using OperationBlacktide.Client.Features.Demolition;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.Features.Training;
using OperationBlacktide.Client.Gameplay.Combat;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Installed temporarily in Assets/Editor by verify-black-gold-ui.ps1; never shipped in a player.
[InitializeOnLoad]
public static class VerifyBlackGoldUI
{
    const string Active = "BlackGold.Verify";
    const string Output = ".build-check/black-gold-ui";
    static int step;
    static int primaryState;
    static double next, started;
    static string error;
    static DemolitionBattleClient battleFixture;
    static RectTransform battleSheet;
    static VerifyBlackGoldUI()
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
            Directory.CreateDirectory(Output);
            BuildBlackGoldUI.Build();
            Check(!ShaderUtil.ShaderHasError(Shader.Find("OperationBlacktide/UI/Black Gold Glass")), "Glass shader errors");
            foreach (var path in Directory.GetFiles("Assets/Resources/UI/Panels", "*.prefab"))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                    Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0, "Missing script: " + path);
            }
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
            Check(EditorApplication.timeSinceStartup - started < 240, "Visual/interaction verification timed out at " + step);
            if (EditorApplication.timeSinceStartup < next) return;
            switch (step)
            {
                case 0:
                    var login = UIManager.Instance.GetPanel<LoginPanel>(UIPanelId.Login);
                    Check(login != null && login.IsOpen, "Login failed to open"); VerifyControls(login.gameObject);
                    foreach (var field in login.GetComponentsInChildren<InputField>()) field.text = "";
                    Check(login.transform.Find("LoginCard/GlassPlate") != null, "Login layout migration did not run");
                    Check(login.transform.Find("LoginCard/LoginButton").GetComponent<UIGlassEffect>().Style == UIGlassEffect.Kind.Primary, "Primary login style lost");
                    Capture("01-login", 1920, 1080); Capture("01-login-16x10", 1280, 800);
                    var input = login.GetComponentsInChildren<InputField>().First();
                    input.text = "x";
                    login.transform.Find("LoginCard/PasswordInput").GetComponent<InputField>().text = "123456";
                    login.transform.Find("LoginCard/LoginButton").GetComponent<Button>().onClick.Invoke();
                    Check(login.transform.Find("LoginCard/StatusText").GetComponent<Text>().text.Contains("用户名"), "Login binding lost");
                    input.Select();
                    var primary = login.transform.Find("LoginCard/LoginButton").GetComponent<UIGlassEffect>();
                    primary.OnPointerEnter(new PointerEventData(EventSystem.current));
                    break;
                case 1:
                    Capture("02-login-focus-hover", 1920, 1080);
                    GameSceneFlow.Instance.Auth.Session.Set("black-gold-validation", "BlacktideOperator", "local-visual-fixture");
                    SceneManager.LoadSceneAsync(GameSceneFlow.LobbyScenePath);
                    break;
                case 2:
                    var lobby = UIManager.Instance.GetPanel<LobbyPanel>(UIPanelId.Lobby);
                    if (lobby == null || !lobby.IsOpen) return;
                    VerifyControls(lobby.gameObject);
                    if (!CapturePrimaryStates(lobby)) return;
                    lobby.transform.Find("PlayControls/ModeButton").GetComponent<Button>().onClick.Invoke();
                    Capture("03-mode-picker", 1280, 800);
                    lobby.transform.Find("ModePicker/Dismiss").GetComponent<Button>().onClick.Invoke();
                    lobby.transform.Find("TopBar/Navigation/Tab3").GetComponent<Button>().onClick.Invoke();
                    var agents = Object.FindObjectOfType<LobbyLoadoutView>();
                    Check(lobby.SelectedTabIndex == LobbyPanel.AgentTabIndex && agents.agentsPanel.activeSelf &&
                        agents.teamControls.activeSelf && !agents.equipmentPage.activeSelf && !agents.playControls.activeSelf,
                        "Agent tab did not open an exclusive selection page");
                    Check(lobby.transform.Find("ChooseAgent") == null, "Retired corner agent entry remains");
                    Capture("03-agent-selection", 1920, 1080); Capture("03-agent-selection-16x10", 1280, 800);
                    agents.tButton.onClick.Invoke();
                    var agentChoice = agents.agentContent.GetComponentsInChildren<LobbyLoadoutRow>().Last();
                    agentChoice.button.onClick.Invoke();
                    Check(agents.Store.Team == EquipmentTeam.T && agents.Store.Current.agentId == agentChoice.name.Substring("Agent_".Length), "Agent selection binding failed");
                    agents.ctButton.onClick.Invoke();
                    lobby.transform.Find("TopBar/Navigation/Tab2").GetComponent<Button>().onClick.Invoke();
                    Check(!agents.agentsPanel.activeSelf && !agents.teamControls.activeSelf, "Agent page leaked into career tab");
                    lobby.transform.Find("TopBar/Navigation/Tab1").GetComponent<Button>().onClick.Invoke();
                    Check(lobby.SelectedTabIndex == 1 && !agents.agentsPanel.activeSelf, "Equipment tab failed");
                    break;
                case 3:
                    var equipment = Object.FindObjectOfType<LobbyLoadoutView>();
                    VerifyControls(equipment.gameObject);
                    Check(equipment.slotContent.GetComponentsInChildren<UIGlassEffect>().Length > 0, "Dynamic loadout rows unthemed");
                    Capture("04-loadout", 1920, 1080); Capture("04-loadout-16x10", 1280, 800);
                    equipment.SelectCategory(EquipmentCategory.Pistols);
                    break;
                case 4:
                    Capture("04-loadout-pistols", 1920, 1080);
                    var gate = DemolitionGateway.Get(); gate.enabled = false; // UI fixture, no external account/server request.
                    UIManager.Instance.Push(UIPanelId.Demolition, gate);
                    break;
                case 5:
                    var deployment = UIManager.Instance.GetPanel<DemolitionPanel>(UIPanelId.Demolition);
                    VerifyControls(deployment.gameObject); Capture("05-deployment", 1920, 1080);
                    FindButton(deployment.gameObject, "房间大厅").onClick.Invoke();
                    break;
                case 6:
                    var browser = UIManager.Instance.GetPanel<DemolitionPanel>(UIPanelId.Demolition);
                    Capture("06-room-browser", 1920, 1080);
                    FindButton(browser.gameObject, "创建房间").onClick.Invoke();
                    break;
                case 7:
                    Capture("07-create-room", 1280, 800);
                    UIManager.Instance.CloseAll(UILayer.Popup);
                    GameSceneFlow.Instance.EnterTrainingGround();
                    break;
                case 8:
                    var training = Object.FindObjectOfType<TrainingSceneController>();
                    if (training == null || training.Player == null || GameSceneFlow.Instance.IsLoading) return;
                    Capture("08-training-hud", 1920, 1080);
                    training.OpenTrainingMenu();
                    break;
                case 9:
                    var menu = UIManager.Instance.GetPanel<TrainingMenuPanel>(UIPanelId.TrainingMenu);
                    Check(menu != null && menu.IsOpen && Object.FindObjectOfType<TrainingSceneController>().IsInputBlocked, "Modal input blocking failed");
                    VerifyControls(menu.gameObject); Capture("09-training-menu", 1920, 1080); Capture("09-training-menu-16x10", 1280, 800);
                    Check(((RectTransform)menu.transform.Find("Sheet")).sizeDelta == new Vector2(520, 408), "Tactical side menu layout missing");
                    menu.transform.Find("Sheet/Home/Weapons").GetComponent<Button>().onClick.Invoke();
                    break;
                case 10:
                    var picker = UIManager.Instance.GetPanel<TrainingMenuPanel>(UIPanelId.TrainingMenu);
                    var choices = picker.transform.Find("Sheet/WeaponPicker/Viewport/Content").GetComponentsInChildren<Button>();
                    Check(choices.Length > 0, "No weapon choices");
                    choices.Last().onClick.Invoke();
                    Check(Object.FindObjectOfType<TrainingSceneController>().Player.GetComponent<WeaponController>().WeaponId == choices.Last().name, "Weapon selection binding lost");
                    break;
                case 11:
                    Capture("10-weapon-picker", 1920, 1080); Capture("10-weapon-picker-16x10", 1280, 800);
                    UIRoot.Instance.HandleBackInput();
                    Check(!Object.FindObjectOfType<TrainingSceneController>().IsInputBlocked, "Esc did not release input");
                    Object.FindObjectOfType<TrainingSceneController>().OpenTrainingMenu();
                    UIManager.Instance.GetPanel<TrainingMenuPanel>(UIPanelId.TrainingMenu).transform.Find("Sheet/Home/Return").GetComponent<Button>().onClick.Invoke();
                    break;
                case 12:
                    Capture("11-confirm-return", 1920, 1080);
                    UIRoot.Instance.HandleBackInput();
                    Check(UIManager.Instance.GetPanel<TrainingMenuPanel>(UIPanelId.TrainingMenu).IsOpen, "Confirm back did not restore menu");
                    UIManager.Instance.CloseAll(UILayer.Popup);
                    UIManager.Instance.ShowToast("装备已更新", 10);
                    break;
                case 13:
                    Capture("12-toast", 1920, 1080);
                    Check(Shader.GetGlobalTexture("_BlackGoldBackdrop") != null, "No glass scene capture");
                    UIManager.Instance.CloseAll(UILayer.Normal);
                    var toast = Object.FindObjectOfType<ToastPanel>();
                    if (toast != null) toast.gameObject.SetActive(false);
                    battleFixture = new GameObject("UI-only battle fixture").AddComponent<DemolitionBattleClient>();
                    battleFixture.Configure(AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BlacktideUI.ttf"), null);
                    typeof(DemolitionBattleClient).GetField("catalog", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(battleFixture, Resources.Load<EquipmentCatalog>("Loadout/EquipmentCatalog"));
                    typeof(DemolitionBattleClient).GetMethod("BuildHud", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(battleFixture, null);
                    battleSheet = (RectTransform)typeof(DemolitionBattleClient).GetField("hudSheet", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(battleFixture);
                    battleSheet.SetParent(UIRoot.Instance.GetLayerRoot(UILayer.Normal), false);
                    DemolitionUI.Fit(battleSheet, 0);
                    battleSheet.Find("Buy menu").gameObject.SetActive(true);
                    break;
                case 14:
                    VerifyControls(battleSheet.gameObject); Capture("13-battle-buy", 1920, 1080);
                    battleSheet.Find("Buy menu").gameObject.SetActive(false);
                    battleSheet.Find("Pause menu").gameObject.SetActive(true);
                    break;
                case 15:
                    Capture("14-battle-menu", 1280, 800);
                    Object.Destroy(battleSheet.gameObject); Object.Destroy(battleFixture.gameObject);
                    GameSceneFlow.Instance.Logout();
                    break;
                case 16:
                    Check(UIManager.Instance.GetPanel<LoginPanel>(UIPanelId.Login)?.IsOpen == true, "Logout failed to restore login");
                    Check(!ShaderUtil.ShaderHasError(Shader.Find("OperationBlacktide/UI/Black Gold Glass")), "Rendered glass shader errors");
                    File.WriteAllText(Output + "/verification.txt", "PASS: all prefab references; themed static and dynamic controls; login validation; focus/hover; primary normal/hover/pressed/disabled renders; agent tab, faction/character selection and tab isolation; loadout navigation; deployment/browser/dialog; training HUD; modal input blocking; weapon selection; Esc/return; toast; battle buy/pause UI fixtures (no server transactions); logout; glass capture; 16:9, 16:10, 21:9 renders.\n");
                    SessionState.SetBool(Active, false); Debug.Log("BLACK_GOLD_VERIFY_PASS"); EditorApplication.Exit(0); return;
            }
            Debug.Log("BLACK_GOLD_VERIFY_PROGRESS: step " + step);
            step++; next = EditorApplication.timeSinceStartup + (step == 2 || step == 8 || step == 16 ? 5 : .65);
        }
        catch (Exception e) { Fail(e); }
    }
    static Button FindButton(GameObject root, string text) => root.GetComponentsInChildren<Button>(true).First(b => b.GetComponentsInChildren<Text>(true).Any(t => t.text == text));
    static bool CapturePrimaryStates(LobbyPanel lobby)
    {
        var button = lobby.transform.Find("PlayControls/EnterGameButton").GetComponent<Button>();
        var glass = button.GetComponent<UIGlassEffect>();
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        switch (primaryState)
        {
            case 0:
                Capture("03-lobby", 1920, 1080); Capture("03-lobby-ultrawide", 2560, 1080);
                glass.OnPointerEnter(pointer);
                break;
            case 1:
                Capture("03-lobby-hover", 1920, 1080);
                glass.OnPointerDown(pointer);
                break;
            case 2:
                Capture("03-lobby-pressed", 1920, 1080);
                glass.OnPointerUp(pointer); button.interactable = false;
                break;
            case 3:
                Capture("03-lobby-disabled", 1920, 1080);
                button.interactable = true; glass.OnPointerExit(pointer);
                break;
            default: return true;
        }
        primaryState++; next = EditorApplication.timeSinceStartup + .35;
        return false;
    }
    static void VerifyControls(GameObject root)
    {
        foreach (var control in root.GetComponentsInChildren<Selectable>(true))
        {
            if (control.name == "Dismiss" || control.name.Contains("Blocker")) continue;
            Check(control.targetGraphic != null && control.targetGraphic.GetComponent<UIGlassEffect>() != null, "Unthemed control: " + control.name);
        }
    }
    static void Capture(string name, int width, int height)
    {
        var camera = Camera.main;
        var canvas = UIRoot.Instance.GetComponentInChildren<Canvas>();
        var scaler = canvas.GetComponent<CanvasScaler>();
        bool scalerEnabled = scaler.enabled; float previousScale = canvas.scaleFactor;
        scaler.enabled = false; canvas.scaleFactor = Mathf.Sqrt((float)width / 1920 * height / 1080);
        var vision = Resources.FindObjectsOfTypeAll<ScriptableRendererData>().SelectMany(r => r.rendererFeatures)
            .OfType<OperationBlacktide.Client.Gameplay.Vision.CharacterVisionRendererFeature>().Where(f => f.isActive).ToArray();
        var sceneTarget = new RenderTexture(width, height, 24);
        var target = new RenderTexture(width, height, 24);
        int mask = camera.cullingMask, layer = canvas.gameObject.layer;
        var data = camera.GetUniversalAdditionalCameraData(); bool post = data.renderPostProcessing;
        GameObject background = null; Texture2D pixels = null;
        var oldTarget = RenderTexture.active;
        try
        {
            canvas.enabled = false;
            camera.targetTexture = sceneTarget; camera.aspect = (float)width / height;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = sceneTarget });
            if (name == "03-lobby")
            {
                Dump(sceneTarget, "scene-buffer");
                if (Shader.GetGlobalTexture("_BlackGoldBackdrop") is RenderTexture glass) Dump(glass, "glass-buffer");
            }
            // The world pass already contains sight shading. A second world-only effect would incorrectly shade UI.
            foreach (var feature in vision) feature.SetActive(false);
            background = new GameObject("CaptureBackground", typeof(RectTransform), typeof(RawImage));
            background.layer = 5; background.transform.SetParent(canvas.transform, false); background.transform.SetAsFirstSibling();
            var rect = (RectTransform)background.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            background.GetComponent<RawImage>().texture = sceneTarget;
            canvas.enabled = true; canvas.gameObject.layer = 5;
            camera.cullingMask = 1 << 5; data.renderPostProcessing = false; camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = .5f;
            Canvas.ForceUpdateCanvases();
            if (name.StartsWith("01-login", StringComparison.Ordinal))
            {
                var footer = UIManager.Instance.GetPanel<LoginPanel>(UIPanelId.Login).transform.Find("Footer");
                foreach (var label in footer != null ? footer.GetComponentsInChildren<Text>() : Array.Empty<Text>())
                {
                    var corners = new Vector3[4]; label.rectTransform.GetWorldCorners(corners);
                    foreach (var corner in corners)
                    {
                        var position = camera.WorldToScreenPoint(corner);
                        Check(position.x >= 0 && position.x <= width && position.y >= 0 && position.y <= height, "Login footer escaped the viewport: " + label.name);
                    }
                }
            }
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = oldTarget;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; canvas.gameObject.layer = layer; canvas.enabled = true;
            canvas.scaleFactor = previousScale; scaler.enabled = scalerEnabled;
            foreach (var feature in vision) feature.SetActive(true);
            camera.targetTexture = null; camera.ResetAspect(); camera.cullingMask = mask; data.renderPostProcessing = post;
            if (background != null) Object.DestroyImmediate(background); if (pixels != null) Object.DestroyImmediate(pixels);
            sceneTarget.Release(); target.Release(); Object.DestroyImmediate(sceneTarget); Object.DestroyImmediate(target);
        }
    }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static void Dump(RenderTexture texture, string name)
    {
        var active = RenderTexture.active; RenderTexture.active = texture;
        var pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); pixels.Apply();
        File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
        Object.DestroyImmediate(pixels); RenderTexture.active = active;
    }
    static void Fail(Exception e)
    {
        SessionState.SetBool(Active, false); Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/failure.txt", e.ToString()); Debug.LogException(e); EditorApplication.Exit(1);
    }
}
