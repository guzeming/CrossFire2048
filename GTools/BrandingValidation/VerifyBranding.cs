using System;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Features.Account;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Copied into an isolated Unity project's Assets/Editor by verify-branding.ps1.
[InitializeOnLoad]
public static class VerifyBranding
{
    private const string Active = "Blacktide.BrandingVerification";
    private static int step;
    private static double next, started;
    private static string failure;

    static VerifyBranding()
    {
        EditorApplication.update += Update;
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (SessionState.GetBool(Active, false) && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                failure = message;
        };
    }

    public static void Run()
    {
        try
        {
            Check(PlayerSettings.productName == "Operation Blacktide", "Wrong product name.");
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Art/Branding/BlacktideEmblem.png");
            Check(PlayerSettings.GetIconsForTargetGroup(BuildTargetGroup.Unknown).Contains(icon), "Default app icon is not assigned.");
            foreach (string name in new[] { "BlacktideLogo", "BlacktideHorizontal", "BlacktideEmblem" })
            {
                string path = "Assets/UI/Art/Branding/" + name + ".png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Check(importer.textureType == TextureImporterType.Sprite && importer.alphaIsTransparency, "Incorrect sprite import: " + name);
                Check(AssetDatabase.LoadAssetAtPath<Sprite>(path) != null, "Missing sprite: " + name);
            }
            VerifySaveMigration();
            SessionState.SetBool(Active, true);
            EditorSceneManager.OpenScene(GameSceneFlow.LoginScenePath);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception ex) { Fail(ex); }
    }

