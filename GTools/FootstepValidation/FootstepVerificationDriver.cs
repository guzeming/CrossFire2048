#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using OperationBlacktide.Client.Features.Training;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class FootstepVerificationDriver : MonoBehaviour
{
    private const float Dt = 1f / 60;
    private TrainingCharacterController player;
    private TrainingFootstepAudio footsteps;
    private AudioSource source;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.logMessageReceived += OnLog;
    }
    private void OnLog(string message, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        File.WriteAllText("footstep-failure.txt", message + "\n" + trace);
        EditorApplication.Exit(1);
    }

    private IEnumerator Start()
    {
        yield return null; yield return null;
        var scene = FindObjectOfType<TrainingSceneController>();
        player = scene.Player;
        footsteps = player.GetComponent<TrainingFootstepAudio>();
        Check(footsteps != null && footsteps.enabled, "Scene did not attach footsteps.");
        source = player.transform.Find("Footstep Audio").GetComponent<AudioSource>();
        Check(source != null && !source.loop && !source.playOnAwake, "Footstep source configuration is invalid.");
        Check(source != player.GetComponent<AudioSource>(), "Footsteps share the gun AudioSource.");
        player.enabled = false;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Footstep Verification Floor";
        floor.transform.SetPositionAndRotation(new Vector3(0, 99.5f, 0), Quaternion.identity);
        floor.transform.localScale = new Vector3(60, 1, 60);
        Physics.SyncTransforms();
        player.Initialize(Camera.main, new Vector3(0, 100.08f, 0), Quaternion.identity);
        ResetPlayer();
        int steps = footsteps.StepsPlayed, lands = footsteps.LandingsPlayed;
        for (int i = 0; i < 120; i++) Move(Vector2.zero);
        Check(footsteps.StepsPlayed == steps && footsteps.LandingsPlayed == lands, "Idle/spawn plays footsteps.");

        AudioClip previous = null;
        for (int i = 0; i < 120; i++)
        {
            int count = footsteps.StepsPlayed;
            Move(Vector2.up);
            if (footsteps.StepsPlayed != count)
            {
                Check(footsteps.LastClip != previous, "Consecutive duplicate footstep sample.");
                previous = footsteps.LastClip;
            }
        }
        int walk = footsteps.StepsPlayed - steps;
        Check(walk >= 3 && walk <= 5 && source.isPlaying, "Walk cadence/audio is incorrect: " + walk);
        ResetPlayer();
        steps = footsteps.StepsPlayed;
        for (int i = 0; i < 120; i++) Move(Vector2.up, true);
        int run = footsteps.StepsPlayed - steps;
        Check(run > walk && run <= 7, "Sprint cadence is not faster: " + run);
        foreach (var direction in new[] { Vector2.left, Vector2.right, Vector2.down })
        {
            ResetPlayer(); steps = footsteps.StepsPlayed;
            for (int i = 0; i < 60; i++) Move(direction);
            Check(footsteps.StepsPlayed > steps, "Strafe/backward movement is silent.");
        }
        ResetPlayer();
        int? expected = null;
        foreach (int fps in new[] { 30, 60, 144 })
        {
            ResetPlayer(); steps = footsteps.StepsPlayed;
            for (int i = 0; i < fps * 2; i++)
            {
                player.Move(Vector2.up, false, false, 1f / fps);
                footsteps.UpdateAudio(1f / fps, true);
            }
            int count = footsteps.StepsPlayed - steps;
            Check(!expected.HasValue || Math.Abs(expected.Value - count) <= 1, "Frame-dependent footsteps.");
            expected = count;
        }
        Debug.Log("FOOTSTEP_MOVEMENT_PASS: walk=" + walk + ", sprint=" + run + ", strafing/backward/random variants/frame rates");

        ResetPlayer();
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = player.transform.position + Vector3.forward * 1.2f + Vector3.up;
        wall.transform.localScale = new Vector3(3, 3, .2f);
        Physics.SyncTransforms();
        for (int i = 0; i < 120; i++) Move(Vector2.up, true);
        steps = footsteps.StepsPlayed;
        for (int i = 0; i < 120; i++) Move(Vector2.up, true);
        Check(footsteps.StepsPlayed == steps, "Pushing into a wall keeps playing footsteps.");
        wall.SetActive(false); Destroy(wall);

        ResetPlayer(); steps = footsteps.StepsPlayed; lands = footsteps.LandingsPlayed;
        Move(Vector2.zero, false, true);
        Check(!player.IsGrounded, "Test jump did not leave the floor.");
        bool wasAirborne = false;
        for (int i = 0; i < 120; i++)
        {
            Move(Vector2.zero);
            if (!player.IsGrounded)
            {
                wasAirborne = true;
                Check(footsteps.StepsPlayed == steps && footsteps.LandingsPlayed == lands, "Airborne character plays a foot contact.");
            }
        }
        Check(wasAirborne && player.IsGrounded && footsteps.LandingsPlayed == lands + 1, "Landing did not play exactly once.");
        Check(footsteps.StepsPlayed == steps, "Landing doubled with a step.");
        Move(Vector2.zero, false, true);
        for (int i = 0; i < 15; i++) Move(Vector2.zero);
        lands = footsteps.LandingsPlayed;
        player.Respawn();
        for (int i = 0; i < 60; i++) Move(Vector2.zero);
        Check(footsteps.LandingsPlayed == lands, "Respawn produced a false landing.");
        Debug.Log("FOOTSTEP_COLLISION_JUMP_PASS");

        ResetPlayer();
        for (int i = 0; i < 30; i++) Move(Vector2.up);
        steps = footsteps.StepsPlayed;
        scene.RequestReturnToLobby();
        for (int i = 0; i < 60; i++) Move(Vector2.up);
        Check(footsteps.StepsPlayed == steps && !source.isPlaying, "Popup did not silence footsteps.");
        UIRoot.Instance.HandleBackInput();
        player.InputEnabled = !scene.IsInputBlocked;
        for (int i = 0; i < 30; i++) Move(Vector2.up);
        Check(footsteps.StepsPlayed > steps, "Footsteps did not resume.");
        steps = footsteps.StepsPlayed;
        for (int i = 0; i < 30; i++)
        {
            player.Move(Vector2.up, true, false, Dt);
            footsteps.UpdateAudio(Dt, false);
        }
        Check(footsteps.StepsPlayed == steps && !source.isPlaying, "Focus block did not silence footsteps.");
        footsteps.UpdateAudio(0, true);
        Check(footsteps.StepsPlayed == steps && !source.isPlaying, "Paused time played footsteps.");
        player.Respawn();
        Check(!source.isPlaying, "Respawn kept old footstep audio.");
        Debug.Log("FOOTSTEP_INPUT_PASS");
        Destroy(floor);
        SceneManager.LoadScene("Assets/Scenes/SampleScene.unity");
        yield return null; yield return null;
        Check(GameObject.Find("Footstep Audio") == null && FindObjectOfType<TrainingFootstepAudio>() == null,
            "Footstep audio survived scene exit.");
        Debug.Log("FOOTSTEP_VERIFY_PASS");
        EditorApplication.Exit(0);
    }

    private void ResetPlayer()
    {
        player.Respawn();
        player.InputEnabled = true;
        for (int i = 0; i < 30; i++) player.Move(Vector2.zero, false, false, Dt);
        Check(player.IsGrounded, "Verification player did not settle on the floor.");
        footsteps.ResetMotion();
    }
    private void Move(Vector2 direction, bool run = false, bool jump = false)
    {
        player.Move(direction, run, jump, Dt);
        footsteps.UpdateAudio(Dt, true);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
#endif
