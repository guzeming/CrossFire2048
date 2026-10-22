#if BLACKTIDE_PERFORMANCE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

// This file is copied into an isolated build. Never installed in the shipping Assets tree.
public sealed class PerformanceCapture : IDisposable
{
    public static readonly string[] Columns = {
        "main_thread_ms", "render_thread_ms", "gc_alloc_bytes", "draw_calls", "triangles", "used_memory_bytes",
        "vision_ms", "cutaway_ms", "snapshot_ms", "prediction_ms", "effects_ms", "decals_ms", "server_tick_ms",
        "gc_collect_ms", "present_wait_ms", "present_ms", "shader_create_ms"
    };
    static readonly string[] Names = {
        "Main Thread", "CPU Render Thread Frame Time", "GC Allocated In Frame", "Draw Calls Count", "Triangles Count", "Total Used Memory",
        "Blacktide.Vision.Mask", "Blacktide.Camera.Obstructions", "Blacktide.Client.Snapshot", "Blacktide.Client.Predict",
        "Blacktide.Effects.Update", "Blacktide.Decals.Update", "Blacktide.Server.Tick",
        "GC.Collect", "Gfx.WaitForPresentOnGfxThread", "Gfx.PresentFrame", "Shader.CreateGPUProgram"
    };
    const int MaxFrames = 120000;
    readonly ProfilerRecorder[] recorders = new ProfilerRecorder[Names.Length];
    readonly List<Row> rows = new List<Row>(MaxFrames);
    readonly double[] values = new double[MaxFrames * Names.Length];
    readonly FrameTiming[] timings = new FrameTiming[1];
    ulong lastTiming;
    readonly bool frameTimings;
    public struct Row
    {
        public int frame, phase, round, alive, actors, shots, snapshotTick;
        public double elapsed, frameMs, cpuMs, gpuMs;
        public float x, y, z, cameraYaw;
        public bool focused;
    }
    [Serializable] public sealed class Distribution
    {
        public string metric;
        public int samples;
        public double mean, p50, p95, p99, max;
    }
    [Serializable] public sealed class Summary
    {
        public string phase;
        public int frames, over16ms, over33ms, over50ms, gpuSamples;
        public double seconds, fps, aliveFraction;
        public Distribution[] metrics;
    }
    public PerformanceCapture()
    {
        frameTimings = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null && FrameTimingManager.IsFeatureEnabled();
        for (int i = 0; i < Names.Length; i++)
        {
            var category = i == 0 ? ProfilerCategory.Internal : i == 1 ? ProfilerCategory.Render : i == 2 || i == 5 ? ProfilerCategory.Memory :
                i < 6 ? ProfilerCategory.Render : i < 13 ? ProfilerCategory.Scripts : i == 13 ? ProfilerCategory.Memory : ProfilerCategory.Render;
            recorders[i] = ProfilerRecorder.StartNew(category, Names[i], 1,
                ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately);
        }
    }
    public void Capture(Row row)
    {
        if (rows.Count >= MaxFrames) throw new InvalidOperationException("Performance capture exceeded its preallocated frame budget.");
        row.cpuMs = row.gpuMs = -1;
        if (frameTimings)
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].frameStartTimestamp != lastTiming)
            {
                lastTiming = timings[0].frameStartTimestamp;
                row.cpuMs = timings[0].cpuFrameTime > 0 ? timings[0].cpuFrameTime : -1;
                row.gpuMs = timings[0].gpuFrameTime > 0 ? timings[0].gpuFrameTime : -1;
            }
        }
        int offset = rows.Count * Names.Length;
        for (int i = 0; i < recorders.Length; i++)
        {
            var recorder = recorders[i];
            double value = !recorder.Valid ? -1 : recorder.Count > 0 ? recorder.LastValue : 0;
            values[offset + i] = value >= 0 && (i < 2 || i >= 6) ? value / 1000000.0 : value;
            // Reset + Start avoids repeating a stale marker sample on frames where it did not execute.
            if (i >= 6 && recorder.Valid) { recorder.Reset(); recorder.Start(); }
        }
        rows.Add(row);
    }
    public void Save(string folder, string prefix, string[] phases)
    {
        Directory.CreateDirectory(folder);
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        File.WriteAllLines(Path.Combine(folder, prefix + "-available-markers.txt"), handles.Select(h => {
            var d = ProfilerRecorderHandle.GetDescription(h); return d.Category.Name + "\t" + d.Name + "\t" + d.UnitType;
        }).OrderBy(s => s));
        using (var writer = new StreamWriter(Path.Combine(folder, prefix + "-frames.csv")))
        {
            writer.WriteLine("frame,elapsed,phase,frame_ms,cpu_frame_ms,gpu_frame_ms,focused,round,alive,actors,shots,snapshot_tick,x,y,z,camera_yaw," + string.Join(",", Columns));
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                writer.Write(FormattableString.Invariant($"{row.frame},{row.elapsed:F6},{phases[row.phase]},{row.frameMs:F6},{row.cpuMs:F6},{row.gpuMs:F6},{(row.focused ? 1 : 0)},{row.round},{row.alive},{row.actors},{row.shots},{row.snapshotTick},{row.x:F4},{row.y:F4},{row.z:F4},{row.cameraYaw:F4}"));
                for (int i = 0; i < Names.Length; i++) writer.Write("," + values[r * Names.Length + i].ToString("F6", CultureInfo.InvariantCulture));
                writer.WriteLine();
            }
        }
        var summaries = new List<Summary>();
        for (int phase = 0; phase < phases.Length; phase++)
        {
            int p = phase;
            int[] indices = Enumerable.Range(0, rows.Count).Where(i => rows[i].phase == p).ToArray();
            if (indices.Length == 0) continue;
            var times = indices.Select(i => rows[i].frameMs).ToArray();
            var metrics = new List<Distribution> { Describe("frame_ms", times),
                Describe("cpu_frame_ms", indices.Select(i => rows[i].cpuMs)), Describe("gpu_frame_ms", indices.Select(i => rows[i].gpuMs)) };
            for (int c = 0; c < Names.Length; c++)
            {
                int column = c;
                metrics.Add(Describe(Columns[c], indices.Select(i => values[i * Names.Length + column])));
            }
            double seconds = times.Sum() / 1000;
            summaries.Add(new Summary { phase = phases[phase], frames = times.Length, seconds = seconds,
                fps = times.Length / seconds, over16ms = times.Count(t => t > 1000.0 / 60), over33ms = times.Count(t => t > 1000.0 / 30),
                over50ms = times.Count(t => t > 50), aliveFraction = indices.Average(i => rows[i].alive),
                gpuSamples = indices.Count(i => rows[i].gpuMs >= 0), metrics = metrics.ToArray() });
        }
        File.WriteAllText(Path.Combine(folder, prefix + "-summary.json"), JsonUtility.ToJson(new Report {
            unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
            cpu = SystemInfo.processorType, logicalProcessors = SystemInfo.processorCount, width = Screen.width, height = Screen.height,
            quality = QualitySettings.names[QualitySettings.GetQualityLevel()], vSync = QualitySettings.vSyncCount,
            targetFrameRate = Application.targetFrameRate, developmentBuild = Debug.isDebugBuild,
            frameTimingEnabled = frameTimings, recorderAvailable = recorders.Select(r => r.Valid).ToArray(),
            recorderNames = Names, summaries = summaries.ToArray(),
            notes = "Warmup, phase changes, screenshots and final serialization are excluded. Missing values are -1. FrameTiming samples may lag several frames and are not aligned to marker rows. Main/render-thread timings include waits; marker scopes are inclusive and must not be summed as total CPU time. Server shares this computer."
        }, true));
    }
    [Serializable] sealed class Report
    {
        public string unity, gpu, graphicsApi, cpu, quality, notes;
        public int logicalProcessors, width, height, vSync, targetFrameRate;
        public bool developmentBuild, frameTimingEnabled;
        public bool[] recorderAvailable;
        public string[] recorderNames;
        public Summary[] summaries;
    }
    public static Distribution Describe(string name, IEnumerable<double> input)
    {
        double[] data = input.Where(v => v >= 0 && !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToArray();
        if (data.Length == 0) return new Distribution { metric = name, mean = -1, p50 = -1, p95 = -1, p99 = -1, max = -1 };
        double Percentile(double p) => data[Math.Max(0, (int)Math.Ceiling(p * data.Length) - 1)];
        return new Distribution { metric = name, samples = data.Length, mean = data.Average(), p50 = Percentile(.5), p95 = Percentile(.95), p99 = Percentile(.99), max = data[data.Length - 1] };
    }
    public void Dispose() { for (int i = 0; i < recorders.Length; i++) recorders[i].Dispose(); }
    public static string Argument(string name, string fallback = "")
    {
        string[] args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }
}
#endif