    private static void Update()
    {
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
        if (started == 0) { started = EditorApplication.timeSinceStartup; next = started + 3; }
        try
        {
            Check(failure == null, failure);
            Check(EditorApplication.timeSinceStartup - started < 90, "Branding verification timed out.");
            if (EditorApplication.timeSinceStartup < next) return;
            if (step == 0)
            {
                var login = UIManager.Instance.GetPanel<LoginPanel>(UIPanelId.Login);
                Check(login != null && login.IsOpen, "Login panel did not open.");
                VerifyPanel(login.gameObject, "Brand", "BlacktideLogo");
                Capture("blacktide-login-1920x1080.png", 1920, 1080);
                Capture("blacktide-login-1280x800.png", 1280, 800);
                var username = login.transform.Find("LoginCard/UsernameInput").GetComponent<InputField>();
                var password = login.transform.Find("LoginCard/PasswordInput").GetComponent<InputField>();
                username.text = "x"; password.text = "123456";
                login.transform.Find("LoginCard/LoginButton").GetComponent<Button>().onClick.Invoke();
                Check(login.transform.Find("LoginCard/StatusText").GetComponent<Text>().text.Contains("用户名"), "Login button binding was lost.");
                GameSceneFlow.Instance.Auth.Session.Set("branding-verification", "BlacktideOperator", "local-verification");
                SceneManager.LoadSceneAsync(GameSceneFlow.LobbyScenePath);
                step = 1; next = EditorApplication.timeSinceStartup + 4;
                return;
            }
            if (step == 1)
            {
                var lobby = UIManager.Instance.GetPanel<LobbyPanel>(UIPanelId.Lobby);
                if (lobby == null || !lobby.IsOpen) return;
                VerifyPanel(lobby.gameObject, "TopBar/Logo", "BlacktideHorizontal");
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, "Missing scene script: " + t.name);
                foreach (string name in new[] { "HangarPlate", "HangarGround", "HangarMotes" })
                {
                    var shader = Shader.Find("OperationBlacktide/Lobby/" + name);
                    Check(shader != null && !ShaderUtil.ShaderHasError(shader), "Invalid renamed shader: " + name);
                }
                Capture("blacktide-lobby-1920x1080.png", 1920, 1080);
                Capture("blacktide-lobby-1280x800.png", 1280, 800);
                lobby.transform.Find("TopBar/Navigation/Tab1").GetComponent<Button>().onClick.Invoke();
                Check(lobby.SelectedTabIndex == 1, "Lobby navigation binding was lost.");
                GameSceneFlow.Instance.Logout();
                step = 2; next = EditorApplication.timeSinceStartup + 3;
                return;
            }
            Check(UIManager.Instance.GetPanel<LoginPanel>(UIPanelId.Login)?.IsOpen == true, "Logout did not return to branded login.");
            File.WriteAllText("branding-verification.txt", "PASS: product/icon, sprite import, font glyphs, prefab/script references, login input validation, lobby navigation, logout, renamed shaders, old save migration/new save precedence, 16:9 and 16:10 renders.\n");
            Debug.Log("BLACKTIDE_BRANDING_PASS");
            SessionState.SetBool(Active, false);
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Fail(ex); }
    }

    private static void VerifyPanel(GameObject panel, string logoPath, string spriteName)
    {
        var logo = panel.transform.Find(logoPath).GetComponent<Image>();
        Check(logo.sprite != null && logo.sprite.name == spriteName && logo.preserveAspect && !logo.raycastTarget, "Invalid logo binding.");
        foreach (var t in panel.GetComponentsInChildren<Transform>(true))
            Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, "Missing prefab script: " + t.name);
        foreach (var label in panel.GetComponentsInChildren<Text>(true))
        {
            Check(!label.text.ToUpperInvariant().Contains("CROSSFIRE") && !label.text.Contains("2048"), "Old brand label: " + label.name);
            foreach (char c in label.text)
                Check(char.IsWhiteSpace(c) || label.font.HasCharacter(c), "Missing font glyph: " + c);
        }
    }

    private static void VerifySaveMigration()
    {
        var catalog = ScriptableObject.CreateInstance<LobbyLoadoutCatalog>();
        catalog.agents = new[] { new LobbyAgent { id = "ctm_sas", team = LobbyTeam.CT }, new LobbyAgent { id = "tm_phoenix", team = LobbyTeam.T } };
        catalog.weapons = new[] { new LobbyWeapon { id = "test_rifle", slot = "rifle.main", teams = 3, held = true } };
        string user = "branding-test-" + Guid.NewGuid().ToString("N");
        string oldKey = "CrossFire2048.Loadout.v1." + user;
        string newKey = "OperationBlacktide.Loadout.v1." + user;
        try
        {
            string saved = JsonUtility.ToJson(new LobbyLoadoutData { activeTeam = LobbyTeam.T });
            PlayerPrefs.SetString(oldKey, saved);
            var migrated = new LobbyLoadoutStore(catalog, user);
            Check(migrated.Team == LobbyTeam.T && PlayerPrefs.HasKey(newKey) && PlayerPrefs.HasKey(oldKey), "Legacy save migration failed.");
            migrated.SelectTeam(LobbyTeam.CT);
            Check(new LobbyLoadoutStore(catalog, user).Team == LobbyTeam.CT, "Legacy data replaced a newer save.");
#if UNITY_EDITOR_WIN
            string windowsUser = Environment.GetEnvironmentVariable("BLACKTIDE_MIGRATION_TEST_USER");
            Check(!string.IsNullOrEmpty(windowsUser), "Missing isolated Windows save fixture.");
            string windowsKey = "OperationBlacktide.Loadout.v1." + windowsUser;
            try
            {
                Check(new LobbyLoadoutStore(catalog, windowsUser).Team == LobbyTeam.T && PlayerPrefs.HasKey(windowsKey), "Previous product's Windows save was not migrated.");
            }
            finally { PlayerPrefs.DeleteKey(windowsKey); }
#endif
        }
        finally
        {
            PlayerPrefs.DeleteKey(oldKey); PlayerPrefs.DeleteKey(newKey); PlayerPrefs.Save();
            Object.DestroyImmediate(catalog);
        }
    }

    private static void Capture(string path, int width, int height)
    {
        var camera = Camera.main;
        var canvas = UIRoot.Instance.GetComponentInChildren<Canvas>();
        var sceneTarget = new RenderTexture(width, height, 24);
        var target = new RenderTexture(width, height, 24);
        int mask = camera.cullingMask;
        int canvasLayer = canvas.gameObject.layer;
        var data = camera.GetUniversalAdditionalCameraData();
        bool post = data.renderPostProcessing;
        canvas.enabled = false;
        camera.targetTexture = sceneTarget; camera.aspect = (float)width / height;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = sceneTarget });
        var background = new GameObject("CaptureBackground", typeof(RectTransform));
        background.layer = 5; background.transform.SetParent(canvas.transform, false); background.transform.SetAsFirstSibling();
        var rect = (RectTransform)background.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        background.AddComponent<RawImage>().texture = sceneTarget;
        canvas.enabled = true; canvas.gameObject.layer = 5;
        camera.cullingMask = 1 << 5; data.renderPostProcessing = false; camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = .5f;
        Canvas.ForceUpdateCanvases();
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var previous = RenderTexture.active; RenderTexture.active = target;
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
        File.WriteAllBytes(path, pixels.EncodeToPNG());
        var button = pixels.GetPixel((int)(width * .82f), (int)(height * (SceneManager.GetActiveScene().name == "LobbyScene" ? .125f : .366f)));
        Check(button.r > .65f && button.g > .25f && button.b < .4f, "UI overlay is missing from capture: " + path);
        RenderTexture.active = previous;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
        canvas.gameObject.layer = canvasLayer;
        camera.targetTexture = null; camera.ResetAspect(); camera.cullingMask = mask; data.renderPostProcessing = post;
        Object.DestroyImmediate(background); Object.DestroyImmediate(pixels);
        sceneTarget.Release(); target.Release(); Object.DestroyImmediate(sceneTarget); Object.DestroyImmediate(target);
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Fail(Exception ex)
    {
        SessionState.SetBool(Active, false);
        File.WriteAllText("branding-verification.txt", ex.ToString());
        Debug.LogException(ex); EditorApplication.Exit(1);
    }
}
