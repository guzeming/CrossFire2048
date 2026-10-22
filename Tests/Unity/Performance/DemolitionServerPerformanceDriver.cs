#if BLACKTIDE_PERFORMANCE && BLACKTIDE_SERVER
using System;
using System.IO;
using UnityEngine;

[DefaultExecutionOrder(-250)]
public sealed class DemolitionServerPerformanceDriver : MonoBehaviour
{
    PerformanceCapture capture;
    string output;
    double started;
    bool saved;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("Server performance capture"); DontDestroyOnLoad(go); go.AddComponent<DemolitionServerPerformanceDriver>();
    }
    void Start() { output = PerformanceCapture.Argument("--perf-output"); started = Time.realtimeSinceStartupAsDouble; }
    void Update()
    {
        if (saved) return;
        // Delay until server types/markers have registered and startup costs have settled.
        if (Time.realtimeSinceStartupAsDouble - started < 12) return;
        if (capture == null) { capture = new PerformanceCapture(); return; }
        if (Time.frameCount % 30 == 0 && File.Exists(Path.Combine(output, "finish-server"))) { Save(); Application.Quit(0); return; }
        capture.Capture(new PerformanceCapture.Row { frame = Time.frameCount - 1, elapsed = Time.realtimeSinceStartupAsDouble - started,
            phase = 0, frameMs = Time.unscaledDeltaTime * 1000, focused = Application.isFocused });
    }
    void Save() { if (saved || capture == null) return; saved = true; capture.Save(output, "server", new[] { "match" }); }
    void OnApplicationQuit() { Save(); capture?.Dispose(); }
}
#endif
