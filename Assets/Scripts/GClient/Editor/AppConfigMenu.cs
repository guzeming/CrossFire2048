using CrossFire2048.Client.App;
using UnityEditor;
using UnityEngine;

namespace CrossFire2048.Client.Editor
{
    public static class AppConfigMenu
    {
        private const string AssetPath = "Assets/Scripts/GClient/Runtime/App/AppConfig.asset";

        [MenuItem("CrossFire2048/Create Default App Config")]
        public static void CreateDefaultAppConfig()
        {
            AppConfig existing = AssetDatabase.LoadAssetAtPath<AppConfig>(AssetPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            AppConfig config = ScriptableObject.CreateInstance<AppConfig>();
            AssetDatabase.CreateAsset(config, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }
    }
}
