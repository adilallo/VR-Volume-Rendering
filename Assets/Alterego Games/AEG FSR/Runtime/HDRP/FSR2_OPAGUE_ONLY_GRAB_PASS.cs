using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace AEG.FSR
{
    public class FSR2_OPAGUE_ONLY_GRAB_PASS : CustomPass
    {
        public FSR2_HDRP m_hdrp;
        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd) {
        }

        protected override void Execute(CustomPassContext ctx) {
            if(m_hdrp == null) {
                m_hdrp = ctx.hdCamera.camera.GetComponent<FSR2_HDRP>();
            }
            if(m_hdrp == null) {
                return;
            }
            ctx.cmd.Blit(ctx.cameraColorBuffer, m_hdrp.m_opaqueOnlyColorBuffer, ctx.cameraColorBuffer.rtHandleProperties.rtHandleScale, new Vector2(0, 0), 0, 0);
            m_hdrp.m_genReactiveDescription.ColorOpaqueOnly = m_hdrp.m_opaqueOnlyColorBuffer;
        }

        protected override void Cleanup() {
        }
    }
}
