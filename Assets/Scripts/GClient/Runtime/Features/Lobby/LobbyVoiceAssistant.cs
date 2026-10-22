using System;
using System.Collections.Generic;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Lobby
{
    public enum LobbyVoiceState { Loading, Idle, Listening, Thinking, Speaking, Error }

    public sealed class LobbyVoiceAssistant : MonoBehaviour
    {
        [SerializeField] private Text statusText, playerText, assistantText, actionText;
        [SerializeField] private GameObject conversation;
        [SerializeField] private Button talkButton;
        [SerializeField] private LobbyVoiceWaveform waveform;
        [SerializeField] private AudioSource voiceSource;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private float silenceTimeout = 8;
        private LocalAssistantProcess backend;
        private readonly Queue<AudioClip> audioQueue = new Queue<AudioClip>();
        private readonly float[] outputSamples = new float[512];
        private AudioClip microphone;
        private float[] capture;
        private int readPosition, turn, frameSamples;
        private float listeningSince, replySince;
        private bool ready, finished, startWhenReady, hasTranscript, previewMode;
        private RectTransform rect;
        private static readonly Color PlayerColor = new Color32(130, 215, 221, 255);
        public LobbyVoiceState State { get; private set; }
        public string PlayerTranscript => playerText.text;
        public string AssistantTranscript => assistantText.text;

        [Serializable] private sealed class Message
        {
            public string type, text, pcm;
            public int turn, sampleRate;
        }

        public static void Attach(LobbyPanel panel)
        {
            if (panel.GetComponentInChildren<LobbyVoiceAssistant>(true) != null) return;
            var prefab = Resources.Load<GameObject>("UI/LobbyAssistant");
            if (prefab != null) Instantiate(prefab, panel.transform, false);
        }

        private void Awake()
        {
            rect = (RectTransform)transform;
            voiceSource.playOnAwake = false; voiceSource.loop = false; voiceSource.spatialBlend = 0;
            voiceSource.ignoreListenerPause = true;
            talkButton.onClick.AddListener(Activate);
            LocalAssistantSettings.Changed += ApplyPreference;
        }
        private void OnDestroy() { LocalAssistantSettings.Changed -= ApplyPreference; }
        private void ApplyPreference()
        {
            if (previewMode) return;
            if (!LocalAssistantSettings.Enabled)
            {
                Shutdown(); group.alpha = 0; group.interactable = group.blocksRaycasts = false;
                conversation.SetActive(false); return;
            }
            group.alpha = 1; group.interactable = group.blocksRaycasts = true;
            if (isActiveAndEnabled) StartBackend();
        }
        private void OnEnable()
        {
            if (!Application.isPlaying || previewMode) return;
            State = LobbyVoiceState.Idle; statusText.text = "大厅助手"; actionText.text = "对话";
            conversation.SetActive(false); waveform.Clear(); hasTranscript = false;
            playerText.text = ""; assistantText.text = "";
            ApplyPreference();
        }
        private void StartBackend()
        {
            if (previewMode || backend != null || !LocalAssistantSettings.Enabled) return;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            string root = LocalAssistantInstallation.FindModels();
            if (root == null) { SetState(LobbyVoiceState.Error, "请在设置中下载 AI 助手"); return; }
            try { backend = new LocalAssistantProcess(LocalAssistantInstallation.FindHost(root), root); SetState(LobbyVoiceState.Loading, "正在加载本地助手…"); }
            catch (Exception) { SetState(LobbyVoiceState.Error, "本地助手启动失败，按 T 重试"); }
#else
            SetState(LobbyVoiceState.Error, "本地助手目前支持 Windows");
#endif
        }

        private bool InputBlocked()
        {
            if (GameSceneFlow.Instance != null && GameSceneFlow.Instance.IsLoading) return true;
            if (UIManager.Instance != null && UIManager.Instance.GetStackCount(UILayer.Popup) > 0) return true;
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.GetComponentInParent<InputField>() != null;
        }
        private void Update()
        {
            if (previewMode || !LocalAssistantSettings.Enabled) return;
            if (backend != null)
            {
                int count = 0;
                while (count++ < 100 && backend != null && backend.TryRead(out string line))
                {
                    try { Handle(JsonUtility.FromJson<Message>(line)); }
                    catch (Exception) { Fail("本地助手返回了无效数据，请按 T 重试"); }
                }
                if (backend != null && backend.HasExited) { backend.Dispose(); backend = null; ready = false; Fail("本地助手已停止，按 T 重试"); }
            }
            bool blocked = InputBlocked();
            talkButton.interactable = !blocked;
            group.alpha = blocked ? .35f : 1;
            if (blocked && (State == LobbyVoiceState.Listening || State == LobbyVoiceState.Speaking || State == LobbyVoiceState.Thinking)) CancelTurn();
            if (!blocked && Input.GetKeyDown(KeyCode.T)) Activate();
            if (State == LobbyVoiceState.Listening) CaptureMicrophone();
            PumpPlayback();
            if (State == LobbyVoiceState.Thinking && Time.unscaledTime - replySince > 65) { CancelTurn(); Fail("回复超时，请按 T 重试"); }
            bool expanded = hasTranscript || State == LobbyVoiceState.Listening || State == LobbyVoiceState.Thinking || State == LobbyVoiceState.Speaking;
            rect.sizeDelta = Vector2.Lerp(rect.sizeDelta, expanded ? new Vector2(560, 164) : new Vector2(310, 46), 1 - Mathf.Exp(-15 * Time.unscaledDeltaTime));
            conversation.SetActive(expanded);
        }

        public void Activate()
        {
            if (!LocalAssistantSettings.Enabled || InputBlocked()) return;
            if (State == LobbyVoiceState.Listening)
            {
                CaptureMicrophone(true);
                if (State != LobbyVoiceState.Listening) return;
                StopMicrophone(); Send("end"); SetState(LobbyVoiceState.Thinking, "正在识别…"); replySince = Time.unscaledTime; return;
            }
            if (backend == null) { StartBackend(); if (backend == null) return; }
            if (!ready) { startWhenReady = true; return; }
            BeginListening();
        }
        private void BeginListening()
        {
            CancelTurn();
            if (Microphone.devices.Length == 0) { Fail("未找到麦克风，请连接后按 T 重试"); return; }
            try
            {
                microphone = Microphone.Start(null, true, 3, 16000);
                if (microphone == null) { Fail("无法打开麦克风，请检查系统权限"); return; }
                frameSamples = Mathf.Max(1, microphone.frequency / 10);
                capture = new float[frameSamples * microphone.channels]; readPosition = 0;
                listeningSince = Time.unscaledTime; finished = false;
                playerText.text = "…"; assistantText.text = ""; hasTranscript = true;
                Send("begin"); SetState(LobbyVoiceState.Listening, "正在听你说");
            }
            catch (Exception) { StopMicrophone(); Fail("无法打开麦克风，请检查系统权限"); }
        }
        private void CaptureMicrophone(bool flush = false)
        {
            if (microphone == null) return;
            int position = Microphone.GetPosition(null);
            if (position < 0 || (!Microphone.IsRecording(null)) || (position == 0 && Time.unscaledTime - listeningSince > 3))
            { CancelTurn(); Fail("麦克风没有输入，请检查系统权限"); return; }
            int available = (position - readPosition + microphone.samples) % microphone.samples;
            while (available >= frameSamples || (flush && available > 0))
            {
                int count = Mathf.Min(available, frameSamples);
                float[] samples = count == frameSamples ? capture : new float[count * microphone.channels];
                microphone.GetData(samples, readPosition); waveform.SetSamples(samples, microphone.channels);
                byte[] pcm = new byte[count * 2];
                for (int i = 0; i < count; i++)
                {
                    float mono = 0;
                    for (int channel = 0; channel < microphone.channels; channel++) mono += samples[i * microphone.channels + channel];
                    short value = (short)Mathf.RoundToInt(Mathf.Clamp(mono / microphone.channels, -1, 1) * 32767);
                    pcm[i * 2] = (byte)value; pcm[i * 2 + 1] = (byte)(value >> 8);
                }
                Send("audio", Convert.ToBase64String(pcm), microphone.frequency);
                if (microphone == null) return;
                readPosition = (readPosition + count) % microphone.samples; available -= count;
            }
            if (Time.unscaledTime - listeningSince > 32 || (Time.unscaledTime - listeningSince > silenceTimeout && playerText.text == "…"))
            { StopMicrophone(); Send("end"); SetState(LobbyVoiceState.Thinking, "正在识别…"); replySince = Time.unscaledTime; }
        }
        private void Handle(Message message)
        {
            if (message == null) return;
            if (message.type == "loading") { SetState(LobbyVoiceState.Loading, message.text); return; }
            if (message.type == "ready")
            {
                ready = true; SetState(LobbyVoiceState.Idle, "大厅助手");
                if (startWhenReady) { startWhenReady = false; BeginListening(); }
                return;
            }
            if (message.type == "fatal") { backend?.Dispose(); backend = null; ready = false; Fail(message.text); return; }
            if (message.turn != turn) return;
            switch (message.type)
            {
                case "partial": playerText.text = Tail(SafeText(message.text), 48); break;
                case "final":
                    StopMicrophone(); playerText.text = Tail(SafeText(message.text), 48); replySince = Time.unscaledTime;
                    SetState(LobbyVoiceState.Thinking, "小潮正在想…"); break;
                case "reply": assistantText.text = Tail(SafeText(message.text), 90); break;
                case "audio": EnqueueAudio(message); break;
                case "done": finished = true; break;
                case "empty": StopMicrophone(); SetState(LobbyVoiceState.Idle, message.text); break;
                case "error": StopMicrophone(); ClearPlayback(); finished = true; Fail(message.text); break;
            }
        }
        private static string SafeText(string value) => (value ?? "").Replace("<", "〈").Replace(">", "〉");
        private static string Tail(string value, int maximum) => value.Length <= maximum ? value : "…" + value.Substring(value.Length - maximum);
        private void EnqueueAudio(Message message)
        {
            if (message.sampleRate < 8000 || message.sampleRate > 48000 || string.IsNullOrEmpty(message.pcm)) return;
            byte[] bytes = Convert.FromBase64String(message.pcm);
            if (bytes.Length == 0 || bytes.Length % 2 != 0 || bytes.Length > message.sampleRate * 2 * 30) return;
            float[] data = new float[bytes.Length / 2];
            for (int i = 0; i < data.Length; i++) data[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
            AudioClip clip = AudioClip.Create("Local assistant speech", data.Length, 1, message.sampleRate, false);
            clip.SetData(data, 0); audioQueue.Enqueue(clip);
        }
        private void PumpPlayback()
        {
            if (!voiceSource.isPlaying && audioQueue.Count > 0)
            {
                if (voiceSource.clip != null) Destroy(voiceSource.clip);
                voiceSource.clip = audioQueue.Dequeue(); voiceSource.Play(); SetState(LobbyVoiceState.Speaking, "小潮正在说");
            }
            if (voiceSource.isPlaying) { voiceSource.GetOutputData(outputSamples, 0); waveform.SetSamples(outputSamples); }
            else if (finished && State == LobbyVoiceState.Speaking) { SetState(LobbyVoiceState.Idle, "大厅助手"); waveform.Clear(); }
        }
        private void Send(string type, string pcm = "", int sampleRate = 16000)
        {
            if (backend != null && !backend.Send(JsonUtility.ToJson(new Message { type = type, turn = turn, pcm = pcm, sampleRate = sampleRate })))
                Fail("本地助手处理繁忙，请按 T 重试");
        }
        private void SetState(LobbyVoiceState state, string text)
        {
            State = state; statusText.text = text;
            statusText.color = state == LobbyVoiceState.Error ? new Color32(237, 154, 116, 255) : BlackGoldTheme.Text;
            waveform.color = state == LobbyVoiceState.Listening ? PlayerColor : BlackGoldTheme.Gold;
            waveform.gameObject.SetActive(state == LobbyVoiceState.Listening || state == LobbyVoiceState.Speaking);
            actionText.text = state == LobbyVoiceState.Listening ? "结束" : state == LobbyVoiceState.Speaking || state == LobbyVoiceState.Thinking ? "打断" : "对话";
            if (state != LobbyVoiceState.Listening && state != LobbyVoiceState.Speaking) waveform.Clear();
        }
        private void Fail(string text)
        {
            ++turn; StopMicrophone(); ClearPlayback(); startWhenReady = false;
            backend?.Send(JsonUtility.ToJson(new Message { type = "cancel", turn = turn }));
            SetState(LobbyVoiceState.Error, text);
        }
        private void StopMicrophone()
        {
            if (microphone != null) { Microphone.End(null); Destroy(microphone); microphone = null; }
            waveform.Clear();
        }
        private void ClearPlayback()
        {
            voiceSource.Stop();
            if (voiceSource.clip != null) { Destroy(voiceSource.clip); voiceSource.clip = null; }
            while (audioQueue.Count > 0) Destroy(audioQueue.Dequeue());
            waveform.Clear();
        }
        private void CancelTurn()
        {
            ++turn; StopMicrophone(); ClearPlayback(); Send("cancel"); finished = false;
            if (ready) SetState(LobbyVoiceState.Idle, "大厅助手");
        }
        private void OnApplicationFocus(bool focused) { if (!focused && ready) CancelTurn(); }
        private void OnDisable()
        {
            if (!Application.isPlaying) return;
            Shutdown();
        }
        private void Shutdown()
        {
            ++turn; StopMicrophone(); ClearPlayback();
            backend?.Dispose(); backend = null; ready = false; startWhenReady = false;
            finished = false; hasTranscript = false; playerText.text = ""; assistantText.text = "";
            State = LobbyVoiceState.Idle;
        }
#if UNITY_EDITOR
        public void Preview(LobbyVoiceState state, string player, string assistant, float[] audio)
        {
            previewMode = true; backend?.Dispose(); backend = null;
            rect = (RectTransform)transform; hasTranscript = true;
            rect.sizeDelta = new Vector2(560, 164); conversation.SetActive(true);
            playerText.text = player; assistantText.text = assistant;
            SetState(state, state == LobbyVoiceState.Listening ? "正在听你说" : state == LobbyVoiceState.Speaking ? "小潮正在说" : "小潮正在想…");
            waveform.SetSamples(audio);
        }
#endif
    }
}
