using System;
using UnityEngine;

namespace OperationBlacktide.BattleServer
{
    /// <summary>Scene-owned entry point, retained even when player assembly constraints differ from the Editor.</summary>
    [DisallowMultipleComponent]
    public sealed class BattleServerEntry : MonoBehaviour
    {
        private void Start()
        {
            string manifest = BattleServerHost.Argument(Environment.GetCommandLineArgs(), "--demolition-manifest", "");
            if (manifest.Length > 0) DemolitionServerHost.Bootstrap();
            else BattleServerHost.Bootstrap();
        }
    }
}
