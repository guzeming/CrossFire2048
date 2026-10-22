using System;
using System.Collections;
using System.Collections.Generic;
using OperationBlacktide.Client.Common;
using OperationBlacktide.Client.Features.Account;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Shared.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OperationBlacktide.Client.App
{
    /// <summary>保留账号和网络对象，协调登录、大厅和训练场之间的切换。</summary>
    [DefaultExecutionOrder(-200)]
    [RequireComponent(typeof(AuthClient))]
    public sealed class GameSceneFlow : MonoBehaviour
    {
        public const string LoginScenePath = "Assets/Scenes/SampleScene.unity";
        public const string LobbyScenePath = "Assets/Scenes/LobbyScene.unity";
        public const string TrainingScenePath = "Assets/Scenes/DustII.unity";
        public const string BattleScenePath = "Assets/Scenes/BattleArena.unity";

        public static GameSceneFlow Instance { get; private set; }
        public AuthClient Auth { get; private set; }
        public bool IsLoading { get; private set; }
        private SceneLoadingPanel loadingPanel;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Auth = GetComponent<AuthClient>();
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            Auth.LoginCompleted += OnLoginCompleted;
            GameEvents.Subscribe<string>(GameEventId.NetworkDisconnected, OnDisconnected);
        }

        private void OnDisable()
        {
            if (Auth != null) Auth.LoginCompleted -= OnLoginCompleted;
            GameEvents.Unsubscribe<string>(GameEventId.NetworkDisconnected, OnDisconnected);
        }

        private void Start()
        {
            // 带有场景流程的大厅和训练场必须先登录。
            if (RequiresLogin(SceneManager.GetActiveScene().path) && !Auth.Session.IsLoggedIn)
                LoadScene(LoginScenePath);
        }

        private void OnDestroy()
        {
            if (Instance == this && loadingPanel != null) UIManager.Instance?.Close(UIPanelId.Loading);
            if (Instance == this) Instance = null;
        }

        public void Logout()
        {
            if (IsLoading) return;
            Auth.Logout();
            LoadScene(LoginScenePath);
        }

        public static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void EnterTrainingGround()
        {
            if (IsLoading) return;
            LoadScene(Auth.Session.IsLoggedIn ? TrainingScenePath : LoginScenePath);
        }

        public void ReturnToLobby()
        {
            if (IsLoading) return;
            LoadScene(Auth.Session.IsLoggedIn ? LobbyScenePath : LoginScenePath);
        }

        public void RequestBattleArena()
        {
            if (IsLoading) return;
            var gateway = GetComponent<OperationBlacktide.Client.Features.Battle.BattleGateway>();
            if (gateway == null) gateway = gameObject.AddComponent<OperationBlacktide.Client.Features.Battle.BattleGateway>();
            gateway.Join();
        }

        public void EnterBattleArena()
        {
            if (!IsLoading) LoadScene(Auth.Session.IsLoggedIn ? BattleScenePath : LoginScenePath);
        }

        private void OnLoginCompleted(LoginResponse response)
        {
            if (response != null && response.Code == AuthResultCode.Ok && Auth.Session.IsLoggedIn)
                LoadScene(LobbyScenePath);
        }

        private void OnDisconnected(string reason)
        {
            if (!IsLoading && RequiresLogin(SceneManager.GetActiveScene().path))
            {
                UIManager.Instance?.ShowToast("连接已断开，请重新登录");
                LoadScene(LoginScenePath);
            }
        }

        private static bool RequiresLogin(string scenePath)
        {
            return scenePath == LobbyScenePath || scenePath == TrainingScenePath || scenePath == BattleScenePath;
        }

        private void LoadScene(string scenePath)
        {
            if (IsLoading || SceneManager.GetActiveScene().path == scenePath) return;
            if (!Application.CanStreamedLevelBeLoaded(scenePath))
            {
                Debug.LogError($"[GameSceneFlow] 场景未加入 Build Settings：{scenePath}");
                UIManager.Instance?.ShowToast("场景加载失败");
                return;
            }

            StartCoroutine(LoadSceneRoutine(scenePath));
        }

        private IEnumerator LoadSceneRoutine(string scenePath)
        {
            IsLoading = true;
            AsyncOperation operation = null;
            try
            {
                if (UIRoot.Instance == null) new GameObject("UIRoot").AddComponent<UIRoot>();
                loadingPanel = UIManager.Instance.Push<SceneLoadingPanel>(UIPanelId.Loading);
                if (loadingPanel == null)
                {
                    Debug.LogError("[GameSceneFlow] 缺少 SceneLoadingPanel 预制体。");
                    UIManager.Instance.ShowToast("场景加载失败");
                    yield break;
                }

                while (true)
                {
                    bool isMap = scenePath == TrainingScenePath || scenePath == BattleScenePath;
                    string title = scenePath == LobbyScenePath ? "大厅" :
                        scenePath == TrainingScenePath ? "训练场 · 沙 2" : isMap ? "沙 2" : "登录";
                    loadingPanel.SetDestination(title, isMap);
                    // 先绘制遮罩，避免 LoadSceneAsync 的同步准备工作仍露出上一场景。
                    yield return null;

                    operation = null;
                    try { operation = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single); }
                    catch (Exception exception) { Debug.LogException(exception); }
                    if (operation == null)
                    {
                        UIManager.Instance.ShowToast("场景加载失败");
                        yield break;
                    }

                    operation.allowSceneActivation = false;
                    while (operation.progress < .9f)
                    {
                        loadingPanel.SetProgress(operation.progress, "正在加载场景");
                        yield return null;
                    }
                    loadingPanel.SetProgress(.9f, "正在进入场景");
                    yield return null;
                    operation.allowSceneActivation = true;
                    while (!operation.isDone) yield return null;
                    // isDone 不保证场景中全部 Start 已执行；保留遮罩至下一帧。
                    yield return null;

                    Scene scene = SceneManager.GetActiveScene();
                    if (RequiresLogin(scene.path) && !Auth.Session.IsLoggedIn)
                    {
                        // 断线重定向仍处在同一次遮罩内，不闪出未登录的大厅/战场。
                        scenePath = LoginScenePath;
                        continue;
                    }

                    var participants = new List<MonoBehaviour>();
                    foreach (GameObject root in scene.GetRootGameObjects())
                        foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>())
                            if (component is ISceneLoadParticipant && component.isActiveAndEnabled)
                                participants.Add(component);
                    loadingPanel.SetProgress(.95f, "正在连接对局");
                    // 战斗客户端仍持续轮询网络，首个快照或可操作的失败界面出现后再揭开遮罩。
                    float deadline = Time.realtimeSinceStartup + 20;
                    while (!ParticipantsReady(participants) && Time.realtimeSinceStartup < deadline &&
                           (!RequiresLogin(scene.path) || Auth.Session.IsLoggedIn))
                        yield return null;
                    if (RequiresLogin(scene.path) && !Auth.Session.IsLoggedIn)
                    {
                        scenePath = LoginScenePath;
                        continue;
                    }
                    if (!ParticipantsReady(participants))
                        UIManager.Instance.ShowToast("对局准备超时，请返回大厅重试", 4);
                    else
                    {
                        loadingPanel.SetProgress(1, "加载完成");
                        yield return null;
                    }
                    if (RequiresLogin(scene.path) && !Auth.Session.IsLoggedIn)
                    {
                        scenePath = LoginScenePath;
                        continue;
                    }
                    break;
                }
            }
            finally
            {
                if (operation != null && !operation.isDone) operation.allowSceneActivation = true;
                UIManager.Instance?.Close(UIPanelId.Loading);
                loadingPanel = null;
                IsLoading = false;
            }
        }

        private static bool ParticipantsReady(List<MonoBehaviour> participants)
        {
            foreach (MonoBehaviour participant in participants)
                if (participant != null && participant.isActiveAndEnabled &&
                    !((ISceneLoadParticipant)participant).IsSceneReady) return false;
            return true;
        }
    }
}
