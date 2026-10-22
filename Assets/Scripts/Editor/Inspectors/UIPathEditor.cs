using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEngine;

namespace OperationBlacktide.Client.Editor
{
    /// <summary>
    /// 读写 UIPath.cs 中的路径字典。
    /// </summary>
    public static class UIPathEditor
    {
        public const string UIPathScriptPath = "Assets/Scripts/GClient/Runtime/UI/UIPath.cs";

        public static Dictionary<string, string> ReadPathMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            string content = File.ReadAllText(UIPathScriptPath);
            MatchCollection matches = Regex.Matches(
                content,
                @"\{\s*""([^""]+)""\s*,\s*""([^""]+)""\s*\}");

            foreach (Match match in matches)
            {
                if (!match.Success || match.Groups.Count < 3)
                {
                    continue;
                }

                string key = match.Groups[1].Value;
                string value = match.Groups[2].Value;
                map[key] = value;
            }

            return map;
        }

        public static bool AddOrUpdate(string panelName, string resourcePath, bool showDialog = true)
        {
            if (string.IsNullOrWhiteSpace(panelName))
            {
                if (showDialog)
                {
                    EditorUtility.DisplayDialog("Add UI", "界面名字不能为空。", "OK");
                }

                return false;
            }

            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                if (showDialog)
                {
                    EditorUtility.DisplayDialog("Add UI", "Prefab 资源路径不能为空。", "OK");
                }

                return false;
            }

            panelName = panelName.Trim();
            resourcePath = NormalizeResourcePath(resourcePath);

            Dictionary<string, string> map = ReadPathMap();
            map[panelName] = resourcePath;
            WritePathMap(map);
            AssetDatabase.Refresh();
            return true;
        }

        public static bool TryGetResourcePathFromAsset(Object asset, out string resourcePath)
        {
            resourcePath = string.Empty;
            if (asset == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return false;
            }

            resourcePath = AssetPathToResourcePath(assetPath);
            return !string.IsNullOrWhiteSpace(resourcePath);
        }

        public static string AssetPathToResourcePath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return string.Empty;
            }

            assetPath = assetPath.Replace('\\', '/');
            const string resourcesPrefix = "Assets/Resources/";
            if (!assetPath.StartsWith(resourcesPrefix))
            {
                return string.Empty;
            }

            string relative = assetPath.Substring(resourcesPrefix.Length);
            if (relative.EndsWith(".prefab"))
            {
                relative = relative.Substring(0, relative.Length - ".prefab".Length);
            }

            return relative;
        }

        public static string NormalizeResourcePath(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                return string.Empty;
            }

            resourcePath = resourcePath.Replace('\\', '/').Trim();
            if (resourcePath.StartsWith("Assets/Resources/"))
            {
                resourcePath = AssetPathToResourcePath(resourcePath);
            }

            if (resourcePath.EndsWith(".prefab"))
            {
                resourcePath = resourcePath.Substring(0, resourcePath.Length - ".prefab".Length);
            }

            return resourcePath;
        }

        private static void WritePathMap(Dictionary<string, string> map)
        {
            string content = File.ReadAllText(UIPathScriptPath);
            int begin = content.IndexOf(UIPath.MapBeginMarker);
            int end = content.IndexOf(UIPath.MapEndMarker);
            if (begin < 0 || end < 0 || end <= begin)
            {
                Debug.LogError("[UIPathEditor] 未找到 UIPath 映射标记，无法写入。");
                return;
            }

            List<string> keys = new List<string>(map.Keys);
            keys.Sort();

            StringBuilder builder = new StringBuilder();
            builder.AppendLine(UIPath.MapBeginMarker);
            for (int i = 0; i < keys.Count; i++)
            {
                builder.Append("            { \"").Append(keys[i]).Append("\", \"")
                    .Append(map[keys[i]]).Append("\" },");
                builder.AppendLine();
            }

            builder.Append("            ").Append(UIPath.MapEndMarker);

            string updated = content.Substring(0, begin)
                + builder.ToString()
                + content.Substring(end + UIPath.MapEndMarker.Length);

            File.WriteAllText(UIPathScriptPath, updated, Encoding.UTF8);
        }
    }
}
