using CrossFire2048.Client.Features.Account;
using UnityEditor;
using UnityEngine;

namespace CrossFire2048.Client.Editor
{
    [CustomEditor(typeof(LoginController))]
    public sealed class LoginControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Account Debug", EditorStyles.boldLabel);

            LoginController controller = (LoginController)target;

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Register"))
                {
                    controller.Register();
                }

                if (GUILayout.Button("Login"))
                {
                    controller.Login();
                }
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("进入 Play Mode 后可点击 Register / Login 测试服务端连接。", MessageType.Info);
            }
        }
    }
}
