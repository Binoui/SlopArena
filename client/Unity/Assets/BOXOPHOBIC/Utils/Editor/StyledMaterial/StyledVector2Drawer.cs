// Cristian Pop - https://boxophobic.com/

using UnityEngine;
using UnityEditor;
using System;

namespace Boxophobic.StyledGUI
{
    public class StyledVector2Drawer : MaterialPropertyDrawer
    {
        public float space = 0;
        public float top = 0;
        public float down = 0;

        public StyledVector2Drawer()
        {
            this.space = 0;
        }

        public StyledVector2Drawer(float space)
        {
            this.space = space;
        }

        public StyledVector2Drawer(float space, float top, float down)
        {
            this.space = space;
            this.top = top;
            this.down = down;
        }

        public override void OnGUI(Rect position, MaterialProperty prop, String label, MaterialEditor materialEditor)
        {
            GUILayout.Space(top);

            if (EditorGUIUtility.currentViewWidth > 330)
            {
                DrawVectorProperty(prop, label);
                GUILayout.Space(-space);
            }
            else
            {
                DrawVectorPropertyNextLine(prop, label);
                GUILayout.Space(2);
            }

            GUILayout.Space(down);
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            return -2;
        }

        void DrawVectorProperty(MaterialProperty prop, string label)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = prop.hasMixedValue;

            GUILayout.BeginHorizontal();
            GUILayout.Space(-1);
            GUILayout.Label(label, GUILayout.Width(EditorGUIUtility.labelWidth - 1));
            Vector4 vec = EditorGUILayout.Vector2Field("", prop.vectorValue);
            GUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                prop.vectorValue = vec;
            }
        }

        void DrawVectorPropertyNextLine(MaterialProperty prop, string label)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = prop.hasMixedValue;

            GUILayout.BeginHorizontal();
            GUILayout.Space(-1);
            GUILayout.Label(label, GUILayout.Width(EditorGUIUtility.labelWidth));
            GUILayout.EndHorizontal();

            Vector4 vec = EditorGUILayout.Vector2Field("", prop.vectorValue);

            if (EditorGUI.EndChangeCheck())
            {
                prop.vectorValue = vec;
            }
        }
    }
}
