using System;
using UnityEngine;

namespace OperationBlacktide.Client.Features.Lobby
{
    public static class LocalAssistantSettings
    {
        public const string PreferenceKey = "OperationBlacktide.LocalAssistant.Enabled.v1";
        public static bool Enabled => PlayerPrefs.GetInt(PreferenceKey, 0) == 1;
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Changed = null; }

        public static void Save(bool enabled)
        {
            bool changed = Enabled != enabled;
            PlayerPrefs.SetInt(PreferenceKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
            if (changed) Changed?.Invoke();
        }
    }
}
