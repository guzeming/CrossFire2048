using System;
using System.IO;
using System.Linq;
using System.Reflection;
using OperationBlacktide.Client.Editor;
using OperationBlacktide.Client.Features.Lobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class VerifyLobbyAssistant
{
    const string Active = "LobbyAssistant.Verify";
    static int stage;
    static double next, started;
    static LobbyVoiceAssistant assistant;
    static Canvas canvas;
    static Camera camera;
    static byte[] inputPcm;
    static int inputOffset;
    static bool heardReply;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static string Output => Path.GetFullPath("../../.build-check/lobby-assistant");

    static VerifyLobbyAssistant() { EditorApplication.update += Tick; }
    public static void Run()
    {
        try
        {
            Directory.CreateDirectory(Output);
            BuildLobbyAssistant.Build();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Active, true); EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Fail(error); }
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
        if (started == 0) started = EditorApplication.timeSinceStartup;
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            Check(EditorApplication.timeSinceStartup - started < 90, "UI test timed out");
            switch (stage++)
            {
                case 0:
                    camera = new GameObject("Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
                    camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .05f, .06f);
                    canvas = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                    canvas.GetComponent<CanvasScaler>().enabled = false;
                    new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                    // Use an actual earlier lobby capture as the backdrop for the prefab layout fixture.
                    string backdropPath = Path.GetFullPath("../../.build-check/black-gold-ui/03-agent-selection.png");
                    if (File.Exists(backdropPath))
                    {
                        var texture = new Texture2D(2, 2); texture.LoadImage(File.ReadAllBytes(backdropPath));
                        var image = new GameObject("LobbyBackdrop", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                        image.transform.SetParent(canvas.transform, false); image.texture = texture;
                        image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one; image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
                    }
                    var container = new GameObject("LobbyFixture", typeof(RectTransform)); container.SetActive(false); container.transform.SetParent(canvas.transform, false);
                    var containerRect = (RectTransform)container.transform; containerRect.anchorMin = Vector2.zero; containerRect.anchorMax = Vector2.one; containerRect.offsetMin = containerRect.offsetMax = Vector2.zero;
                    var panel = container.AddComponent<LobbyPanel>();
                    LobbyVoiceAssistant.Attach(panel); LobbyVoiceAssistant.Attach(panel);
                    Check(container.GetComponentsInChildren<LobbyVoiceAssistant>(true).Length == 1, "Assistant duplicated on reopening");
                    assistant = container.GetComponentInChildren<LobbyVoiceAssistant>(true);
                    assistant.Preview(LobbyVoiceState.Listening, "你好，给我介绍一下训练场", "", Audio());
                    container.SetActive(true);
                    break;
                case 1:
                    assistant.Preview(LobbyVoiceState.Listening, "你好，给我介绍一下训练场", "", Audio());
                    break;
                case 2:
                    Capture("listening", 1920, 1080);
                    assistant.Preview(LobbyVoiceState.Speaking, "你好，给我介绍一下训练场", "你可以从大厅右下角选择训练场，练习移动、瞄准和射击。", Audio());
                    break;
                case 3:
                    Capture("speaking", 1920, 1080); Capture("speaking-16x10", 1280, 800); Capture("speaking-ultrawide", 2560, 1080);
                    Check(assistant.GetComponentsInChildren<Text>().All(t => t.font != null && !t.supportRichText), "Missing font or unsafe transcript markup");
                    typeof(LobbyVoiceAssistant).GetField("turn", Private).SetValue(assistant, 10);
                    Deliver("{\"type\":\"partial\",\"turn\":9,\"text\":\"旧的回复\"}");
                    Check(!assistant.PlayerTranscript.Contains("旧的"), "Stale turn overwrote current transcript");
                    Deliver("{\"type\":\"partial\",\"turn\":10,\"text\":\"测试<color=red>\"}");
                    Check(assistant.PlayerTranscript == "测试〈color=red〉", "Transcript escaping failed");
                    Deliver("{\"type\":\"final\",\"turn\":10,\"text\":\"你好\"}");
                    Check(assistant.State == LobbyVoiceState.Thinking, "Final ASR did not enter thinking state");
                    Deliver("{\"type\":\"error\",\"turn\":10,\"text\":\"测试错误\"}");
                    Check(assistant.State == LobbyVoiceState.Error, "Error state missing");
                    assistant.GetComponentInChildren<LobbyVoiceWaveform>(true).Clear();
                    break;
                case 4:
                    Check(assistant.GetComponentInChildren<LobbyVoiceWaveform>(true).Level == 0, "Silence continued showing speech");
                    assistant.gameObject.SetActive(false);
                    Check(!Microphone.IsRecording(null), "Microphone leaked after close");
                    assistant = Object.Instantiate(Resources.Load<GameObject>("UI/LobbyAssistant"), canvas.transform, false).GetComponent<LobbyVoiceAssistant>();
                    break;
                case 5:
                    Check(assistant.State != LobbyVoiceState.Error, "Local backend failed to start in Unity");
                    if (assistant.State != LobbyVoiceState.Idle) { stage--; break; }
                    typeof(LobbyVoiceAssistant).GetField("turn", Private).SetValue(assistant, 20);
                    using (var wav = new BinaryReader(File.OpenRead(Path.Combine(Output, "input.wav"))))
                    {
                        wav.ReadBytes(12);
                        while (wav.BaseStream.Position + 8 <= wav.BaseStream.Length)
                        {
                            string tag = new string(wav.ReadChars(4)); int length = wav.ReadInt32(); byte[] data = wav.ReadBytes(length);
                            if (tag == "data") { inputPcm = data; break; }
                        }
                    }
                    Check(inputPcm != null && inputPcm.Length > 0, "Run offline backend integration before Unity verification");
                    SendBackend("{\"type\":\"begin\",\"turn\":20}");
                    typeof(LobbyVoiceAssistant).GetField("hasTranscript", Private).SetValue(assistant, true);
                    break;
                case 6:
                    int count = Math.Min(3200, inputPcm.Length - inputOffset);
                    if (count > 0)
                    {
                        SendBackend("{\"type\":\"audio\",\"turn\":20,\"sampleRate\":16000,\"pcm\":\"" + Convert.ToBase64String(inputPcm, inputOffset, count) + "\"}");
                        inputOffset += count; stage--;
                    }
                    else SendBackend("{\"type\":\"end\",\"turn\":20}");
                    break;
                case 7:
                    Check(assistant.State != LobbyVoiceState.Error, "Unity offline conversation failed");
                    var source = assistant.GetComponent<AudioSource>();
                    if (source.isPlaying && source.clip != null && !heardReply && assistant.GetComponentInChildren<LobbyVoiceWaveform>(true).Level > .005f)
                    {
                        float[] samples = new float[Math.Min(24000, source.clip.samples)]; source.clip.GetData(samples, 0);
                        Check(samples.Any(value => Math.Abs(value) > .001), "Silent assistant playback");
                        Check(assistant.PlayerTranscript.Contains("你好"), "Unity transcription encoding failed: " + assistant.PlayerTranscript);
                        Check(assistant.AssistantTranscript.Length > 3, "Unity reply transcript missing");
                        Capture("offline-conversation", 1920, 1080); heardReply = true;
                    }
                    if (!heardReply || assistant.State != LobbyVoiceState.Idle) { stage--; break; }
                    assistant.gameObject.SetActive(false);
                    Check(typeof(LobbyVoiceAssistant).GetField("backend", Private).GetValue(assistant) == null, "Backend survived lobby close");
                    File.WriteAllText(Path.Combine(Output, "ui-verification.txt"), "LOBBY_ASSISTANT_UI_PASS: prefab wiring, duplicate attachment, live mesh, 16:9 / 16:10 / ultrawide bounds, transcript escaping, stale-turn rejection, states, close cleanup, real subprocess ASR -> model -> Unity subtitles and non-silent AudioSource playback. Screenshots are layout fixtures over an earlier lobby capture; microphone input uses a prerecorded fixture.");
                    Debug.Log("LOBBY_ASSISTANT_UI_PASS"); SessionState.SetBool(Active, false); EditorApplication.Exit(0); return;
            }
            next = EditorApplication.timeSinceStartup + .1;
        }
        catch (Exception error) { Fail(error); }
    }
    static void Deliver(string json)
    {
        Type message = typeof(LobbyVoiceAssistant).GetNestedType("Message", BindingFlags.NonPublic);
        typeof(LobbyVoiceAssistant).GetMethod("Handle", Private).Invoke(assistant, new[] { JsonUtility.FromJson(json, message) });
    }
    static void SendBackend(string json)
    {
        object backend = typeof(LobbyVoiceAssistant).GetField("backend", Private).GetValue(assistant);
        Check((bool)backend.GetType().GetMethod("Send").Invoke(backend, new object[] { json }), "Backend queue rejected test audio");
    }
    static float[] Audio() => Enumerable.Range(0, 512).Select(i => Mathf.Sin(i * .13f) * (.03f + .12f * Mathf.Abs(Mathf.Sin(i * .021f)))).ToArray();
    static void Capture(string name, int width, int height)
    {
        var target = new RenderTexture(width, height, 24);
        var before = RenderTexture.active;
        canvas.scaleFactor = Mathf.Sqrt(width / 1920f * height / 1080f);
        camera.targetTexture = target; camera.aspect = width / (float)height;
        Canvas.ForceUpdateCanvases();
        var corners = new Vector3[4]; ((RectTransform)assistant.transform).GetWorldCorners(corners);
        foreach (var corner in corners)
        {
            var screen = camera.WorldToViewportPoint(corner);
            Check(screen.x >= 0 && screen.x <= 1 && screen.y >= 0 && screen.y <= 1, "Assistant outside screen at " + width);
        }
        camera.Render(); RenderTexture.active = target;
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false); pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
        File.WriteAllBytes(Path.Combine(Output, name + ".png"), pixels.EncodeToPNG());
        Object.DestroyImmediate(pixels); camera.targetTexture = null; RenderTexture.active = before; Object.DestroyImmediate(target);
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Fail(Exception error)
    {
        SessionState.SetBool(Active, false); Debug.LogException(error);
        Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output, "ui-failure.txt"), error.ToString()); EditorApplication.Exit(1);
    }
}
