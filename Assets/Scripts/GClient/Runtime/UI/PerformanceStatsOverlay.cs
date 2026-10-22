using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using OperationBlacktide.Client.App;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace OperationBlacktide.Client.UI
{
    /// <summary>Device-local telemetry, independent of the panel stack and scene lifetime.</summary>
    public sealed class PerformanceStatsOverlay : MonoBehaviour
    {
        [SerializeField] private GameObject content;
        [SerializeField] private Text statisticsText, deviceText;
        private const double RefreshInterval = .25;
        private const double Megabyte = 1024 * 1024;
        private readonly FrameTiming[] timings = new FrameTiming[1];
        private readonly StringBuilder text = new StringBuilder(256);
        private Process process;
        private double windowStart, processSampleTime, previousCpuSeconds, cpuMilliseconds, gpuMilliseconds;
        private int frames, cpuSamples, gpuSamples;
        private ulong lastTimingTimestamp;
        private bool frameTimingAvailable;
        public bool IsVisible => content != null && content.activeSelf;

        private void OnEnable()
        {
            DisplaySettings.PerformanceStatsChanged += SetVisible;
            frameTimingAvailable = FrameTimingManager.IsFeatureEnabled();
            deviceText.text = $"{SystemInfo.graphicsDeviceName}  |  显存容量：{(SystemInfo.graphicsMemorySize > 0 ? SystemInfo.graphicsMemorySize + " MB" : "不可用")}";
            SetVisible(DisplaySettings.PerformanceStatsEnabled);
        }

        private void OnDisable()
        {
            DisplaySettings.PerformanceStatsChanged -= SetVisible;
            ReleaseProcess();
        }

        private void SetVisible(bool visible)
        {
            content.SetActive(visible);
            ReleaseProcess();
            if (!visible) return;
            windowStart = processSampleTime = Time.realtimeSinceStartupAsDouble;
            frames = cpuSamples = gpuSamples = 0;
            cpuMilliseconds = gpuMilliseconds = 0;
            lastTimingTimestamp = 0;
            try
            {
                process = Process.GetCurrentProcess();
                previousCpuSeconds = process.TotalProcessorTime.TotalSeconds;
            }
            catch (Exception exception) when (IsProcessMetricUnavailable(exception)) { ReleaseProcess(); }
            statisticsText.text = "FPS：采样中\nCPU 进程：采样中\n内存：采样中\nGPU 帧：采样中";
        }

        private void Update()
        {
            if (!IsVisible) return;
            frames++;
            if (frameTimingAvailable)
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].frameStartTimestamp != lastTimingTimestamp)
                {
                    lastTimingTimestamp = timings[0].frameStartTimestamp;
                    if (Valid(timings[0].cpuFrameTime)) { cpuMilliseconds += timings[0].cpuFrameTime; cpuSamples++; }
                    if (Valid(timings[0].gpuFrameTime)) { gpuMilliseconds += timings[0].gpuFrameTime; gpuSamples++; }
                }
            }
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = now - windowStart;
            if (elapsed < RefreshInterval) return;
            RefreshText(now, elapsed);
            windowStart = now;
            frames = cpuSamples = gpuSamples = 0;
            cpuMilliseconds = gpuMilliseconds = 0;
        }

        private void RefreshText(double now, double elapsed)
        {
            double cpuPercent = -1, memory = ReadResidentMemory();
            if (process != null)
            {
                try
                {
                    process.Refresh();
                    double cpuSeconds = process.TotalProcessorTime.TotalSeconds;
                    cpuPercent = Math.Max(0, Math.Min(100, (cpuSeconds - previousCpuSeconds) * 100 /
                        ((now - processSampleTime) * Math.Max(1, SystemInfo.processorCount))));
                    previousCpuSeconds = cpuSeconds;
                    processSampleTime = now;
                }
                catch (Exception exception) when (IsProcessMetricUnavailable(exception)) { ReleaseProcess(); }
            }

            text.Clear();
            text.AppendFormat("FPS：{0:F1}  |  帧：{1:F2} ms\nCPU 进程：", frames / elapsed, elapsed * 1000 / frames);
            Metric(cpuPercent, "F1", "%");
            text.Append("  |  CPU 帧：");
            Metric(cpuSamples > 0 ? cpuMilliseconds / cpuSamples : -1, "F2", " ms");
            text.Append("\n内存："); Metric(memory, "F0", " MB");
            text.AppendFormat("  |  GC：{0:F0} MB\nGPU 帧：", GC.GetTotalMemory(false) / Megabyte);
            Metric(gpuSamples > 0 ? gpuMilliseconds / gpuSamples : -1, "F2", " ms");
            // Unity exposes driver allocations in development players/editor only; this is not total VRAM usage.
            long graphicsBytes = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            graphicsBytes = Profiler.GetAllocatedMemoryForGraphicsDriver();
#endif
            text.Append("  |  图形分配："); Metric(graphicsBytes > 0 ? graphicsBytes / Megabyte : -1, "F0", " MB");
            statisticsText.text = text.ToString();
        }

        private void Metric(double value, string format, string unit)
        {
            if (value < 0 || double.IsNaN(value) || double.IsInfinity(value)) text.Append("不可用");
            else text.Append(value.ToString(format)).Append(unit);
        }

        private static bool Valid(double milliseconds) => milliseconds > 0 && !double.IsNaN(milliseconds) && !double.IsInfinity(milliseconds);
        private double ReadResidentMemory()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // Unity's Mono Process.WorkingSet64 can return zero on Windows. Read the OS working set directly.
            if (GetProcessMemoryInfo(new IntPtr(-1), out var counters, (uint)Marshal.SizeOf<ProcessMemoryCounters>()))
            {
                ulong bytes = counters.workingSetSize.ToUInt64();
                return bytes > 0 ? bytes / Megabyte : -1;
            }
            return -1;
#else
            try { long bytes = process?.WorkingSet64 ?? 0; return bytes > 0 ? bytes / Megabyte : -1; }
            catch (Exception exception) when (IsProcessMetricUnavailable(exception)) { return -1; }
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCounters
        {
            public uint size, pageFaultCount;
            public UIntPtr peakWorkingSetSize, workingSetSize, quotaPeakPagedPoolUsage, quotaPagedPoolUsage;
            public UIntPtr quotaPeakNonPagedPoolUsage, quotaNonPagedPoolUsage, pagefileUsage, peakPagefileUsage;
        }
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessMemoryInfo(IntPtr processHandle, out ProcessMemoryCounters counters, uint size);
#endif

        private static bool IsProcessMetricUnavailable(Exception exception) => exception is NotSupportedException
            || exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception;
        private void ReleaseProcess() { process?.Dispose(); process = null; }
    }
}
