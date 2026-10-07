using System;
using System.IO;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Features.Account;
using OperationBlacktide.Client.Network;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Shared.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Copied into Assets/Editor only in the isolated validation project.
[InitializeOnLoad]
public static class VerifyLoginMemory
{
    private const string Active = "OperationBlacktide.LoginMemoryValidation";
    private const string User1 = "MemoryUser1", User2 = "MemoryUser2";
    private const string Password1 = "LoginTest_2048!", Password2 = " P@ss\\密 2048 ";
    private static int step;
    private static double deadline;
    private static AuthClient auth;
    private static LoginResponse login;
    private static RegisterResponse registration;
    private static string error;
    private static int Port => int.Parse(File.ReadAllText("login-memory-test-port.txt"));

    static VerifyLoginMemory()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (SessionState.GetBool(Active, false) && (type == LogType.Error || type == LogType.Exception))
                error = "Unity raised an error; inspect the private test log.";
        };
    }

    public static void Run()
    {
        StoreChecks();
        Start(false);
    }

    public static void Restart() => Start(true);

    private static void StoreChecks()
    {
        string host = "login-memory-" + Guid.NewGuid().ToString("N") + ".invalid";
        try
        {
            Check(!LoginCredentialStore.TryLoad(host, 1, out _, out _), "Fresh credential target was not empty.");
            Check(LoginCredentialStore.TrySave(host, 1, User1, Password2), "Windows credential write failed.");
            Check(LoginCredentialStore.TryLoad(host.ToUpperInvariant(), 1, out var user, out var password)
                  && user == User1 && password == Password2, "Unicode/whitespace credential round trip failed.");
            Check(!LoginCredentialStore.TryLoad(host, 2, out _, out _), "Credentials leaked to another endpoint.");
            Check(!LoginCredentialStore.TrySave(host, 1, User2, "short"), "Invalid credential replaced the saved login.");
            Check(LoginCredentialStore.TryLoad(host, 1, out user, out password) && user == User1 && password == Password2,
                  "Rejected write damaged the saved login.");
        }
        finally { Check(LoginCredentialStore.Forget(host, 1), "Could not remove the isolated test credential."); }
        Debug.Log("LOGIN_MEMORY_STORE_PASS: native vault, exact Unicode/whitespace, endpoint isolation and invalid-write preservation.");
    }

    private static void Start(bool restart)
    {
        var config = new SerializedObject(AssetDatabase.LoadAssetAtPath<AppConfig>(
            "Assets/Scripts/GClient/Runtime/App/AppConfig.asset"));
        config.FindProperty("serverHost").stringValue = "127.0.0.1";
        config.FindProperty("serverPort").intValue = Port;
        config.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(GameSceneFlow.LoginScenePath);
        SessionState.SetBool(Active + ".Restart", restart);
        SessionState.SetBool(Active, true);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
        try
        {
            if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 60;
            Check(error == null, error);
            Check(EditorApplication.timeSinceStartup < deadline, "Timeout at login-memory step " + step);
            if (SessionState.GetBool(Active + ".Restart", false))
            {
                if (!Ready(false)) return;
                CheckFields(User2, Password2);
                Check(!GameSceneFlow.Instance.Auth.Session.IsLoggedIn &&
                      !GameSceneFlow.Instance.Auth.GetComponent<TcpGameClient>().IsConnected,
                      "Remembering credentials must not automatically authenticate.");
                Check(LoginCredentialStore.Forget("127.0.0.1", Port), "Could not clean test credential.");
                Pass("RESTART: a fresh Unity process restores the last account/password into the masked form.");
                return;
            }

            switch (step)
            {
                case 0:
                    if (!Ready(false)) return;
                    auth = GameSceneFlow.Instance.Auth;
                    auth.LoginCompleted += value => login = value;
                    auth.RegisterCompleted += value => registration = value;
                    CheckFields("", "");
                    Submit("RegisterButton", User1, Password1); Next(); break;
                case 1:
                    if (registration == null) return;
                    Check(registration.Code == AuthResultCode.Ok, "First test registration failed.");
                    Check(!auth.TryGetRememberedLogin(out _, out _), "Registration saved a password before login.");
                    Submit("LoginButton", User1, "WrongPassword123"); Next(); break;
                case 2:
                    if (login == null) return;
                    Check(login.Code != AuthResultCode.Ok && !auth.Session.IsLoggedIn, "Invalid password accepted.");
                    Check(!auth.TryGetRememberedLogin(out _, out _), "Failed login was remembered.");
                    login = null;
                    Submit("LoginButton", User1, Password1);
                    // Edit the form and click again before the first response arrives.
                    Submit("LoginButton", "IgnoredUser", "IgnoredPass123");
                    Next(); break;
                case 3:
                    if (!Ready(true)) return;
                    Check(auth.Session.Username == User1, "Repeated click changed the pending login account.");
                    CheckSaved(User1, Password1);
                    Check(PasswordField.text == "", "Closed login panel retained its plaintext password.");
                    GameSceneFlow.Instance.Logout(); Next(); break;
                case 4:
                    if (!Ready(false)) return;
                    CheckFields(User1, Password1);
                    var ui = UIManager.Instance;
                    ui.CloseAll(UILayer.Normal);
                    ui.Push(UIPanelId.Login, new LoginOpenArgs { DefaultUsername = "ExplicitUser", DefaultPassword = "ExplicitPass123" });
                    CheckFields("ExplicitUser", "ExplicitPass123");
                    ui.CloseAll(UILayer.Normal); ui.Push(UIPanelId.Login);
                    CheckFields(User1, Password1);
                    login = null;
                    Submit("LoginButton", User1, "WrongAgain123"); Next(); break;
                case 5:
                    if (login == null) return;
                    Check(login.Code != AuthResultCode.Ok, "Wrong second password accepted.");
                    CheckSaved(User1, Password1);
                    registration = null;
                    Submit("RegisterButton", User2, Password2); Next(); break;
                case 6:
                    if (registration == null) return;
                    Check(registration.Code == AuthResultCode.Ok, "Second test registration failed.");
                    CheckSaved(User1, Password1);
                    login = null;
                    Submit("LoginButton", User2, Password2); Next(); break;
                case 7:
                    if (!Ready(true)) return;
                    CheckSaved(User2, Password2);
                    GameSceneFlow.Instance.Logout(); Next(); break;
                case 8:
                    if (!Ready(false)) return;
                    CheckFields(User2, Password2);
                    Pass("LIVE: success-only persistence, duplicate clicks, submitted-value capture, logout fill, explicit defaults, failed-login preservation and account replacement.");
                    break;
            }
        }
        catch (Exception exception)
        {
            SessionState.SetBool(Active, false);
            LoginCredentialStore.Forget("127.0.0.1", Port);
            Debug.LogError("LOGIN_MEMORY_FAIL: " + exception.Message);
            EditorApplication.Exit(1);
        }
    }

    private static LoginPanel Panel => UIManager.Instance.GetPanel<LoginPanel>(UIPanelId.Login);
    private static InputField UsernameField => Panel.transform.Find("LoginCard/UsernameInput").GetComponent<InputField>();
    private static InputField PasswordField => Panel.transform.Find("LoginCard/PasswordInput").GetComponent<InputField>();
    private static void CheckFields(string username, string password)
    {
        Check(UsernameField.text == username && PasswordField.text == password, "Login form defaults did not match the expected account.");
        Check(PasswordField.contentType == InputField.ContentType.Password, "Password is not visually masked.");
    }
    private static void CheckSaved(string username, string password) =>
        Check(auth.TryGetRememberedLogin(out var u, out var p) && u == username && p == password,
              "Remembered credential did not match the successful submitted login.");
    private static bool Ready(bool lobby) => GameSceneFlow.Instance != null && !GameSceneFlow.Instance.IsLoading &&
        SceneManager.GetActiveScene().path == (lobby ? GameSceneFlow.LobbyScenePath : GameSceneFlow.LoginScenePath) &&
        UIManager.Instance != null && UIManager.Instance.TryGetTopPanelId(UILayer.Normal, out var id) && id == (lobby ? "Lobby" : "Login");
    private static void Submit(string button, string user, string password)
    {
        UsernameField.text = user; PasswordField.text = password;
        Panel.transform.Find("LoginCard/" + button).GetComponent<Button>().onClick.Invoke();
    }
    private static void Next() { step++; deadline = EditorApplication.timeSinceStartup + 60; }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Pass(string result)
    {
        SessionState.SetBool(Active, false);
        Debug.Log("LOGIN_MEMORY_PASS: " + result);
        EditorApplication.Exit(0);
    }
}
