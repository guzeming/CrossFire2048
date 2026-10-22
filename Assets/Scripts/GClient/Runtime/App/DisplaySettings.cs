using System;
using System.Collections.Generic;
using UnityEngine;

namespace OperationBlacktide.Client.App
{
    /// <summary>Display preferences belong to this device, independently of the signed-in account.</summary>
    public static class DisplaySettings
    {
        public const string PreferenceKey = "OperationBlacktide.Display.v1";

        [Serializable]
        public struct Value : IEquatable<Value>
        {
            public int width, height;
            public bool fullscreen;
            // Missing in older saves, so false deliberately means enabled by default.
            public bool performanceStatsDisabled;
            public bool ShowPerformanceStats { get => !performanceStatsDisabled; set => performanceStatsDisabled = !value; }
            public Value(int width, int height, bool fullscreen, bool showPerformanceStats = true)
            { this.width = width; this.height = height; this.fullscreen = fullscreen; performanceStatsDisabled = !showPerformanceStats; }
            public bool Equals(Value other) => width == other.width && height == other.height && fullscreen == other.fullscreen
                && performanceStatsDisabled == other.performanceStatsDisabled;
        }

        public static bool PerformanceStatsEnabled { get; private set; } = true;
        public static event Action<bool> PerformanceStatsChanged;
        public static Value Current => new Value(Screen.width, Screen.height, Screen.fullScreen, PerformanceStatsEnabled);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            PerformanceStatsEnabled = true;
            PerformanceStatsChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Restore()
        {
            bool loaded = TryLoad(out var saved);
            SetPerformanceStats(!loaded || saved.ShowPerformanceStats);
            if (Application.isEditor || Application.isBatchMode || !loaded) return;
            var desktop = Screen.currentResolution;
            if (desktop.width > 0 && desktop.height > 0)
            {
                saved.width = Mathf.Min(saved.width, desktop.width);
                saved.height = Mathf.Min(saved.height, desktop.height);
            }
            Apply(saved);
        }

        public static bool TryLoad(out Value saved)
        {
            saved = default;
            if (!PlayerPrefs.HasKey(PreferenceKey)) return false;
            try { saved = JsonUtility.FromJson<Value>(PlayerPrefs.GetString(PreferenceKey)); }
            catch (ArgumentException) { return false; }
            return saved.width >= 320 && saved.height >= 240 && saved.width <= 16384 && saved.height <= 16384;
        }

        public static void Save(Value value)
        {
            if (value.width <= 0 || value.height <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            PlayerPrefs.SetString(PreferenceKey, JsonUtility.ToJson(value));
            PlayerPrefs.Save();
            Apply(value);
        }

        private static void Apply(Value value)
        {
            SetPerformanceStats(value.ShowPerformanceStats);
            // Borderless fullscreen permits every listed render resolution without changing the desktop mode.
            if (value.width != Screen.width || value.height != Screen.height || value.fullscreen != Screen.fullScreen)
                Screen.SetResolution(value.width, value.height,
                    value.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        }

        private static void SetPerformanceStats(bool enabled)
        {
            if (PerformanceStatsEnabled == enabled) return;
            PerformanceStatsEnabled = enabled;
            PerformanceStatsChanged?.Invoke(enabled);
        }

        public static List<Vector2Int> Resolutions(Value current)
        {
            var sizes = new List<Vector2Int>();
            var desktop = Screen.currentResolution;
            int maxWidth = Mathf.Max(desktop.width, current.width);
            int maxHeight = Mathf.Max(desktop.height, current.height);
            void Add(int width, int height)
            {
                var size = new Vector2Int(width, height);
                if (width >= 640 && height >= 480 && width <= maxWidth && height <= maxHeight && !sizes.Contains(size)) sizes.Add(size);
            }
            foreach (var size in Screen.resolutions) Add(size.width, size.height);
            foreach (var size in new[] { new Vector2Int(1024, 768), new Vector2Int(1280, 720), new Vector2Int(1280, 800),
                new Vector2Int(1366, 768), new Vector2Int(1600, 900), new Vector2Int(1920, 1080), new Vector2Int(1920, 1200),
                new Vector2Int(2560, 1440), new Vector2Int(2560, 1600), new Vector2Int(3440, 1440), new Vector2Int(3840, 2160) })
                Add(size.x, size.y);
            Add(desktop.width, desktop.height);
            var live = new Vector2Int(current.width, current.height);
            if (live.x > 0 && live.y > 0 && !sizes.Contains(live)) sizes.Add(live);
            sizes.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            return sizes;
        }
    }
}
