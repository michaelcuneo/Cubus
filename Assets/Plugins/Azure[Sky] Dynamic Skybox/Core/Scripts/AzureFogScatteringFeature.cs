using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.AzureSky
{
    public class AzureFogScatteringFeature : ScriptableRendererFeature
    {
        [SerializeField] private bool m_enableFogScattering = true;

        /// <summary>The instance of the unity's RenderPassEvent class.</summary>
        public RenderPassEvent fogRenderPassEvent { get => m_fogRenderPassEvent; set => m_fogRenderPassEvent = value; }
        [SerializeField] private RenderPassEvent m_fogRenderPassEvent = RenderPassEvent.BeforeRenderingSkybox;

        /// <summary>The material that will render the fog scattering effect into the screen.</summary>
        public Material fogRendererMaterial { get => m_fogRendererMaterial; set => m_fogRendererMaterial = value; }
        [SerializeField] private Material m_fogRendererMaterial = null;

        /// <summary>The instance of the AzureFogScatteringPass class.</summary>
        private AzureFogScatteringPass m_azureFogScatteringPass;

        /// <summary>Called when the renderer feature is created or modified.</summary>
        public override void Create()
        {
            m_azureFogScatteringPass = new AzureFogScatteringPass
            {
                rendererMaterial = m_fogRendererMaterial,
                renderPassEvent = m_fogRenderPassEvent
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (camera == null)
            {
                return;
            }

            // Skip non-game cameras to avoid unintended fog overlays in utility passes.
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
            {
                return;
            }

            if (m_fogRendererMaterial == null)
            {
                Debug.LogWarningFormat("Missing the Fog Renderer Material. {0} blit pass will not execute. Check for missing reference in the assigned renderer.", GetType().Name);
                return;
            }

            // REQUIRED: Guarantees depth texture is available in RenderGraph and Legacy mode
            m_azureFogScatteringPass.ConfigureInput(ScriptableRenderPassInput.Depth);

            renderer.EnqueuePass(m_azureFogScatteringPass);
        }

        private class AzureFogScatteringPass : ScriptableRenderPass
        {
            /// <summary>The material used to render the ScriptableRenderPass.</summary>
            public Material rendererMaterial { get => m_rendererMaterial; set => m_rendererMaterial = value; }
            [SerializeField] private Material m_rendererMaterial = null;

            private TextureHandle m_sourceTextureHandle;
            private TextureHandle m_destinationTextureHandle;
            private TextureDesc m_destinationDesc;

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (m_rendererMaterial == null) return;

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                // The following line ensures that the render pass doesn't blit from the back buffer.
                if (resourceData.isActiveTargetBackBuffer)
                    return;

                m_sourceTextureHandle = resourceData.activeColorTexture;

                // Define the texture descriptor for creating the destination render graph texture.
                m_destinationDesc = renderGraph.GetTextureDesc(m_sourceTextureHandle);
                m_destinationDesc.name = "AzureFogScattering (RenderGraph Pass)";
                m_destinationDesc.clearBuffer = false;
                m_destinationDesc.depthBufferBits = 0;

                m_destinationTextureHandle = renderGraph.CreateTexture(m_destinationDesc);

                // This check is to avoid an error from the material preview in the scene
                if (!m_sourceTextureHandle.IsValid() || !m_destinationTextureHandle.IsValid())
                    return;

                // The AddBlitPass method adds the render graph pass that blits from the source to the destination texture.
                RenderGraphUtils.BlitMaterialParameters parameters = new(m_sourceTextureHandle, m_destinationTextureHandle, m_rendererMaterial, 0);
                renderGraph.AddBlitPass(parameters);

                // Tell URP to continue using our result as the active camera target.
                // This uses the destination texture as the camera texture to avoid the extra blit from the destination texture back to the camera texture.
                resourceData.cameraColor = m_destinationTextureHandle;
            }
        }
    }
}