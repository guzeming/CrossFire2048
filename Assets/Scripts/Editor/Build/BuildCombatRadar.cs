using System;
using System.IO;
using OperationBlacktide.Client.Gameplay.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

namespace OperationBlacktide.EditorTools
{
    public static class BuildCombatRadar
    {
        public const string LayoutPath = "Assets/Resources/Gameplay/DustIIRadar.asset";
        const string TexturePath = "Assets/Resources/Gameplay/DustIIRadar.png";
        const int Size = 768;

        [MenuItem("OperationBlacktide/Combat/Build tactical radar")]
        public static void Build()
        {
            var surfaces = UnityEngine.Object.FindObjectsOfType<NavMeshSurface>();
            foreach (var surface in surfaces) surface.RemoveData();
            Texture2D texture = null;
            try
            {
                var data = AssetDatabase.LoadAssetAtPath<NavMeshData>(BuildBattleNavigation.AssetPath);
                if (data == null) throw new InvalidOperationException("Bake the Dust II battle navigation first.");
                if (NavMesh.CalculateTriangulation().vertices.Length != 0)
                    throw new InvalidOperationException("Close scenes containing legacy navigation data before baking the radar.");
                var instance = NavMesh.AddNavMeshData(data);
                NavMeshTriangulation geometry;
                try { geometry = NavMesh.CalculateTriangulation(); }
                finally { instance.Remove(); }
                if (geometry.vertices.Length == 0) throw new InvalidOperationException("Dust II radar has no walkable geometry.");
                var bounds = new Bounds(geometry.vertices[0], Vector3.zero);
                foreach (var vertex in geometry.vertices) bounds.Encapsulate(vertex);
                float extent = Mathf.Max(bounds.size.x, bounds.size.z) + 6;
                var world = new Rect(bounds.center.x - extent / 2, bounds.center.z - extent / 2, extent, extent);
                var floor = new bool[Size * Size];
                var shades = new byte[Size * Size];
                var uv = new Vector2[geometry.vertices.Length];
                for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2((geometry.vertices[i].x - world.x) / extent * (Size - 1), (geometry.vertices[i].z - world.y) / extent * (Size - 1));
                for (int i = 0; i < geometry.indices.Length; i += 3)
                {
                    int ia = geometry.indices[i], ib = geometry.indices[i + 1], ic = geometry.indices[i + 2];
                    var a = uv[ia]; var b = uv[ib]; var c = uv[ic];
                    float area = Cross(b - a, c - a); if (Mathf.Abs(area) < .0001f) continue;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x)), 0, Size - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x, c.x)), 0, Size - 1);
                    int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y, c.y)), 0, Size - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y, c.y)), 0, Size - 1);
                    byte shade = (byte)Mathf.RoundToInt(58 + 28 * Mathf.InverseLerp(bounds.min.y, bounds.max.y, (geometry.vertices[ia].y + geometry.vertices[ib].y + geometry.vertices[ic].y) / 3));
                    for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                    {
                        var p = new Vector2(x + .5f, y + .5f);
                        if (Cross(b - a, p - a) / area < -.001f || Cross(c - b, p - b) / area < -.001f || Cross(a - c, p - c) / area < -.001f) continue;
                        int n = y * Size + x; floor[n] = true; shades[n] = Math.Max(shades[n], shade);
                    }
                }
                var pixels = new Color32[Size * Size];
                for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                {
                    int n = y * Size + x;
                    bool edge = floor[n] && (x == 0 || y == 0 || x == Size - 1 || y == Size - 1 || !floor[n - 1] || !floor[n + 1] || !floor[n - Size] || !floor[n + Size]);
                    byte s = shades[n];
                    pixels[n] = edge ? new Color32(134, 153, 161, 255) : floor[n] ? new Color32(s, (byte)(s + 12), (byte)(s + 19), 255) : new Color32(14, 22, 29, 255);
                }
                texture = new Texture2D(Size, Size, TextureFormat.RGB24, false); texture.SetPixels32(pixels); texture.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(TexturePath)); File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
                importer.textureType = TextureImporterType.Default; importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false; importer.wrapMode = TextureWrapMode.Clamp; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
                var layout = AssetDatabase.LoadAssetAtPath<CombatMapLayout>(LayoutPath);
                if (layout == null) { layout = ScriptableObject.CreateInstance<CombatMapLayout>(); AssetDatabase.CreateAsset(layout, LayoutPath); }
                layout.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath); layout.worldBounds = world;
                EditorUtility.SetDirty(layout); AssetDatabase.SaveAssets();
                Debug.Log("COMBAT_RADAR_BUILD_PASS triangles=" + geometry.indices.Length / 3 + " bounds=" + world);
            }
            finally
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                foreach (var surface in surfaces) if (surface != null && surface.isActiveAndEnabled) surface.AddData();
            }
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
