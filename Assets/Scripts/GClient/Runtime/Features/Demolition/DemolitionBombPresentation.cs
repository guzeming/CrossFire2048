using OperationBlacktide.Client.Gameplay.Characters;
using OperationBlacktide.Shared.Modes.Demolition;
using UnityEngine;

namespace OperationBlacktide.Client.Features.Demolition
{
    /// <summary>World C4 and spatial phase sounds. Match state remains server-authoritative.</summary>
    public sealed class DemolitionBombPresentation : MonoBehaviour
    {
        DemolitionPresentationAssets assets;
        GameObject model;
        AudioSource events, beeper;
        Light indicator;
        BombPhase phase;
        int round;
        bool received, ticking;
        float remaining, receivedAt, nextBeep, flashUntil;
        Vector3 lastPosition;
        public GameObject Model => model;
        public int PhaseSoundsPlayed { get; private set; }
        public int BeepsPlayed { get; private set; }

        public void Initialize(DemolitionPresentationAssets data)
        {
            assets = data;
            if (data == null) { Debug.LogError("[Demolition] Missing C4 Presentation assets."); return; }
            model = Instantiate(data.bombPrefab, transform, false); model.name = "World C4"; model.SetActive(false);
            model.transform.localScale = Vector3.Scale(model.transform.localScale, data.worldScale);
            events = Audio("C4 phase sounds", 85); beeper = Audio("C4 countdown", 35);
            var lamp = new GameObject("C4 indicator"); lamp.transform.SetParent(transform, false);
            indicator = lamp.AddComponent<Light>(); indicator.type = LightType.Point;
            indicator.color = Color.red; indicator.range = .7f; indicator.intensity = 0;
        }
        AudioSource Audio(string name, float distance)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>(); source.playOnAwake = false;
            source.spatialBlend = 1; source.minDistance = 3; source.maxDistance = distance;
            source.dopplerLevel = 0; source.rolloffMode = AudioRolloffMode.Linear; return source;
        }
        public void Present(DemolitionSnapshot state)
        {
            if (assets == null) return;
            bool newRound = !received || round != state.Round;
            if (newRound) { events.Stop(); beeper.Stop(); nextBeep = 0; }
            else if (phase != state.Bomb)
            {
                AudioClip clip = state.Bomb == BombPhase.Planted ? assets.planted :
                    phase == BombPhase.Planted && state.Bomb == BombPhase.Defused ? assets.defused :
                    phase == BombPhase.Planted && state.Bomb == BombPhase.Exploded ? assets.explode : null;
                if (clip != null) { events.PlayOneShot(clip); PhaseSoundsPlayed++; }
            }
            var position = new Vector3(state.BombPosition.X, state.BombPosition.Y, state.BombPosition.Z);
            bool visible = state.Bomb == BombPhase.Dropped || state.Bomb == BombPhase.Planted || state.Bomb == BombPhase.Defused;
            model.SetActive(visible);
            if (visible && (!received || newRound || position != lastPosition || phase != state.Bomb)) PlaceOnGround(position);
            phase = state.Bomb; round = state.Round; received = true; lastPosition = position;
            remaining = state.Remaining; receivedAt = Time.unscaledTime;
            ticking = state.Bomb == BombPhase.Planted && state.Phase == RoundPhase.Planted;
            if (!ticking) { beeper.Stop(); indicator.intensity = 0; }
        }
        void PlaceOnGround(Vector3 position)
        {
            // Server coordinates use the pawn's feet. Ignore nearby pawns when finding the floor.
            float nearest = float.MaxValue; Vector3 ground = position;
            foreach (var hit in Physics.RaycastAll(position + Vector3.up * .5f, Vector3.down, 2f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<CharacterMotor>() != null || hit.distance >= nearest) continue;
                nearest = hit.distance; ground = hit.point;
            }
            transform.position = ground;
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.Euler(assets.groundEuler);
            // Mesh bounds are valid even while the model is inactive.
            var bounds = OperationBlacktide.Client.Gameplay.Avatars.ModelComponent.StaticMeshBounds(model);
            model.transform.position += new Vector3(ground.x - bounds.center.x, ground.y + .025f - bounds.min.y, ground.z - bounds.center.z);
            indicator.transform.localPosition = new Vector3(0, .15f, 0);
        }
        void Update()
        {
            if (!ticking || assets == null) return;
            float now = Time.unscaledTime;
            // Stop stale beeps during a lost connection, without inventing a detonation.
            if (now - receivedAt > 1f) { beeper.Stop(); indicator.intensity = 0; return; }
            float seconds = Mathf.Max(0, remaining - (now - receivedAt));
            if (now >= nextBeep)
            {
                beeper.PlayOneShot(seconds <= 10 ? assets.urgentBeep : assets.beep, .7f); BeepsPlayed++;
                nextBeep = now + Mathf.Lerp(.18f, 1f, Mathf.Clamp01(seconds / 40f)); flashUntil = now + .09f;
            }
            indicator.intensity = now < flashUntil ? 1.5f : 0;
        }
        public void StopPresentation()
        {
            ticking = false;
            if (events != null) events.Stop(); if (beeper != null) beeper.Stop();
            if (indicator != null) indicator.intensity = 0; if (model != null) model.SetActive(false);
        }
        void OnDisable() { StopPresentation(); }
    }
}
