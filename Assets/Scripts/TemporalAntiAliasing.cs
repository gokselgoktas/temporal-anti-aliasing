using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityStandardAssets.CinematicEffects
{
    [ExecuteInEditMode]
#if UNITY_5_4_OR_NEWER
    [ImageEffectAllowedInSceneView]
#endif
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Image Effects/Cinematic/Temporal Anti-aliasing")]
    public class TemporalAntiAliasing : MonoBehaviour
    {
        public enum Sequence
        {
            Halton
        }

        [Serializable]
        public struct JitterSettings
        {
            [Tooltip("The sequence used to generate the points used as jitter offsets.")]
            public Sequence sequence;

            [Tooltip("The diameter (in texels) inside which jitter samples are spread. Smaller values result in crisper but more aliased output, while larger values result in more stable but blurrier output.")]
            [Range(0.1f, 3f)]
            public float spread;

            [Tooltip("Number of temporal samples. A larger value results in a smoother image but takes longer to converge; whereas a smaller value converges fast but allows for less subpixel information.")]
            [Range(4, 64)]
            public int sampleCount;
        }

        [Serializable]
        public struct SharpenFilterSettings
        {
            [Tooltip("Controls the amount of sharpening applied to the color buffer.")]
            [Range(0f, 3f)]
            public float amount;
        }

        [Serializable]
        public struct BlendSettings
        {
            [Tooltip("The blend coefficient for a stationary fragment. Controls the percentage of history sample blended into the final color.")]
            [Range(0f, 1f)]
            public float stationary;

            [Tooltip("The blend coefficient for a fragment with significant motion. Controls the percentage of history sample blended into the final color.")]
            [Range(0f, 1f)]
            public float moving;

            [Tooltip("Amount of motion amplification in percentage. A higher value will make the final blend more sensitive to smaller motion, but might result in more aliased output; while a smaller value might desensitivize the algorithm resulting in a blurry output.")]
            [Range(30f, 100f)]
            public float motionAmplification;
        }

        [Serializable]
        public class Settings
        {
            [AttributeUsage(AttributeTargets.Field)]
            public class LayoutAttribute : PropertyAttribute
            {
            }

            [Layout]
            public JitterSettings jitterSettings;

            [Layout]
            public SharpenFilterSettings sharpenFilterSettings;

            [Layout]
            public BlendSettings blendSettings;

            private static readonly Settings m_Default = new Settings
            {
                jitterSettings = new JitterSettings
                {
                    sequence = Sequence.Halton,
                    spread = 1f,
                    sampleCount = 8
                },

                sharpenFilterSettings = new SharpenFilterSettings
                {
                    amount = 0.25f
                },

                blendSettings = new BlendSettings
                {
                    stationary = 0.98f,
                    moving = 0.8f,

                    motionAmplification = 60f
                }
            };

            public static Settings defaultSettings
            {
                get
                {
                    return m_Default;
                }
            }
        }

        [SerializeField]
        public Settings settings = Settings.defaultSettings;

        private Shader m_EffectShader;
        public Shader effectShader
        {
            get
            {
                if (m_EffectShader == null)
                    m_EffectShader = Shader.Find("Hidden/Temporal Anti-aliasing");

                return m_EffectShader;
            }
        }

        private Material m_EffectMaterial;
        public Material effectMaterial
        {
            get
            {
                if (m_EffectMaterial == null)
                {
                    if (effectShader == null || !effectShader.isSupported)
                        return null;

                    m_EffectMaterial = new Material(effectShader);
                }

                return m_EffectMaterial;
            }
        }

        private Shader m_BlitShader;
        private Shader blitShader
        {
            get
            {
                if (m_BlitShader == null)
                    m_BlitShader = Shader.Find("Hidden/MRT Blit");

                return m_BlitShader;
            }
        }

        private Material m_BlitMaterial;
        private Material blitMaterial
        {
            get
            {
                if (m_BlitMaterial == null)
                {
                    if (blitShader == null || !blitShader.isSupported)
                        return null;

                    m_BlitMaterial = new Material(blitShader);
                }

                return m_BlitMaterial;
            }
        }

        private Camera m_Camera;
        public Camera camera_
        {
            get
            {
                if (m_Camera == null)
                    m_Camera = GetComponent<Camera>();

                return m_Camera;
            }
        }

        private Mesh m_Quad;
        private Mesh quad
        {
            get
            {
                if (m_Quad == null)
                {
                    Vector3[] vertices = new Vector3[4]
                    {
                        new Vector3(1.0f, 1.0f, 0.0f),
                        new Vector3(-1.0f, 1.0f, 0.0f),
                        new Vector3(-1.0f, -1.0f, 0.0f),
                        new Vector3(1.0f, -1.0f, 0.0f),
                    };

                    int[] indices = new int[6] { 0, 1, 2, 2, 3, 0 };

                    m_Quad = new Mesh();
                    m_Quad.vertices = vertices;
                    m_Quad.triangles = indices;
                }

                return m_Quad;
            }
        }

        private CommandBuffer m_CommandBuffer;
        private CommandBuffer commandBuffer
        {
            get
            {
                if (m_CommandBuffer == null)
                {
                    m_CommandBuffer = new CommandBuffer();
                    m_CommandBuffer.name = "Temporal Anti-aliasing";
                }

                return m_CommandBuffer;
            }
        }

        static private int kTemporaryTexture;

        private RenderTexture m_History;
        private RenderTargetIdentifier m_HistoryIdentifier;

        private RenderTextureFormat m_IntermediateFormat;

        private bool m_IsFirstFrame = true;
        private int m_SampleIndex = 0;

        private float GetHaltonValue(int index, int radix)
        {
            float result = 0.0f;
            float fraction = 1.0f / (float)radix;

            while (index > 0)
            {
                result += (float)(index % radix) * fraction;

                index /= radix;
                fraction /= (float)radix;
            }

            return result;
        }

        private Vector2 GenerateRandomOffset()
        {
            Vector2 offset = new Vector2(
                    GetHaltonValue(m_SampleIndex & 1023, 2),
                    GetHaltonValue(m_SampleIndex & 1023, 3));

            if (++m_SampleIndex >= settings.jitterSettings.sampleCount)
                m_SampleIndex = 0;

            return offset;
        }

        // Adapted heavily from PlayDead's TAA code
        // https://github.com/playdeadgames/temporal/blob/master/Assets/Scripts/Extensions.cs
        private Matrix4x4 GetPerspectiveProjectionMatrix(Vector2 offset)
        {
            float vertical = Mathf.Tan(0.5f * Mathf.Deg2Rad * camera_.fieldOfView);
            float horizontal = vertical * camera_.aspect;

            offset.x *= horizontal / (0.5f * camera_.pixelWidth);
            offset.y *= vertical / (0.5f * camera_.pixelHeight);

            float left = (offset.x - horizontal) * camera_.nearClipPlane;
            float right = (offset.x + horizontal) * camera_.nearClipPlane;
            float top = (offset.y + vertical) * camera_.nearClipPlane;
            float bottom = (offset.y - vertical) * camera_.nearClipPlane;

            Matrix4x4 matrix = new Matrix4x4();

            matrix[0, 0] = (2.0f * camera_.nearClipPlane) / (right - left);
            matrix[0, 1] = 0.0f;
            matrix[0, 2] = (right + left) / (right - left);
            matrix[0, 3] = 0.0f;

            matrix[1, 0] = 0.0f;
            matrix[1, 1] = (2.0f * camera_.nearClipPlane) / (top - bottom);
            matrix[1, 2] = (top + bottom) / (top - bottom);
            matrix[1, 3] = 0.0f;

            matrix[2, 0] = 0.0f;
            matrix[2, 1] = 0.0f;
            matrix[2, 2] = -(camera_.farClipPlane + camera_.nearClipPlane) / (camera_.farClipPlane - camera_.nearClipPlane);
            matrix[2, 3] = -(2.0f * camera_.farClipPlane * camera_.nearClipPlane) / (camera_.farClipPlane - camera_.nearClipPlane);

            matrix[3, 0] = 0.0f;
            matrix[3, 1] = 0.0f;
            matrix[3, 2] = -1.0f;
            matrix[3, 3] = 0.0f;

            return matrix;
        }

        void OnEnable()
        {
#if !UNITY_5_4_OR_NEWER
            enabled = false;
#endif
            camera_.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;

            m_IntermediateFormat = camera_.hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;

            kTemporaryTexture = Shader.PropertyToID("_BlitSourceTex");

            m_IsFirstFrame = true;
        }

        void OnDisable()
        {
            if (m_History != null)
            {
                RenderTexture.ReleaseTemporary(m_History);
                m_History = null;
                m_HistoryIdentifier = 0;
            }

            if (camera_ != null)
            {
                if (m_CommandBuffer != null)
                {
                    camera_.RemoveCommandBuffer(CameraEvent.AfterImageEffectsOpaque, m_CommandBuffer);
                    m_CommandBuffer = null;
                }
            }

            camera_.depthTextureMode &= ~(DepthTextureMode.MotionVectors);
            m_SampleIndex = 0;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (camera_ != null)
            {
                if (m_CommandBuffer != null)
                {
                    camera_.RemoveCommandBuffer(CameraEvent.AfterImageEffectsOpaque, m_CommandBuffer);
                    m_CommandBuffer = null;
                }
            }
        }
#endif
        void OnPreCull()
        {
            if (camera_.orthographic)
            {
                enabled = false;
                return;
            }

            Vector2 jitter = GenerateRandomOffset();
            jitter *= settings.jitterSettings.spread;

#if UNITY_5_4_OR_NEWER
            camera_.nonJitteredProjectionMatrix = camera_.projectionMatrix;
#endif
            camera_.projectionMatrix = GetPerspectiveProjectionMatrix(jitter);

            jitter.x /= camera_.pixelWidth;
            jitter.y /= camera_.pixelHeight;

            effectMaterial.SetVector("_Jitter", jitter);
        }

        void OnPreRender()
        {
            if (m_History == null || (m_History.width != camera_.pixelWidth || m_History.height != camera_.pixelHeight))
            {
                if (m_History)
                    RenderTexture.ReleaseTemporary(m_History);

                m_History = RenderTexture.GetTemporary(camera_.pixelWidth, camera_.pixelHeight, 0, m_IntermediateFormat, RenderTextureReadWrite.Default);
                m_History.filterMode = FilterMode.Bilinear;

                m_History.hideFlags = HideFlags.HideAndDontSave;

                m_HistoryIdentifier = new RenderTargetIdentifier(m_History);

                m_IsFirstFrame = true;
            }

            effectMaterial.SetVector("_SharpenParameters", new Vector4(settings.sharpenFilterSettings.amount, 0f, 0f, 0f));
            effectMaterial.SetVector("_FinalBlendParameters", new Vector4(settings.blendSettings.stationary, settings.blendSettings.moving, 100f * settings.blendSettings.motionAmplification, 0f));

            camera_.RemoveCommandBuffer(CameraEvent.AfterImageEffectsOpaque, commandBuffer);
            commandBuffer.Clear();

            if (m_IsFirstFrame)
            {
                commandBuffer.Blit(BuiltinRenderTextureType.CameraTarget, m_HistoryIdentifier);
                m_IsFirstFrame = false;
            }

            commandBuffer.GetTemporaryRT(kTemporaryTexture, camera_.pixelWidth, camera_.pixelHeight, 0, FilterMode.Bilinear, m_IntermediateFormat);

            commandBuffer.SetGlobalTexture("_HistoryTex", m_HistoryIdentifier);
            commandBuffer.Blit(BuiltinRenderTextureType.CameraTarget, kTemporaryTexture, effectMaterial, 0);

            var renderTargets = new RenderTargetIdentifier[2];
            renderTargets[0] = BuiltinRenderTextureType.CameraTarget;
            renderTargets[1] = m_HistoryIdentifier;

            commandBuffer.SetRenderTarget(renderTargets, BuiltinRenderTextureType.CameraTarget);
            commandBuffer.DrawMesh(quad, Matrix4x4.identity, blitMaterial, 0, 0);

            commandBuffer.ReleaseTemporaryRT(kTemporaryTexture);

            camera_.AddCommandBuffer(CameraEvent.AfterImageEffectsOpaque, commandBuffer);
        }

        public void OnPostRender()
        {
            camera_.ResetProjectionMatrix();
        }

        [ImageEffectOpaque]
        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            Graphics.Blit(source, destination);
        }
    }
}
