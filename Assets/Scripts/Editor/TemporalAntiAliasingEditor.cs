using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

using UnityEngine;
using UnityEditor;

namespace UnityStandardAssets.CinematicEffects
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(TemporalAntiAliasing))]
    public class TemporalAntiAliasingEditor : Editor
    {
        #if UNITY_5_4_OR_NEWER

        [CustomPropertyDrawer(typeof(TemporalAntiAliasing.Settings.LayoutAttribute))]
        public class LayoutDrawer : PropertyDrawer
        {
            private bool GenerateHeader(Rect position, String title, bool display, GUIStyle style)
            {
                position.height = EditorGUIUtility.singleLineHeight;

                GUI.Box(position, title, style);

                Rect toggleArea = new Rect(position.x + 4f, position.y + 2f, 13f, 13f);

                if (Event.current.type == EventType.Repaint)
                    EditorStyles.foldout.Draw(toggleArea, false, false, display, false);

                Event e = Event.current;

                if (e.type == EventType.MouseDown && position.Contains(e.mousePosition))
                {
                    display = !display;
                    e.Use();
                }

                return display;
            }

            protected void DrawProperty(Rect position, SerializedProperty property, GUIContent label, GUIStyle style)
            {
                position.height = EditorGUIUtility.singleLineHeight;
                property.isExpanded = GenerateHeader(position, property.displayName, property.isExpanded, style);

                position.y += 22f;

                if (!property.isExpanded)
                    return;

                foreach (SerializedProperty child in property)
                {
                    EditorGUI.PropertyField(position, child);
                    position.y += EditorGUIUtility.singleLineHeight;
                }
            }

            public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            {
                if (!property.isExpanded)
                    return 22f;

                var count = property.CountInProperty();
                return EditorGUIUtility.singleLineHeight * count  + 15f;
            }

            public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
            {
                GUIStyle style = "ShurikenModuleTitle";
                style.font = (new GUIStyle("Label")).font;
                style.fontSize = (new GUIStyle("Label")).fontSize;
                style.border = new RectOffset(15, 7, 4, 4);
                style.fixedHeight = 22f;
                style.contentOffset = new Vector2(20f, -2f);

                DrawProperty(position, property, label, style);
            }
        }

        [CustomPropertyDrawer(typeof(TemporalAntiAliasing.Settings.AuxiliaryLayoutAttribute))]
        public class AuxiliaryLayoutDrawer : LayoutDrawer
        {
            public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
            {
                GUIStyle style = "ShurikenModuleTitle";
                style.font = (new GUIStyle("Label")).font;
                style.fontSize = (new GUIStyle("Label")).fontSize;
                style.border = new RectOffset(15, 7, 4, 4);
                style.fixedHeight = 22f;
                style.contentOffset = new Vector2(20f, -2f);

                GUI.Box(position, (Texture) null);
                DrawProperty(position, property, label, style);

                GUI.backgroundColor = Color.white;
            }
        }

        [NonSerialized]
        private List<SerializedProperty> m_Properties = new List<SerializedProperty>();

        public void OnEnable()
        {
            var settings = FieldFinder<TemporalAntiAliasing>.GetField(x => x.settings);
            foreach (var setting in settings.FieldType.GetFields())
            {
                var property = settings.Name + "." + setting.Name;
                m_Properties.Add(serializedObject.FindProperty(property));
            }
        }

        public override void OnInspectorGUI()
        {
            var temporalAntiAliasing = target as TemporalAntiAliasing;

            if (temporalAntiAliasing != null)
            {
                var camera = temporalAntiAliasing.GetComponent<Camera>();

                if (camera != null && camera.orthographic)
                {
                    EditorGUILayout.HelpBox("This effect does not work with Orthographic camera, will not execute.", MessageType.Warning);
                    return;
                }
            }

            serializedObject.Update();

            if (m_Properties.Count >= 1)
                EditorGUILayout.Space();

            foreach (var property in m_Properties)
                EditorGUILayout.PropertyField(property);

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
