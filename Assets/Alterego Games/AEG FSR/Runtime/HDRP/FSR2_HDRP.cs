#if UNITY_HDRP
using FidelityFX;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace AEG.FSR
{
    /// <summary>
    /// FSR implementation for the High Definition RenderPipeline
    /// </summary>
    public class FSR2_HDRP : FSR2_BASE
    {
        public static FSR2_HDRP Instance;
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
        }

        //public RTHandleSystem RTHandleS;
        private GraphicsFormat m_colorBufferFormat = GraphicsFormat.B10G11R11_UFloatPack32;
        public RTHandle m_opaqueOnlyColorBuffer;
        public RTHandle m_afterOpaqueOnlyColorBuffer;
        public RTHandle m_reactiveMaskOutput;

        private Volume m_postProcessVolume;
        private FSR2_POST_PROCESS_PASS m_postProcessPass;

        //Reactive mask setup
        private bool m_previousGenerateReactiveMask;
        private CustomPassVolume m_opaqueOnlyGrabVolume;
        private CustomPassVolume m_afterOpaqueOnlyGrabVolume;

        private new HDCamera m_mainCamera;

        private Matrix4x4 m_jitterMatrix;
        private Matrix4x4 m_projectionMatrix;

        public GraphicsFormat m_graphicsFormat;
        public readonly Fsr2.DispatchDescription m_dispatchDescription = new Fsr2.DispatchDescription();
        public readonly Fsr2.GenerateReactiveDescription m_genReactiveDescription = new Fsr2.GenerateReactiveDescription();
        private IFsr2Callbacks Callbacks { get; set; } = new Fsr2CallbacksBase();
        public Fsr2Context m_context;

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        protected override void InitializeFSR()
        {
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            RenderPipelineManager.endContextRendering += OnEndContextRendering;

            m_postProcessVolume = gameObject.AddComponent<Volume>();
            m_postProcessVolume.hideFlags = HideFlags.HideInInspector;
            m_postProcessVolume.isGlobal = true;
            m_postProcessPass = m_postProcessVolume.profile.Add<FSR2_POST_PROCESS_PASS>();
            m_postProcessPass.enable.value = true;
            m_postProcessPass.enable.Override(true);

            m_opaqueOnlyGrabVolume = gameObject.AddComponent<CustomPassVolume>();
            m_opaqueOnlyGrabVolume.hideFlags = HideFlags.HideInInspector;
            m_opaqueOnlyGrabVolume.injectionPoint = CustomPassInjectionPoint.BeforeTransparent;
            CustomPass _opaqueOnlyPass = m_opaqueOnlyGrabVolume.AddPassOfType<FSR2_OPAGUE_ONLY_GRAB_PASS>();
            _opaqueOnlyPass.name = "FSR Opaque Only Grab Pass";
            foreach (var pass in m_opaqueOnlyGrabVolume.customPasses)
            {
                var opaquePass = pass as FSR2_OPAGUE_ONLY_GRAB_PASS;
                if (opaquePass != null)
                {
                    opaquePass.m_hdrp = this;
                }
            }

            m_afterOpaqueOnlyGrabVolume = gameObject.AddComponent<CustomPassVolume>();
            m_afterOpaqueOnlyGrabVolume.hideFlags = HideFlags.HideInInspector;
            m_afterOpaqueOnlyGrabVolume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
            CustomPass _afterOpaqueOnlyPass = m_afterOpaqueOnlyGrabVolume.AddPassOfType<FSR2_AFTER_OPAGUE_ONLY_GRAB_PASS>();
            _afterOpaqueOnlyPass.name = "FSR After Opaque Only Grab Pass";
            foreach (var pass in m_afterOpaqueOnlyGrabVolume.customPasses)
            {
                var afterOpaquePass = pass as FSR2_AFTER_OPAGUE_ONLY_GRAB_PASS;
                if (afterOpaquePass != null)
                {
                    afterOpaquePass.m_hdrp = this;
                }
            }

            GetHDCamera();
        }

        private void OnBeginContextRendering(ScriptableRenderContext renderContext, List<Camera> cameras)
        {
            GetHDCamera();
            DynamicResolutionHandler.SetDynamicResScaler(SetDynamicResolutionScale, DynamicResScalePolicyType.ReturnsPercentage);

            // Set up the parameters to auto-generate a reactive mask
            if (generateReactiveMask)
            {
                m_genReactiveDescription.RenderSize = new Vector2Int(m_renderWidth, m_renderHeight);
                m_genReactiveDescription.Scale = autoReactiveScale;
                m_genReactiveDescription.CutoffThreshold = autoTcThreshold;
                m_genReactiveDescription.BinaryValue = autoReactiveBinaryValue;

                m_genReactiveDescription.Flags = reactiveFlags;
            }

            m_dispatchDescription.Exposure = null;
            m_dispatchDescription.PreExposure = 1;
            m_dispatchDescription.EnableSharpening = sharpening;
            m_dispatchDescription.Sharpness = sharpness;
            m_dispatchDescription.MotionVectorScale.x = -m_renderWidth;
            m_dispatchDescription.MotionVectorScale.y = -m_renderHeight;
            m_dispatchDescription.RenderSize = new Vector2Int(m_renderWidth, m_renderHeight);
            m_dispatchDescription.FrameTimeDelta = Time.deltaTime;
            m_dispatchDescription.CameraNear = m_mainCamera.camera.nearClipPlane;
            m_dispatchDescription.CameraFar = m_mainCamera.camera.farClipPlane;
            m_dispatchDescription.CameraFovAngleVertical = m_mainCamera.camera.fieldOfView * Mathf.Deg2Rad;
            m_dispatchDescription.ViewSpaceToMetersFactor = 1.0f;
            m_dispatchDescription.Reset = m_resetCamera;

            //Experimental!  (disabled)
            m_dispatchDescription.EnableAutoReactive = generateTCMask;
            m_dispatchDescription.AutoTcThreshold = autoTcThreshold;
            m_dispatchDescription.AutoTcScale = autoTcScale;
            m_dispatchDescription.AutoReactiveScale = autoReactiveScale;
            m_dispatchDescription.AutoReactiveMax = autoTcReactiveMax;

            m_resetCamera = false;

            if (SystemInfo.usesReversedZBuffer)
            {
                // Swap the near and far clip plane distances as FSR2 expects this when using inverted depth
                (m_dispatchDescription.CameraNear, m_dispatchDescription.CameraFar) = (m_dispatchDescription.CameraFar, m_dispatchDescription.CameraNear);
            }

            if (m_previousScaleFactor != m_scaleFactor || m_previousGenerateReactiveMask != generateReactiveMask || m_previousTCMask != generateTCMask || m_displayWidth != m_mainCamera.camera.pixelWidth || m_displayHeight != m_mainCamera.camera.pixelHeight)
            {
                SetupResolution();
            }
            JitterCameraMatrix();
        }

        private void OnEndContextRendering(ScriptableRenderContext renderContext, List<Camera> cameras)
        {
            m_mainCamera.camera.ResetProjectionMatrix();
        }

        /// <summary>
        /// FSR TAA Jitter
        /// </summary>
        private void JitterCameraMatrix()
        {
            int jitterPhaseCount = Fsr2.GetJitterPhaseCount(m_renderWidth, m_displayWidth);
            Fsr2.GetJitterOffset(out float jitterX, out float jitterY, Time.frameCount, jitterPhaseCount);
            m_dispatchDescription.JitterOffset = new Vector2(jitterX, jitterY);

            jitterX = 2.0f * jitterX / (float)m_renderWidth;
            jitterY = 2.0f * jitterY / (float)m_renderHeight;
            m_jitterMatrix = Matrix4x4.Translate(new Vector2(jitterX, jitterY));
            m_projectionMatrix = m_mainCamera.camera.projectionMatrix;
            m_mainCamera.camera.nonJitteredProjectionMatrix = m_projectionMatrix;
            m_mainCamera.camera.projectionMatrix = m_jitterMatrix * m_projectionMatrix;
            m_mainCamera.camera.useJitteredProjectionMatrixForTransparentRendering = true;
        }

        /// <summary>
        /// Gets the HD Camera and sets up things related to the hd camera if the instance cahnged
        /// </summary>
        private void GetHDCamera()
        {
            HDCamera hdCamera = HDCamera.GetOrCreate(GetComponent<Camera>());

            if (hdCamera != m_mainCamera)
            {
                m_mainCamera = hdCamera;
                m_mainCamera.fsr2Enabled = true;

                //Make sure the camera allows Dynamic Resolution and VolumeMask includes the Layer of the camera.
                HDAdditionalCameraData m_mainCameraAdditional = m_mainCamera.camera.GetComponent<HDAdditionalCameraData>();
                m_mainCameraAdditional.allowDynamicResolution = true;
                m_mainCameraAdditional.stopNaNs = false;
                m_mainCameraAdditional.volumeLayerMask |= (1 << m_mainCamera.camera.gameObject.layer);
            }
        }


        /// <summary>
        /// Initializes FSR in the plugin
        /// </summary>
        private void SetupResolution()
        {
            m_previousScaleFactor = m_scaleFactor;

            m_previousGenerateReactiveMask = generateReactiveMask;
            m_previousTCMask = generateTCMask;

            m_displayWidth = m_mainCamera.camera.pixelWidth;
            m_displayHeight = m_mainCamera.camera.pixelHeight;

            m_renderWidth = (int)(m_mainCamera.camera.pixelWidth / m_scaleFactor);
            m_renderHeight = (int)(m_mainCamera.camera.pixelHeight / m_scaleFactor);

            Fsr2.InitializationFlags flags = Fsr2.InitializationFlags.EnableMotionVectorsJitterCancellation
                                           | Fsr2.InitializationFlags.EnableHighDynamicRange
                                           | Fsr2.InitializationFlags.EnableAutoExposure;
            if (enableF16)
                flags |= Fsr2.InitializationFlags.EnableFP16Usage;

            if (m_context != null)
            {
                m_context.Destroy();
                m_context = null;
            }
            m_context = Fsr2.CreateContext(new Vector2Int(m_displayWidth, m_displayHeight), new Vector2Int((int)(m_displayWidth / 1.5f), (int)(m_displayHeight / 1.5f)), Callbacks, flags);

            ClearRTs();

            if (generateReactiveMask)
            {
                m_opaqueOnlyGrabVolume.enabled = true;
                m_afterOpaqueOnlyGrabVolume.enabled = true;
                m_opaqueOnlyColorBuffer = RTHandles.Alloc(m_renderWidth, m_renderHeight, enableRandomWrite: true, dimension: TextureDimension.Tex2DArray, useDynamicScale: false, colorFormat: m_colorBufferFormat, name: "FSR OPAQUE ONLY");
                m_afterOpaqueOnlyColorBuffer = RTHandles.Alloc(m_renderWidth, m_renderHeight, enableRandomWrite: true, dimension: TextureDimension.Tex2DArray, useDynamicScale: false, colorFormat: m_colorBufferFormat, name: "FSR AFTER OPAQUE");
                m_reactiveMaskOutput = RTHandles.Alloc(m_renderWidth, m_renderHeight, enableRandomWrite: true, dimension: TextureDimension.Tex2D, useDynamicScale: false, colorFormat: m_colorBufferFormat, name: "FSR REACTIVE MASK OUTPUT");
            }
            else
            {
                m_opaqueOnlyGrabVolume.enabled = false;
                m_afterOpaqueOnlyGrabVolume.enabled = false;
            }
        }

        /// <summary>
        /// Create FSR output
        /// </summary>
        /// <param name="format"></param>
        public void CreateFSROutputRT(GraphicsFormat format)
        {
            m_colorBufferFormat = format;
            SetupResolution();
        }

        public float SetDynamicResolutionScale()
        {
            return 100 / m_scaleFactor;
        }

        private void ClearRTs()
        {
            if (m_reactiveMaskOutput != null)
            {
                m_opaqueOnlyColorBuffer.Release();
                m_afterOpaqueOnlyColorBuffer.Release();
                m_reactiveMaskOutput.Release();
            }
        }


        protected override void DisableFSR()
        {
            base.DisableFSR();
            m_previousScaleFactor = -1;

            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            RenderPipelineManager.endContextRendering -= OnEndContextRendering;

            DynamicResolutionHandler.SetDynamicResScaler(() => { return 100; }, DynamicResScalePolicyType.ReturnsPercentage);

            if (m_mainCamera != null)
            {
                m_mainCamera.fsr2Enabled = false;

                //Set main camera to null to make sure it's setup again when fsr is initialized
                m_mainCamera = null;
            }

            ClearRTs();

            if (m_postProcessVolume)
            {
                m_postProcessPass.Cleanup();
                Destroy(m_postProcessVolume);
            }

            if (m_opaqueOnlyGrabVolume)
            {
                m_postProcessPass.Cleanup();
                Destroy(m_opaqueOnlyGrabVolume);
            }
            if (m_afterOpaqueOnlyGrabVolume)
            {
                Destroy(m_afterOpaqueOnlyGrabVolume);
            }
        }
    }
}
#endif