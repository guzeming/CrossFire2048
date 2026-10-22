using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OperationBlacktide.Client.UI
{
    /// <summary>Capture the finished world once, at half resolution, for all overlay glass controls.</summary>
    public sealed class UIGlassRendererFeature : ScriptableRendererFeature
    {
        private GlassPass pass;
        public override void Create()
        {
            pass?.Dispose(); pass = new GlassPass();
            pass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            var camera = data.cameraData.camera;
            if (data.cameraData.cameraType != CameraType.Game || data.cameraData.renderType != CameraRenderType.Base ||
                camera.cullingMask == (1 << 5)) return;
            renderer.EnqueuePass(pass);
        }
        protected override void Dispose(bool disposing) { pass?.Dispose(); pass = null; }
        private sealed class GlassPass : ScriptableRenderPass
        {
            private RTHandle backdrop;
            private static readonly int Texture = Shader.PropertyToID("_BlackGoldBackdrop");
            private static readonly int Available = Shader.PropertyToID("_BlackGoldBackdropAvailable");
            public GlassPass() { ConfigureInput(ScriptableRenderPassInput.Color); }
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
            {
                var descriptor = data.cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0; descriptor.msaaSamples = 1;
                descriptor.width = Mathf.Max(1, descriptor.width / 2);
                descriptor.height = Mathf.Max(1, descriptor.height / 2);
                RenderingUtils.ReAllocateIfNeeded(ref backdrop, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "BlackGoldBackdrop");
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                if (backdrop == null) return;
                var cmd = CommandBufferPool.Get("Black Gold / Scene glass");
                Blitter.BlitCameraTexture(cmd, data.cameraData.renderer.cameraColorTargetHandle, backdrop);
                cmd.SetGlobalTexture(Texture, backdrop.nameID);
                cmd.SetGlobalFloat(Available, 1);
                context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            }
            public void Dispose() { backdrop?.Release(); backdrop = null; Shader.SetGlobalFloat(Available, 0); }
        }
    }
}
