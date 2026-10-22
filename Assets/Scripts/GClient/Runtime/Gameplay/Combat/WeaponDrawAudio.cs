using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Combat
{
    /// <summary>One draw cue per equipment change, shared by local and network presentation.</summary>
    public sealed class WeaponDrawAudio : MonoBehaviour
    {
        private AudioSource source;
        private string currentId;
        public int SoundsPlayed { get; private set; }
        public AudioClip LastClip => source != null ? source.clip : null;

        public static void Equip(GameObject owner, string id, bool local)
        {
            var audio = owner.GetComponent<WeaponDrawAudio>();
            if (audio == null) audio = owner.AddComponent<WeaponDrawAudio>();
            audio.Present(id, local);
        }

        public void Present(string id, bool local)
        {
            if (currentId == id) return;
            currentId = id;
            if (source != null) source.Stop();
            if (string.IsNullOrEmpty(id)) return;
            var clip = Resources.Load<AudioClip>("Audio/WeaponDraw/" + id);
            if (clip == null) return;
            if (source == null)
            {
                var child = new GameObject("Weapon Draw Audio");
                child.transform.SetParent(transform, false);
                source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.volume = .65f;
                source.minDistance = 2; source.maxDistance = 25;
                source.rolloffMode = AudioRolloffMode.Linear;
            }
            source.spatialBlend = local ? 0 : 1;
            source.clip = clip;
            source.Play(); SoundsPlayed++;
        }

        public void Stop() { if (source != null) source.Stop(); }
        private void OnDisable() { Stop(); }
        private void OnDestroy() { if (source != null) Destroy(source.gameObject); }
    }
}
