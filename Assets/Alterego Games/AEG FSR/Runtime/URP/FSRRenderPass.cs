#if UNITY_URP
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AEG.FSR
{
    public class FSRRenderPass : ScriptableRenderPass
    {
        private CommandBuffer cmd;

        private FSR2_URP m_fsrURP;

        public FSRRenderPass(FSR2_URP _fsrURP) {
            renderPassEvent = RenderPassEvent.AfterRendering + 5;
            m_fsrURP = _fsrURP;
        }

        public void OnSetReference(FSR2_URP _fsrURP) {
            m_fsrURP = _fsrURP;
        }

        // The actual execution of the pass. This is where custom rendering occurs.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
            ref CameraData cameraData = ref renderingData.cameraData;
#if UNITY_EDITOR
            if(cameraData.isPreviewCamera || cameraData.isSceneViewCamera) {
                return;
            }
            if(cameraData.camera.GetComponent<FSR2_URP>() == null) {
                return;
            }
#endif
            if(!cameraData.resolveFinalTarget) {
                return;
            }
            if(m_fsrURP == null) {
                return;
            }

            m_fsrURP.m_autoHDR = cameraData.isHdrEnabled;

            if(m_fsrURP.m_dispatchDescription.Color != null && m_fsrURP.m_dispatchDescription.Depth != null && m_fsrURP.m_dispatchDescription.MotionVectors != null) {
                cmd = CommandBufferPool.Get();

                if(m_fsrURP.generateReactiveMask) {
                    m_fsrURP.m_context.GenerateReactiveMask(m_fsrURP.m_genReactiveDescription, cmd);
                }
                m_fsrURP.m_context.Dispatch(m_fsrURP.m_dispatchDescription, cmd);


#if UNITY_2022_1_OR_NEWER
                Blitter.BlitCameraTexture(cmd, m_fsrURP.m_fsrOutput, cameraData.renderer.cameraColorTargetHandle, new Vector4(1, -1, 0, 1), 0, false);
#else
                Blit(cmd, m_fsrURP.m_fsrOutput, cameraData.renderer.cameraColorTarget);
#endif
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }

    public class FSRBufferPass : ScriptableRenderPass
    {
        private FSR2_URP m_fsrURP;

#if !UNITY_2022_1_OR_NEWER
        private int depthTexturePropertyID = Shader.PropertyToID("_CameraDepthTexture");
#endif
        private int motionTexturePropertyID = Shader.PropertyToID("_MotionVectorTexture");

        public FSRBufferPass(FSR2_URP _fsrURP) {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Depth);
            m_fsrURP = _fsrURP;
        }

        //2022 and up
        public void Setup(RenderTargetIdentifier color, RenderTargetIdentifier depth) {
            if(!Application.isPlaying) {
                return;
            }
            if(m_fsrURP == null) {
                return;
            }

            m_fsrURP.m_dispatchDescription.Color = color;
            m_fsrURP.m_dispatchDescription.Depth = depth;
            m_fsrURP.m_dispatchDescription.MotionVectors = Shader.GetGlobalTexture(motionTexturePropertyID);
        }

        public void OnSetReference(FSR2_URP _fsrURP) {
            m_fsrURP = _fsrURP;
        }

        // The actual execution of the pass. This is where custom rendering occurs.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
            ref CameraData cameraData = ref renderingData.cameraData;
#if UNITY_EDITOR
            if(cameraData.isPreviewCamera || cameraData.isSceneViewCamera) {
                return;
            }
            if(cameraData.camera.GetComponent<FSR2_URP>() == null) {
                return;
            }
#endif

            if(m_fsrURP == null) {
                return;
            }
            if(!cameraData.resolveFinalTarget) {
                return;
            }

#if !UNITY_2022_1_OR_NEWER
            m_fsrURP.m_dispatchDescription.Color = renderingData.cameraData.renderer.cameraColorTarget;
            m_fsrURP.m_dispatchDescription.Depth = Shader.GetGlobalTexture(depthTexturePropertyID);
            m_fsrURP.m_dispatchDescription.MotionVectors = Shader.GetGlobalTexture(motionTexturePropertyID);

#endif

            if(m_fsrURP.generateReactiveMask) {
                m_fsrURP.m_genReactiveDescription.ColorOpaqueOnly = m_fsrURP.m_opaqueOnlyColorBuffer;
                m_fsrURP.m_genReactiveDescription.ColorPreUpscale = m_fsrURP.m_afterOpaqueOnlyColorBuffer;
                m_fsrURP.m_genReactiveDescription.OutReactive = m_fsrURP.m_reactiveMaskOutput;
                m_fsrURP.m_dispatchDescription.Reactive = m_fsrURP.m_genReactiveDescription.OutReactive;
            } else {
                if(m_fsrURP.m_genReactiveDescription.ColorOpaqueOnly != null) {
                    m_fsrURP.m_genReactiveDescription.ColorOpaqueOnly = null;
                    m_fsrURP.m_genReactiveDescription.ColorPreUpscale = null;
                    m_fsrURP.m_genReactiveDescription.OutReactive = null;
                    m_fsrURP.m_dispatchDescription.Reactive = null;
                }
            }
        }
    }

    public class FSROpaqueOnlyPass : ScriptableRenderPass
    {
        private CommandBuffer cmd;
        private FSR2_URP m_fsrURP;

        public FSROpaqueOnlyPass(FSR2_URP _fsrURP) {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            m_fsrURP = _fsrURP;
        }

        public void OnSetReference(FSR2_URP _fsrURP) {
            m_fsrURP = _fsrURP;
        }

        // The actual execution of the pass. This is where custom rendering occurs.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
            ref CameraData cameraData = ref renderingData.cameraData;

            if(!m_fsrURP.generateReactiveMask) {
                return;
            }
            if(!cameraData.resolveFinalTarget) {
                return;
            }
            cmd = CommandBufferPool.Get();

#if UNITY_2022_1_OR_NEWER
            Blit(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle, m_fsrURP.m_opaqueOnlyColorBuffer);
#else
            Blit(cmd, renderingData.cameraData.renderer.cameraColorTarget, m_fsrURP.m_opaqueOnlyColorBuffer);
#endif
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

        }
    }

    public class FSRTransparentPass : ScriptableRenderPass
    {
        private CommandBuffer cmd;
        private FSR2_URP m_fsrURP;

        public FSRTransparentPass(FSR2_URP _fsrURP) {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            m_fsrURP = _fsrURP;
        }

        public void OnSetReference(FSR2_URP _fsrURP) {
            m_fsrURP = _fsrURP;
        }

        // The actual execution of the pass. This is where custom rendering occurs.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
            ref CameraData cameraData = ref renderingData.cameraData;

            if(!m_fsrURP.generateReactiveMask) {
                return;
            }
            if(!cameraData.resolveFinalTarget) {
                return;
            }
            cmd = CommandBufferPool.Get();

#if UNITY_2022_1_OR_NEWER
            Blit(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle, m_fsrURP.m_afterOpaqueOnlyColorBuffer);
#else
            Blit(cmd, renderingData.cameraData.renderer.cameraColorTarget, m_fsrURP.m_afterOpaqueOnlyColorBuffer);
#endif
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
#endif