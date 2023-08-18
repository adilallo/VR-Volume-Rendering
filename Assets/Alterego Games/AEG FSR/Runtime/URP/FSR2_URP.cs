#if UNITY_URP
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FidelityFX;

namespace AEG.FSR
{
    [RequireComponent(typeof(Camera))]
    public class FSR2_URP : FSR2_BASE
    {
        //Rendertextures
        private RTHandleSystem RTHandleS;
        public RTHandle m_opaqueOnlyColorBuffer;
        public RTHandle m_afterOpaqueOnlyColorBuffer;
        public RTHandle m_reactiveMaskOutput;
        //public RTHandle m_colorBuffer;
        public RTHandle m_fsrOutput;
        public bool m_autoHDR;

        private FSRScriptableRenderFeature fsrScriptableRenderFeature;
        private bool containsRenderFeature = false;

        private Matrix4x4 m_jitterMatrix;
        private Matrix4x4 m_projectionMatrix;

        //UniversalRenderPipelineAsset
        private UniversalRenderPipelineAsset UniversalRenderPipelineAsset;
        private UniversalAdditionalCameraData m_cameraData;

        public GraphicsFormat m_graphicsFormat;
        public readonly Fsr2.DispatchDescription m_dispatchDescription = new Fsr2.DispatchDescription();
        public readonly Fsr2.GenerateReactiveDescription m_genReactiveDescription = new Fsr2.GenerateReactiveDescription();
        private IFsr2Callbacks Callbacks { get; set; } = new Fsr2CallbacksBase();
        public Fsr2Context m_context;

        public bool m_cameraStacking = false;
        public Camera m_topCamera;
        private int m_prevCameraStackCount;
        private bool m_isBaseCamera;
        private List<FSR2_URP> m_prevCameraStack = new List<FSR2_URP>();
        private FSR_Quality m_prevStackQuality = (FSR_Quality)(-1);

        protected override void InitializeFSR() {
            base.InitializeFSR();
            m_mainCamera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;

            SetupResolution();

            if(!m_fsrInitialized) {
                RenderPipelineManager.beginCameraRendering += PreRenderCamera;
                RenderPipelineManager.endCameraRendering += PostRenderCamera;
            }

            if(m_cameraData == null) {
                m_cameraData = m_mainCamera.GetUniversalAdditionalCameraData();
                if(m_cameraData != null) {
                    if(m_cameraData.renderType == CameraRenderType.Base) {
                        m_isBaseCamera = true;
                        SetupCameraStacking();
                    }
                }
            }
        }

        /// <summary>
        /// Sets up the buffers, initializes the fsr context, and sets up the command buffer
        /// Must be recalled whenever the display resolution changes
        /// </summary>
        private void SetupCommandBuffer() {
            if(m_fsrOutput != null) {
                m_fsrOutput.Release();
                if(generateReactiveMask) {
                    m_opaqueOnlyColorBuffer.Release();
                    m_afterOpaqueOnlyColorBuffer.Release();
                    m_reactiveMaskOutput.Release();
                }
            }

            if(fsrScriptableRenderFeature != null) {
                fsrScriptableRenderFeature.OnDispose();
            } else {
                containsRenderFeature = GetRenderFeature();
            }
            SetDynamicResolution(m_scaleFactor);

            m_renderWidth = (int)(m_mainCamera.pixelWidth / m_scaleFactor);
            m_renderHeight = (int)(m_mainCamera.pixelHeight / m_scaleFactor);

            m_fsrOutput = RTHandleS.Alloc(m_mainCamera.pixelWidth, m_mainCamera.pixelHeight, enableRandomWrite: true, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, msaaSamples: MSAASamples.None, name: "FSR OUTPUT");

            m_dispatchDescription.InputResourceSize = new Vector2Int(m_renderWidth, m_renderHeight);
            m_dispatchDescription.Output = m_fsrOutput;

            if(generateReactiveMask) {
                m_opaqueOnlyColorBuffer = RTHandleS.Alloc(m_renderWidth, m_renderHeight, enableRandomWrite: false, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, msaaSamples: MSAASamples.None, name: "OPAQUE ONLY BUFFER");
                m_afterOpaqueOnlyColorBuffer = RTHandleS.Alloc(m_renderWidth, m_renderHeight, enableRandomWrite: false, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, msaaSamples: MSAASamples.None, name: "AFTER OPAQUE ONLY BUFFER");
                m_reactiveMaskOutput = RTHandleS.Alloc(m_renderWidth, m_renderHeight, enableRandomWrite: true, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, msaaSamples: MSAASamples.None, name: "FSR REACTIVE MASK OUTPUT");
            }

            if(!containsRenderFeature) {
                Debug.LogError("Current Universal Render Data is missing the 'FSR Scriptable Render Pass URP' Rendering Feature");
            } else {
                fsrScriptableRenderFeature.OnSetReference(this);
            }

            fsrScriptableRenderFeature.IsEnabled = true;
        }


