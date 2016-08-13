using UnityEngine;

namespace UnityStandardAssets.CinematicEffects
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Image Effects/Cinematic/Temporal Anti-aliasing")]
    public class TemporalAntiAliasing : MonoBehaviour
    {
        [Range(0f, 3f)]
        public float jitterScale = 1f;

        [Range(4, 128)]
        public int haltonSequenceLength = 8;

        [Range(0f, 1f)]
        public float sharpeningAmount = 0.25f;

        [Range(0f, 2f)]
        public float sharpenFilterWidth = 1f;

        [Range(0.9f, 1f)]
        public float staticBlurAmount = 0.98f;

        [Range(0.6f, 0.9f)]
        public float motionBlurAmount = 0.8f;

        [Range(3000f, 10000f)]
        public float motionAmplificationAmount = 6000f;

        private Shader m_Shader;
        public Shader shader
        {
            get
            {
                if (m_Shader == null)
                    m_Shader = Shader.Find("Hidden/Temporal Anti-aliasing");

                return m_Shader;
            }
        }

        private Material m_Material;
        public Material material
        {
            get
            {
                if (m_Material == null)
                {
                    if (shader == null || !shader.isSupported)
                        return null;

                    m_Material = new Material(shader);
                }

                return m_Material;
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

        private Matrix4x4 m_PreviousViewProjectionMatrix;

        private RenderTexture m_History;
        private int m_SampleIndex = 0;

        private float GetCatmullRomValue(float k)
        {
            k = Mathf.Abs(k);

            if (k > 1.0f)
            {
                return ((-0.5f * k + 2.5f) * k - 4.0f) * k + 2.0f;
            }

            return (1.5f * k - 2.5f) * k * k + 1.0f;
        }

        private float GetMitchellNetravaliValue(float k, float b, float c)
        {
            k = Mathf.Abs(k);

            if (k < 1.0f)
            {
                return ((12.0f - 9.0f * b - 6.0f * c) * k * k * k +
                        (-18.0f + 12.0f * b + 6.0f * c) * k * k +
                        (6.0f - 2.0f * b)) / 6.0f;
            }
            else if ((k >= 1.0f) && (k < 2.0f))
            {
                return ((-b - 6.0f * c) * k * k * k +
                        (6.0f * b + 30.0f * c) * k * k +
                        (-12.0f * b - 48.0f * c) * k +
                        (8.0f * b + 24.0f * c)) / 6.0f;
            }

            return 0.0f;
        }

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

            if (++m_SampleIndex >= haltonSequenceLength)
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
            camera_.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
        }

        void OnDisable()
        {
            if (m_History != null)
            {
                RenderTexture.ReleaseTemporary(m_History);
                m_History = null;
            }

            camera_.depthTextureMode &= ~(DepthTextureMode.MotionVectors);
            m_SampleIndex = 0;
        }

        void OnPreCull()
        {
            Vector2 jitter = GenerateRandomOffset();
            jitter *= jitterScale;

#if UNITY_5_4_OR_NEWER
            camera_.nonJitteredProjectionMatrix = camera_.projectionMatrix;
#endif
            camera_.projectionMatrix = GetPerspectiveProjectionMatrix(jitter);

            jitter.x /= camera_.pixelWidth;
            jitter.y /= camera_.pixelHeight;

            material.SetVector("_Jitter", jitter);
        }

        public void OnPostRender()
        {
            camera_.ResetProjectionMatrix();
        }

        private void RenderFullScreenQuad()
        {
            GL.PushMatrix();
            GL.LoadOrtho();
            material.SetPass(0);

            //Render the full screen quad manually.
            GL.Begin(GL.QUADS);
            GL.TexCoord2(0.0f, 0.0f); GL.Vertex3(0.0f, 0.0f, 0.1f);
            GL.TexCoord2(1.0f, 0.0f); GL.Vertex3(1.0f, 0.0f, 0.1f);
            GL.TexCoord2(1.0f, 1.0f); GL.Vertex3(1.0f, 1.0f, 0.1f);
            GL.TexCoord2(0.0f, 1.0f); GL.Vertex3(0.0f, 1.0f, 0.1f);
            GL.End();

            GL.PopMatrix();
        }

        [ImageEffectOpaque]
        public void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (camera_.orthographic)
            {
                Graphics.Blit(source, destination);
                return;
            }
            else if (m_History == null || (m_History.width != source.width || m_History.height != source.height))
            {
                if (m_History)
                    RenderTexture.ReleaseTemporary(m_History);

                m_History = RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Default);
                m_History.hideFlags = HideFlags.HideAndDontSave;
                m_History.filterMode = FilterMode.Bilinear;

                Graphics.Blit(source, m_History);
            }

            material.SetVector("_SharpenParameters", new Vector4(sharpeningAmount, sharpenFilterWidth, 1f / source.width, 1f / source.height));
            material.SetVector("_FinalBlendParameters", new Vector4(staticBlurAmount, motionBlurAmount, motionAmplificationAmount));
            material.SetTexture("_HistoryTex", m_History);
            material.SetTexture("_MainTex", source);

            // generate a temp texture, this will become history next frame
            RenderTexture temporary = RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Default);
            temporary.filterMode = FilterMode.Bilinear;

            // destination can be null if we are writing to framebuffer
            // this means we need an extra blit. Will only happen if
            // taa is last in the effects stack
            var destinationToUse = destination;
            var needsExtraBlit = false;
            if (destinationToUse == null)
            {
                destinationToUse = RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Default);
                destinationToUse.filterMode = FilterMode.Bilinear;
                needsExtraBlit = true;
            }

            // set up MRT, we can do this all in one pass :)
            var mrt = new RenderBuffer[2];
            mrt[0] = temporary.colorBuffer;
            mrt[1] = destinationToUse.colorBuffer;
            Graphics.SetRenderTarget(mrt, destinationToUse.depthBuffer);

            // Do the render
            RenderFullScreenQuad();

            // release the old history / update with new history
            RenderTexture.ReleaseTemporary(m_History);
            m_History = temporary;

            // do the extra blit if needed
            if (needsExtraBlit)
            {
                Graphics.Blit(destinationToUse, destination);
                RenderTexture.ReleaseTemporary(destinationToUse);
            }

            RenderTexture.active = destination;
        }
    }
}
