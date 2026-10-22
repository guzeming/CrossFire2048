using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Demolition;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class VerifyUIAudio
{
    const string Active = "UIAudio.Verify";
    static int stage, cueIndex, before;
    static double next, started;
    static string error;
    static UISoundPlayer player;
    static GameObject canvas;
    static Button button, clone, closing;
    static Toggle toggle;
    static Dropdown dropdown;
    static InputField input;
    static Slider slider;
    static Scene originalScene;
    static readonly UISoundCue[] Cues = Enum.GetValues(typeof(UISoundCue)).Cast<UISoundCue>().Where(c => c != UISoundCue.Auto).ToArray();
    static readonly List<string> Report = new List<string>();
    static VerifyUIAudio()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += (message, trace, type) =>
        {
            if (SessionState.GetBool(Active, false) && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) error = message;
        };
    }
    public static void Run()
    {
        try
        {
            foreach (var cue in Cues)
            {
                string path = "Assets/Resources/UI/Audio/" + cue + ".wav";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                Check(clip != null && clip.length > .015f && clip.length <= 1.01f, "Invalid cue: " + cue);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                Check(importer.defaultSampleSettings.loadType == AudioClipLoadType.DecompressOnLoad && importer.defaultSampleSettings.preloadAudioData, "Cue not preloaded: " + cue);
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Active, true);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception exception) { Fail(exception); }
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
        if (started == 0) { started = EditorApplication.timeSinceStartup; next = started + 1; }
        try
        {
            Check(error == null, error);
            Check(EditorApplication.timeSinceStartup - started < 90, "Audio verification timeout at " + stage);
            if (EditorApplication.timeSinceStartup < next) return;
            switch (stage)
            {
                case 0:
                    originalScene = SceneManager.GetActiveScene();
                    new GameObject("Listener", typeof(AudioListener));
                    var root = new GameObject("UI root").AddComponent<UIRoot>();
                    player = UISoundPlayer.Ensure();
                    canvas = root.GetComponentInChildren<Canvas>().gameObject;
                    Check(player.PlayCount == 0, "UI initialization made a sound");
                    AuditPrefabs();
                    button = DemolitionUI.Button(canvas.transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), "Action", 0, 0, 200, () => { });
                    for (int i = 0; i < 5; i++) { BlackGoldTheme.Apply(button.gameObject); UISoundFeedback.BindAll(button.gameObject); }
                    button.gameObject.SetActive(false); button.gameObject.SetActive(true);
                    clone = Object.Instantiate(button, canvas.transform); clone.name = "clone";
                    toggle = DefaultControls.CreateToggle(new DefaultControls.Resources()).GetComponent<Toggle>();
                    toggle.transform.SetParent(canvas.transform, false); UISoundFeedback.Attach(toggle);
                    dropdown = DefaultControls.CreateDropdown(new DefaultControls.Resources()).GetComponent<Dropdown>();
                    dropdown.transform.SetParent(canvas.transform, false); UISoundFeedback.BindAll(dropdown.gameObject);
                    input = DefaultControls.CreateInputField(new DefaultControls.Resources()).GetComponent<InputField>();
                    input.transform.SetParent(canvas.transform, false); UISoundFeedback.Attach(input);
                    slider = DefaultControls.CreateSlider(new DefaultControls.Resources()).GetComponent<Slider>();
                    slider.transform.SetParent(canvas.transform, false); UISoundFeedback.Attach(slider);
                    toggle.isOn = false; dropdown.value = 1; slider.value = .7f; input.text = "silent initialization";
                    Check(player.PlayCount == 0, "Programmatic control values made sound");
                    Report.Add("All prefab controls bind once; all twenty cue assets load; programmatic values stay silent.");
                    break;
                case 1:
                    if (cueIndex < Cues.Length)
                    {
                        UISoundPlayer.Play(Cues[cueIndex]);
                        Check(player.LastCue == Cues[cueIndex], "Cue not played: " + Cues[cueIndex]);
                        var voice = player.GetComponentsInChildren<AudioSource>().Single(s => s.clip != null && s.clip.name == Cues[cueIndex].ToString());
                        var samples = new float[voice.clip.samples * voice.clip.channels];
                        Check(voice.clip.GetData(samples, 0) && samples.Any(x => Math.Abs(x) > .001), "Silent PCM: " + Cues[cueIndex]);
                        Check(voice.spatialBlend == 0 && voice.ignoreListenerPause && !voice.loop, "UI source configuration lost");
                        cueIndex++; next = EditorApplication.timeSinceStartup + .1; return;
                    }
                    Check(player.GetComponentsInChildren<AudioSource>().Length == 4, "Unbounded audio voices");
                    before = player.PlayCount; Click(button.gameObject);
                    Expect(UISoundCue.Click, 1); Report.Add("One pointer click = one sound after repeated theme application and enable cycles.");
                    break;
                case 2:
                    before = player.PlayCount; Click(clone.gameObject); Expect(UISoundCue.Click, 1);
                    before = player.PlayCount; button.interactable = false; Click(button.gameObject);
                    Check(player.PlayCount == before, "Disabled button played audio"); button.interactable = true;
                    Click(button.gameObject, PointerEventData.InputButton.Right);
                    Check(player.PlayCount == before, "Right click played audio");
                    ExecuteEvents.Execute(button.gameObject, Pointer(), ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(button.gameObject, Pointer(), ExecuteEvents.pointerUpHandler);
                    Check(player.PlayCount == before, "Cancelled pointer press played audio");
                    Report.Add("Template clone plays once; disabled, right-click and cancelled press remain silent.");
                    break;
                case 3:
                    before = player.PlayCount;
                    ExecuteEvents.Execute(button.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
                    Expect(UISoundCue.Click, 1); break;
                case 4:
                    before = player.PlayCount; Click(toggle.gameObject); Expect(UISoundCue.ToggleOn, 1); break;
                case 5:
                    before = player.PlayCount;
                    ExecuteEvents.Execute(toggle.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
                    Expect(UISoundCue.ToggleOff, 1); break;
                case 6:
                    before = player.PlayCount; Click(input.gameObject); Expect(UISoundCue.Input, 1); break;
                case 7:
                    before = player.PlayCount; Click(dropdown.gameObject); Expect(UISoundCue.Open, 1); break;
                case 8:
                    var option = dropdown.GetComponentsInChildren<Toggle>().First(t => t.gameObject.activeInHierarchy && !t.isOn);
                    Check(option.GetComponent<UISoundFeedback>() != null, "Spawned dropdown option has no audio");
                    before = player.PlayCount; Click(option.gameObject); Expect(UISoundCue.Option, 1);
                    Report.Add("Keyboard submit, toggles on/off, input focus, dropdown open and generated option selection each play their own cue.");
                    break;
                case 9:
                    before = player.PlayCount;
                    ExecuteEvents.Execute(slider.gameObject, Pointer(), ExecuteEvents.pointerDownHandler);
                    slider.value = .2f; slider.value = .3f; slider.value = .4f;
                    ExecuteEvents.Execute(slider.gameObject, Pointer(), ExecuteEvents.pointerUpHandler);
                    Expect(UISoundCue.Slider, 1);
                    before = player.PlayCount; slider.value = .8f;
                    Check(player.PlayCount == before, "Programmatic slider update played audio");
                    break;
                case 10:
                    // Sound is registered after an action that disables both the control and its object.
                    closing = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
                    closing.transform.SetParent(canvas.transform, false);
                    closing.onClick.AddListener(() => { closing.onClick.RemoveAllListeners(); closing.interactable = false; closing.gameObject.SetActive(false); });
                    UISoundFeedback.Attach(closing);
                    before = player.PlayCount; Click(closing.gameObject); Expect(UISoundCue.Back, 1);
                    Check(player.gameObject.activeInHierarchy, "Closing the panel stopped audio service");
                    Time.timeScale = 0; AudioListener.pause = true;
                    break;
                case 11:
                    before = player.PlayCount; Click(button.gameObject); Expect(UISoundCue.Click, 1);
                    Check(player.GetComponentsInChildren<AudioSource>().Any(s => s.clip?.name == "Click" && s.isPlaying), "Paused UI audio did not start");
                    AudioListener.pause = false; Time.timeScale = 1;
                    var destination = SceneManager.CreateScene("Audio next scene"); SceneManager.SetActiveScene(destination);
                    SceneManager.UnloadSceneAsync(originalScene);
                    break;
                case 12:
                    Check(player != null && UISoundPlayer.Ensure() == player, "Scene transition replaced UI audio player");
                    before = player.PlayCount; UISoundPlayer.Play(UISoundCue.Confirm); Expect(UISoundCue.Confirm, 1);
                    Report.Add("Sound survives panel closure and scene unload, works while paused, and uses a bounded four-voice pool with drag throttling.");
                    File.WriteAllLines("ui-audio-verification.txt", new[] { "PASS: UI audio integration" }.Concat(Report));
                    SessionState.SetBool(Active, false); Debug.Log("UI_AUDIO_VERIFY_PASS"); EditorApplication.Exit(0); return;
            }
            stage++; next = EditorApplication.timeSinceStartup + .25;
        }
        catch (Exception exception) { Fail(exception); }
    }
    static void AuditPrefabs()
    {
        int controls = 0;
        var holder = new GameObject("Inactive prefab audit"); holder.SetActive(false);
        var prefabs = Resources.LoadAll<GameObject>("UI/Panels");
        Check(prefabs.Length == Directory.GetFiles("Assets/Resources/UI/Panels", "*.prefab").Length && prefabs.Length > 0, "Not all UI prefabs loaded");
        foreach (var prefab in prefabs)
        {
            var copy = Object.Instantiate(prefab, holder.transform);
            BlackGoldTheme.Apply(copy); UISoundFeedback.BindAll(copy);
            foreach (var control in copy.GetComponentsInChildren<Selectable>(true))
            {
                if (control.name.Contains("Blocker")) continue;
                Check(control.GetComponents<UISoundFeedback>().Length == 1, "Missing/duplicated binding: " + prefab.name + "/" + control.name);
                controls++;
            }
            if (prefab.name == "LobbyPanel")
            {
                string[] names = { "Tab0", "Tab3", "Tab1", "Tab2" };
                var cues = names.Select(n => copy.transform.Find("TopBar/Navigation/" + n).GetComponent<UISoundFeedback>().ResolveCue()).ToArray();
                Check(cues.Distinct().Count() == 4, "Lobby tabs do not have distinct cues");
                Check(copy.transform.Find("PlayControls/EnterGameButton").GetComponent<UISoundFeedback>().ResolveCue() == UISoundCue.Start, "Start action cue lost");
                foreach (var row in copy.GetComponentsInChildren<OperationBlacktide.Client.Features.Lobby.LobbyLoadoutRow>(true))
                {
                    row.name = "Agent_fixture";
                    Check(row.button.GetComponent<UISoundFeedback>().ResolveCue() == UISoundCue.AgentSelect, "Agent row not classified");
                    row.name = "Weapon_fixture";
                    Check(row.button.GetComponent<UISoundFeedback>().ResolveCue() == UISoundCue.Select, "Weapon row not classified");
                }
            }
        }
        Check(controls > 0, "No authored controls audited");
        Report.Add("Audited " + controls + " authored controls, four distinct lobby tabs and loadout row templates.");
        Object.Destroy(holder);
    }
    static PointerEventData Pointer(PointerEventData.InputButton button = PointerEventData.InputButton.Left) => new PointerEventData(EventSystem.current) { button = button };
    static void Click(GameObject target, PointerEventData.InputButton button = PointerEventData.InputButton.Left) => ExecuteEvents.Execute(target, Pointer(button), ExecuteEvents.pointerClickHandler);
    static void Expect(UISoundCue cue, int count) => Check(player.PlayCount == before + count && player.LastCue == cue, $"Expected {count} {cue}, got delta {player.PlayCount - before}, {player.LastCue}");
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Fail(Exception exception)
    {
        SessionState.SetBool(Active, false); Time.timeScale = 1; AudioListener.pause = false;
        File.WriteAllText("ui-audio-failure.txt", exception.ToString()); Debug.LogException(exception); EditorApplication.Exit(1);
    }
}
