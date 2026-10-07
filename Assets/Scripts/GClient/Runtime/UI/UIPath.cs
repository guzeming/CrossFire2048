using System.Collections.Generic;
using UnityEngine;

namespace OperationBlacktide.Client.UI
{
    /// <summary>
    /// UI 面板名称与 Resources 路径映射表。
    /// 新增界面请用菜单 OperationBlacktide/UI/Add UI 写入下方字典。
    /// </summary>
    public static class UIPath
    {
        public const string MapBeginMarker = "// BEGIN UI PATH MAP";
        public const string MapEndMarker = "// END UI PATH MAP";

        /// <summary>界面名 → Resources 路径（不含 Assets/Resources 前缀与 .prefab 后缀）。</summary>
        public static IReadOnlyDictionary<string, string> Paths => PathMap;

        private static readonly Dictionary<string, string> PathMap = new Dictionary<string, string>
        {
            // BEGIN UI PATH MAP
            { "Login", "UI/Panels/LoginPanel" },
            { "Lobby", "UI/Panels/LobbyPanel" },
            { "Toast", "UI/Panels/ToastPanel" },
            { "Training", "UI/Panels/TrainingPanel" },
            { "ReturnToLobby", "UI/Panels/ReturnToLobbyPanel" },
            { "TrainingMenu", "UI/Panels/TrainingMenuPanel" },
            // END UI PATH MAP
        };

        public static bool TryGetPath(UIPanelId panelId, out string resourcePath)
        {
            return TryGetPath(PanelIds.Key(panelId), out resourcePath);
        }

        public static bool TryGetPath(string panelName, out string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(panelName))
            {
                resourcePath = string.Empty;
                return false;
            }

            return PathMap.TryGetValue(panelName, out resourcePath);
        }

        public static UIPanel LoadPanel(UIPanelId panelId)
        {
            return LoadPanel(PanelIds.Key(panelId));
        }

        public static UIPanel LoadPanel(string panelName)
        {
            if (!TryGetPath(panelName, out string resourcePath) || string.IsNullOrWhiteSpace(resourcePath))
            {
                return null;
            }

            UIPanel panel = Resources.Load<UIPanel>(resourcePath);
            if (panel == null)
            {
                Debug.LogError($"[UIPath] Resources.Load 失败：{panelName} -> {resourcePath}");
            }

            return panel;
        }
    }
}
