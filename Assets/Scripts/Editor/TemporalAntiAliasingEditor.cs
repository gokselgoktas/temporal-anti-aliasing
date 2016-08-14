using UnityEngine;
using UnityEditor;

namespace UnityStandardAssets.CinematicEffects
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(TemporalAntiAliasing))]
    public class TemporalAntiAliasingEditor : Editor
    {
        #if UNITY_5_4_OR_NEWER

        private SerializedProperty m_JitterScale;
        private SerializedProperty m_SharpeningAmount;

        private SerializedProperty m_StaticBlurAmount;
        private SerializedProperty m_MotionBlurAmount;

        public void OnEnable()
        {
            m_JitterScale = serializedObject.FindProperty("jitterScale");

            m_SharpeningAmount = serializedObject.FindProperty("sharpeningAmount");

            m_StaticBlurAmount = serializedObject.FindProperty("staticBlurAmount");
            m_MotionBlurAmount = serializedObject.FindProperty("motionBlurAmount");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_JitterScale);
            EditorGUILayout.PropertyField(m_SharpeningAmount);
            EditorGUILayout.PropertyField(m_StaticBlurAmount);
            EditorGUILayout.PropertyField(m_MotionBlurAmount);

            serializedObject.ApplyModifiedProperties();
        }

        #else

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("This effect requires Unity 5.4 or later.", MessageType.Error);
        }

        #endif
    }
}
