// Cristian Pop - https://boxophobic.com/

using UnityEngine;
using UnityEditor;

namespace Boxophobic.StyledGUI
{
    public class StyledTextureDrawer : MaterialPropertyDrawer
    {
        public float size;
        public float top;
        public float down;
        public string tooltip = "";

        public StyledTextureDrawer()
        {
            this.size = 50;
            this.top = 0;
            this.down = 0;
        }

        public StyledTextureDrawer(float size)
        {
            this.size = size;
            this.top = 0;
            this.down = 0;
        }

        public StyledTextureDrawer(float size, string tooltip)
        {
            this.size = size;
            this.top = 0;
            this.down = 0;
            this.tooltip = tooltip;
        }

        public StyledTextureDrawer(float size, float top, float down)
        {
            this.size = size;
            this.top = top;
            this.down = down;
        }

        public StyledTextureDrawer(float size, string tooltip, float top, float down)
        {
            this.size = size;
            this.top = top;
            this.down = down;
            this.tooltip = tooltip;
        }

        public override void OnGUI(Rect position, MaterialProperty prop, string label, MaterialEditor materialEditor)
        {
            GUILayout.Space(top);

            EditorGUI.BeginChangeCheck();

            EditorGUI.showMixedValue = prop.hasMixedValue;

            Texture tex = null;

            if (prop.textureDimension == UnityEngine.Rendering.TextureDimension.Tex2D)
            {
                tex = (Texture2D)EditorGUILayout.ObjectField(new GUIContent(prop.displayName, tooltip), prop.textureValue, typeof(Texture2D), false, GUILayout.Height(50));
            }

            if (prop.textureDimension == UnityEngine.Rendering.TextureDimension.Cube)
            {
                tex = (Cubemap)EditorGUILayout.ObjectField(new GUIContent(prop.displayName, tooltip), prop.textureValue, typeof(Cubemap), false, GUILayout.Height(50));
            }

            EditorGUI.showMixedValue = false;

            if (EditorGUI.EndChangeCheck())
            {
                prop.textureValue = tex;
            }

            GUILayout.Space(down);
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            return -2;
        }
    }
}
