using OperationBlacktide.Client.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyFirstPerson
{
    public static void Run()
    {
        BuildFirstPerson.Build();
        RunExisting();
    }

    public static void RunExisting()
    {
        ShaderUtil.allowAsyncCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/DustII.unity");
        new GameObject("First Person Verification").AddComponent<FirstPersonVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }
}
