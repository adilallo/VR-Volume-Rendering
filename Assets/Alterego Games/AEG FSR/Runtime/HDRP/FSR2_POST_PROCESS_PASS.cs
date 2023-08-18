using System;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using FidelityFX;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AEG.FSR
{
    public class FSR2_POST_PROCESS_PASS : CustomPostProcessVolumeComponent, IPostProcessComponent
    {
        [HideInInspector]
        public BoolParameter enable = new BoolParameter(false);

        public bool IsActive() => enable.value;

        public override CustomPostProcessInjectionPoint injectionPoint => CustomPostProcessInjectionPoint.BeforeTAA;

        private int depthTexturePropertyID = Shader.PropertyToID("_CameraDepthTexture");
        private int motionTexturePropertyID = Shader.PropertyToID("_CameraMotionVectorsTexture");

        private FSR2_HDRP m_hdrp;
        private FSR_Quality currentQuality;


        public override void Setup() {
        }

        public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle source, RTHandle destination) {


            if(Application.isPlaying) {
                if(!IsActive()) {
                    return;
                }
#if UNITY_EDITOR
                if(SceneView.GetAllSceneCameras().Length > 0) {
                    if(camera.name == SceneView.GetAllSceneCameras()[0].name) {
                        cmd.Blit(source, destination, 0, 0);
                        return;
                    }
                }
#endif
                if(m_hdrp == null) {
                    m_hdrp = camera.camera.GetComponent<FSR2_HDRP>();
                }
                if(m_hdrp == null) {
                    return;
                }

                if(currentQuality != m_hdrp.FSRQuality) {
                    currentQuality = m_hdrp.FSRQuality;
                    return;
                }

                m_hdrp.m_dispatchDescription.Color = source.rt;
                m_hdrp.m_dispatchDescription.Depth = Shader.GetGlobalTexture(depthTexturePropertyID);
                m_hdrp.m_dispatchDescription.MotionVectors = Shader.GetGlobalTexture(motionTexturePropertyID);
                m_hdrp.m_dispatchDescription.Output = destination.rt;

                if(m_hdrp.generateReactiveMask) {
                    m_hdrp.m_genReactiveDescription.OutReactive = m_hdrp.m_reactiveMaskOutput;
                    m_hdrp.m_dispatchDescription.Reactive = m_hdrp.m_reactiveMaskOutput;
                }

                if(m_hdrp.m_dispatchDescription.Color != null && m_hdrp.m_dispatchDescription.Depth != null && m_hdrp.m_dispatchDescription.MotionVectors != null && m_hdrp.m_dispatchDescription.MotionVectorScale.x != 0) {
                    if(m_hdrp.generateReactiveMask) {
                        m_hdrp.m_context.GenerateReactiveMask(m_hdrp.m_genReactiveDescription, cmd);
                    }
                    m_hdrp.m_context.Dispatch(m_hdrp.m_dispatchDescription, cmd);
                }
            }
        }

        public override void Cleanup() {
        }
    }
}