        private bool GetRenderFeature() {
            UniversalRenderPipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

            if(UniversalRenderPipelineAsset != null) {
                UniversalRenderPipelineAsset.upscalingFilter = UpscalingFilterSelection.Linear;
                UniversalRenderPipelineAsset.msaaSampleCount = (int)MsaaQuality.Disabled;

                var type = UniversalRenderPipelineAsset.GetType();
                var propertyInfo = type.GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);

                if(propertyInfo != null) {
                    var scriptableRenderData = (ScriptableRendererData[])propertyInfo.GetValue(UniversalRenderPipelineAsset);

                    if(scriptableRenderData != null && scriptableRenderData.Length > 0) {
                        foreach(var renderData in scriptableRenderData) {

                            foreach(var rendererFeature in renderData.rendererFeatures) {
                                fsrScriptableRenderFeature = rendererFeature as FSRScriptableRenderFeature;


                                if(fsrScriptableRenderFeature != null) {
                                    return true;
                                    //Todo give error when RenderFeature is disabled
                                }
                            }
                        }
                    }
                }
            } else {
                Debug.LogError("FSR 2: Can't find UniversalRenderPipelineAsset");
            }
            return false;
        }

        void PreRenderCamera(ScriptableRenderContext context, Camera cameras) {

            if(cameras != m_mainCamera) {
                return;
            }

            // Set up the parameters to auto-generate a reactive mask
            if(generateReactiveMask) {
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
            m_dispatchDescription.CameraNear = m_mainCamera.nearClipPlane;
            m_dispatchDescription.CameraFar = m_mainCamera.farClipPlane;
            m_dispatchDescription.CameraFovAngleVertical = m_mainCamera.fieldOfView * Mathf.Deg2Rad;
            m_dispatchDescription.ViewSpaceToMetersFactor = 1.0f;
            m_dispatchDescription.Reset = m_resetCamera;
            m_resetCamera = false;

            if(SystemInfo.usesReversedZBuffer) {
                // Swap the near and far clip plane distances as FSR2 expects this when using inverted depth
                (m_dispatchDescription.CameraNear, m_dispatchDescription.CameraFar) = (m_dispatchDescription.CameraFar, m_dispatchDescription.CameraNear);
            }

            JitterCameraMatrix();

            if(UniversalRenderPipelineAsset != GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset) {
                fsrScriptableRenderFeature.OnDispose();
                fsrScriptableRenderFeature = null;
                OnSetQuality(FSRQuality);
                SetupCommandBuffer();
            }



            //Check if display resolution has changed
            if(m_displayWidth != m_mainCamera.pixelWidth || m_displayHeight != m_mainCamera.pixelHeight || m_previousHDR != m_autoHDR) {
                SetupResolution();
            }

            if(m_previousScaleFactor != m_scaleFactor || m_previousReactiveMask != generateReactiveMask || m_previousTCMask != generateTCMask || m_previousRenderingPath != m_mainCamera.actualRenderingPath) {
                SetupFrameBuffers();
            }

            //Camera Stacking
            if(m_isBaseCamera) {
                if(m_cameraData != null) {
                    if(m_cameraStacking) {
                        try {
                            if(m_topCamera != m_cameraData.cameraStack[m_cameraData.cameraStack.Count - 1] || m_prevCameraStackCount != m_cameraData.cameraStack.Count || m_prevStackQuality != FSRQuality) {
                                SetupCameraStacking();
                            }
                        }
                        catch { }
                    }
                }
            }
        }

        void PostRenderCamera(ScriptableRenderContext context, Camera cameras) {
            if(cameras != m_mainCamera) {
                return;
            }

            m_mainCamera.ResetProjectionMatrix();
        }

        /// <summary>
        /// FSR TAA Jitter
        /// </summary>
        private void JitterCameraMatrix() {
            if(fsrScriptableRenderFeature == null) {
                return;
            } else if(!fsrScriptableRenderFeature.IsEnabled) {
                return;
            }


            int jitterPhaseCount = Fsr2.GetJitterPhaseCount(m_renderWidth, (int)(m_renderWidth * m_scaleFactor));
            Fsr2.GetJitterOffset(out float jitterX, out float jitterY, Time.frameCount, jitterPhaseCount);
            m_dispatchDescription.JitterOffset = new Vector2(jitterX, jitterY);

            jitterX = 2.0f * jitterX / (float)m_renderWidth;
            jitterY = 2.0f * jitterY / (float)m_renderHeight;
            m_jitterMatrix = Matrix4x4.Translate(new Vector2(jitterX, jitterY));
            m_projectionMatrix = m_mainCamera.projectionMatrix;
            m_mainCamera.nonJitteredProjectionMatrix = m_projectionMatrix;
            m_mainCamera.projectionMatrix = m_jitterMatrix * m_projectionMatrix;
            m_mainCamera.useJitteredProjectionMatrixForTransparentRendering = true;
        }

        /// <summary>
        /// Handle Dynamic Scaling
        /// </summary>
        /// <param name="_value"></param>
        public void SetDynamicResolution(float _value) {
            if(UniversalRenderPipelineAsset != null) {
                UniversalRenderPipelineAsset.renderScale = 1 / _value;
            }
        }

        /// <summary>
        /// Creates new buffers and sends them to the plugin
        /// </summary>
        private void SetupFrameBuffers() {
            m_previousScaleFactor = m_scaleFactor;
            m_previousReactiveMask = generateReactiveMask;

            SetupCommandBuffer();

            m_previousRenderingPath = m_mainCamera.actualRenderingPath;
        }

        /// <summary>
        /// Creates new buffers, sends them to the plugin, and reintilized FSR to adjust the display size
        /// </summary>
        private void SetupResolution() {
            m_displayWidth = m_mainCamera.pixelWidth;
            m_displayHeight = m_mainCamera.pixelHeight;
            m_previousHDR = m_autoHDR;

            RTHandleS = new RTHandleSystem();
            RTHandleS.Initialize(m_mainCamera.pixelWidth, m_mainCamera.pixelHeight);

            Fsr2.InitializationFlags flags = Fsr2.InitializationFlags.EnableMotionVectorsJitterCancellation
                                           | Fsr2.InitializationFlags.EnableHighDynamicRange
                                           | Fsr2.InitializationFlags.EnableAutoExposure;
            if(enableF16)
                flags |= Fsr2.InitializationFlags.EnableFP16Usage;

            if(m_context != null) {
                m_context.Destroy();
                m_context = null;
            }
            m_context = Fsr2.CreateContext(new Vector2Int(m_displayWidth, m_displayHeight), new Vector2Int((int)(m_displayWidth / 1.5f), (int)(m_displayHeight / 1.5f)), Callbacks, flags);

            SetupFrameBuffers();
        }

        /// <summary>
        /// Automatically Setup camera stacking
        /// </summary>
        private void SetupCameraStacking() {
            m_prevCameraStackCount = m_cameraData.cameraStack.Count;
            if(m_cameraData.renderType == CameraRenderType.Base) {
                m_isBaseCamera = true;

                m_cameraStacking = m_cameraData.cameraStack.Count > 0;
                if(m_cameraStacking) {
                    CleanupOverlayCameras();
                    m_prevStackQuality = FSRQuality;

                    m_topCamera = m_cameraData.cameraStack[m_cameraData.cameraStack.Count - 1];

                    for(int i = 0; i < m_cameraData.cameraStack.Count; i++) {
                        FSR2_URP stackedCamera = m_cameraData.cameraStack[i].gameObject.GetComponent<FSR2_URP>();
                        if(stackedCamera == null) {
                            stackedCamera = m_cameraData.cameraStack[i].gameObject.AddComponent<FSR2_URP>();
                        }
                        m_prevCameraStack.Add(m_cameraData.cameraStack[i].gameObject.GetComponent<FSR2_URP>());

                        //stackedCamera.hideFlags = HideFlags.HideInInspector;
                        stackedCamera.m_cameraStacking = true;
                        stackedCamera.m_topCamera = m_topCamera;

                        stackedCamera.OnSetQuality(FSRQuality);

                        stackedCamera.sharpening = sharpening;
                        stackedCamera.sharpness = sharpness;
                        stackedCamera.generateReactiveMask = generateReactiveMask;
                        stackedCamera.autoReactiveScale = autoReactiveScale;
                        stackedCamera.autoReactiveThreshold = autoReactiveThreshold;
                        stackedCamera.autoReactiveBinaryValue = autoReactiveBinaryValue;
                    }
                }
            }
        }

        private void CleanupOverlayCameras() {
            for(int i = 0; i < m_prevCameraStack.Count; i++) {
                if(!m_prevCameraStack[i].m_isBaseCamera)
                    DestroyImmediate(m_prevCameraStack[i]);
            }
            m_prevCameraStack = new List<FSR2_URP>();
        }

        protected override void DisableFSR() {
            base.DisableFSR();

            RenderPipelineManager.beginCameraRendering -= PreRenderCamera;
            RenderPipelineManager.endCameraRendering -= PostRenderCamera;

            SetDynamicResolution(1);
            if(fsrScriptableRenderFeature != null) {
                fsrScriptableRenderFeature.IsEnabled = false;
            }
            CleanupOverlayCameras();
            m_previousScaleFactor = -1;
            m_prevStackQuality = (FSR_Quality)(-1);

            if(m_fsrOutput != null) {
                m_fsrOutput.Release();

                if(generateReactiveMask) {
                    m_opaqueOnlyColorBuffer.Release();
                    m_afterOpaqueOnlyColorBuffer.Release();
                    m_reactiveMaskOutput.Release();
                }
            }

            if(m_context != null) {
                m_context.Destroy();
                m_context = null;
            }
        }
    }
}



#endif