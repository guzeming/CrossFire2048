using System;
using UnityEngine;

namespace CrossFire2048.Client.UI
{
    /// <summary>
    /// Inspector 中注册的面板预制体条目。
    /// </summary>
    [Serializable]
    public sealed class UIPanelEntry
    {
        [SerializeField] private string panelId;
        [SerializeField] private UIPanel prefab;
        [SerializeField] private UILayer layer = UILayer.Normal;

        public string PanelId => panelId;
        public UIPanel Prefab => prefab;
        public UILayer Layer => layer;

        public bool IsValid => prefab != null && !string.IsNullOrWhiteSpace(panelId);
    }
}
