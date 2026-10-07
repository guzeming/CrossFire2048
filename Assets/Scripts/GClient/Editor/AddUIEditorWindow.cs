using System.Collections.Generic;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEngine;

namespace OperationBlacktide.Client.Editor
{
    /// <summary>
    /// Add UI 工具：填写界面名与 Prefab Resources 路径，写入 UIPath 字典。
    /// </summary>
    public sealed class AddUIEditorWindow : EditorWindow
    {
        private string _panelName = string.Empty;
        private string _resourcePath = string.Empty;
        private UIPanel _prefabAsset;
        private Vector2 _scroll;

        [MenuItem("OperationBlacktide/UI/Add UI")]
        public static void Open()
        {
            AddUIEditorWindow window = GetWindow<AddUIEditorWindow>("Add UI");
            window.minSize = new Vector2(420f, 360f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Add UI", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "填写界面名字和 Prefab 的 Resources 路径，点击 Add 后会写入 UIPath.cs 字典。\n" +
                "路径示例：UI/Panels/LoginPanel（对应 Assets/Resources/UI/Panels/LoginPanel.prefab）",
                MessageType.Info);

            _panelName = EditorGUILayout.TextField("界面名字", _panelName);
            _resourcePath = EditorGUILayout.TextField("Resources 路径", _resourcePath);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("或从 Prefab 自动填路径（需在 Resources 目录下）", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            _prefabAsset = (UIPanel)EditorGUILayout.ObjectField("Prefab", _prefabAsset, typeof(UIPanel), false);
            if (EditorGUI.EndChangeCheck() && _prefabAsset != null)
            {
                if (UIPathEditor.TryGetResourcePathFromAsset(_prefabAsset, out string autoPath))
                {
                    _resourcePath = autoPath;
                    if (string.IsNullOrWhiteSpace(_panelName))
                    {
                        _panelName = _prefabAsset.PanelId;
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "Add UI",
                        "该 Prefab 不在 Assets/Resources/ 目录下。\n请移动到 Resources 后再选择，或手动填写路径。",
                        "OK");
                }
            }

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add", GUILayout.Height(28f)))
                {
                    if (UIPathEditor.AddOrUpdate(_panelName, _resourcePath))
                    {
                        ShowNotification(new GUIContent($"已添加：{_panelName}"));
                        _panelName = string.Empty;
                        _resourcePath = string.Empty;
                        _prefabAsset = null;
                    }
                }

                if (GUILayout.Button("打开 UIPath.cs", GUILayout.Height(28f)))
                {
                    Object script = AssetDatabase.LoadAssetAtPath<Object>(UIPathEditor.UIPathScriptPath);
                    AssetDatabase.OpenAsset(script);
                }
            }

            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("当前 UIPath 字典", EditorStyles.boldLabel);

            Dictionary<string, string> map = UIPathEditor.ReadPathMap();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (KeyValuePair<string, string> pair in map)
            {
                EditorGUILayout.LabelField(pair.Key, pair.Value);
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
