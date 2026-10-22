using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Lobby
{
    /// <summary>Audio-driven UGUI mesh. Silence settles to a thin line; no idle fake speech.</summary>
    public sealed class LobbyVoiceWaveform : MaskableGraphic
    {
        private const int Bars = 40;
        private readonly float[] levels = new float[Bars];
        private readonly float[] targets = new float[Bars];
        private float lastAudio;
        public float Level { get; private set; }

        public void SetSamples(float[] samples, int channels = 1)
        {
            if (samples == null || samples.Length == 0) return;
            double total = 0;
            for (int bar = 0; bar < Bars; bar++)
            {
                int start = bar * samples.Length / Bars, end = (bar + 1) * samples.Length / Bars;
                double energy = 0;
                for (int i = start; i < end; i++) energy += samples[i] * samples[i];
                total += energy;
                targets[bar] = Mathf.Clamp01((Mathf.Sqrt((float)(energy / Mathf.Max(1, end - start))) - .003f) * 18);
            }
            Level = Mathf.Sqrt((float)(total / samples.Length));
            lastAudio = Time.unscaledTime;
        }

        public void Clear()
        {
            System.Array.Clear(targets, 0, Bars); System.Array.Clear(levels, 0, Bars);
            Level = 0; lastAudio = -10; SetVerticesDirty();
        }

        private void Update()
        {
            bool stale = Time.unscaledTime - lastAudio > .16f;
            float speed = 1 - Mathf.Exp(-18 * Time.unscaledDeltaTime);
            for (int i = 0; i < Bars; i++) levels[i] = Mathf.Lerp(levels[i], stale ? 0 : targets[i], speed);
            if (stale) Level = 0;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var rect = GetPixelAdjustedRect();
            float step = rect.width / Bars;
            for (int i = 0; i < Bars; i++)
            {
                float height = 1.5f + levels[i] * (rect.height - 1.5f);
                float x = rect.xMin + i * step + step * .28f, y = rect.center.y - height * .5f;
                var tint = color; tint.a *= .32f + levels[i] * .68f;
                int index = mesh.currentVertCount;
                mesh.AddVert(new Vector3(x, y), tint, Vector2.zero);
                mesh.AddVert(new Vector3(x, y + height), tint, Vector2.zero);
                mesh.AddVert(new Vector3(x + step * .44f, y + height), tint, Vector2.zero);
                mesh.AddVert(new Vector3(x + step * .44f, y), tint, Vector2.zero);
                mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3);
            }
        }
    }
}
