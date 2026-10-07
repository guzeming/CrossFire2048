using System;
using System.Collections;
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

        public static GameSceneFlow Instance { get; private set; }
        public AuthClient Auth { get; private set; }
        public bool IsLoading { get; private set; }

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
            if (Instance == this) Instance = null;
        }

        public void Logout()
        {
            if (IsLoading) return;
            Auth.Logout();
            LoadScene(LoginScenePath);
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
            return scenePath == LobbyScenePath || scenePath == TrainingScenePath;
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
            AsyncOperation operation;
            try
            {
                operation = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
            }
            catch (Exception exception)
            {
                IsLoading = false;
                Debug.LogException(exception);
                UIManager.Instance?.ShowToast("场景加载失败");
                yield break;
            }

            yield return operation;
            IsLoading = false;

            string activeScenePath = SceneManager.GetActiveScene().path;
            // 处理场景加载过程中发生的断线，避免保留无有效会话的游戏场景。
            if (RequiresLogin(activeScenePath) && !Auth.Session.IsLoggedIn)
                LoadScene(LoginScenePath);
        }
    }
}
