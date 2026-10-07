using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Depth-based sight shading before post-processing and HUD, only on the training camera.</summary>
    public sealed class TrainingVisionRendererFeature : ScriptableRendererFeature
    {
        private Material material;
        private SightPass pass;

        public override void Create()
        {
            CoreUtils.Destroy(material);
            material = CoreUtils.CreateEngineMaterial(Resources.Load<Shader>("Training/TrainingVision"));
            pass = new SightPass(material);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            if (camera.cameraType != CameraType.Game || material == null) return;
            var vision = camera.GetComponent<TrainingVision>();
            if (vision == null || !vision.HasObserver) return;
            pass.Vision = vision;
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) { CoreUtils.Destroy(material); }

        private sealed class SightPass : ScriptableRenderPass
        {
            private readonly Material material;
            public TrainingVision Vision;
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
