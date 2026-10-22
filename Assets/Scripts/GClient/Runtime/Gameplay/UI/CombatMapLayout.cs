using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.UI
{
    /// <summary>Baked from the same world-space navigation geometry as the battle map.</summary>
    public sealed class CombatMapLayout : ScriptableObject
    {
        public Texture2D texture;
        public Rect worldBounds;

        public Vector2 Project(Vector3 position)
        {
            return new Vector2(Mathf.InverseLerp(worldBounds.xMin, worldBounds.xMax, position.x),
                Mathf.InverseLerp(worldBounds.yMin, worldBounds.yMax, position.z));
        }
    }
}
