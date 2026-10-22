using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OperationBlacktide.Client.Gameplay.Vision
{
    /// <summary>Depth-based sight shading before post-processing and HUD on cameras with an observer.</summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Training", "Assembly-CSharp", "TrainingVisionRendererFeature")]
    public sealed class CharacterVisionRendererFeature : ScriptableRendererFeature
    {
        private Material material;
        private SightPass pass;

        public override void Create()
        {
            CoreUtils.Destroy(material);
            material = CoreUtils.CreateEngineMaterial(Resources.Load<Shader>("Gameplay/CharacterVision"));
            pass = new SightPass(material);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            if (camera.cameraType != CameraType.Game || material == null) return;
            var vision = camera.GetComponent<CharacterVision>();
            if (vision == null || !vision.HasObserver) return;
            pass.Vision = vision;
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) { CoreUtils.Destroy(material); }

        private sealed class SightPass : ScriptableRenderPass
        {
            private readonly Material material;
            public CharacterVision Vision;
            public SightPass(Material material)
            {
                this.material = material;
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) { ResetTarget(); }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (Vision == null || !Vision.HasObserver) return;
                Vision.BindMaterial(material);
                var cmd = CommandBufferPool.Get("Training character sight");
                CoreUtils.SetRenderTarget(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle);
                cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
