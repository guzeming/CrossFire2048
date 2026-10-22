using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Combat
{
    public enum HitBodyPart { Body, Head, Limb }

    /// <summary>Explicit collider region; independent of names, skins and damage multipliers.</summary>
    [DisallowMultipleComponent]
    public sealed class HitRegion : MonoBehaviour
    {
        [SerializeField] private HitBodyPart part;
        public HitBodyPart Part { get => part; set => part = value; }
    }
}
