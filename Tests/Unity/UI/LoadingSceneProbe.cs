using OperationBlacktide.Client.App;
using UnityEngine;

// Installed only in the disposable validation project, never in the shipped client.
public sealed class LoadingSceneProbe : MonoBehaviour, ISceneLoadParticipant
{
    public bool waitForSignal;
    public static bool Release;
    public static int Starts;
    private bool started;
    private void Start() { started = true; Starts++; }
    public bool IsSceneReady => started && (!waitForSignal || Release);
}
