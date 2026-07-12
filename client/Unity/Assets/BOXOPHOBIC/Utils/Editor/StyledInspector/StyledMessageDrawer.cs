// Cristian Pop - https://boxophobic.com/

using UnityEngine;
using UnityEditor;

namespace Boxophobic.StyledGUI
{
    [CustomPropertyDrawer(typeof(StyledMessage))]
    public class StyledMessageAttributeDrawer : PropertyDrawer
    {
        StyledMessage a;

        bool show;
        MessageType messageType;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            show = property.boolValue;

            if (show)
            {
                a = (StyledMessage)attribute;

                if (a.type == "None")
                {
                    messageType = MessageType.None;
                }
                else if (a.type == "Info")
                {
                    messageType = MessageType.Info;
                }
                else if (a.type == "Warning")
                {
                    messageType = MessageType.Warning;
                }
                else if (a.type == "Error")
                {
                    messageType = MessageType.Error;
                }

                GUILayout.Space(a.top);
                EditorGUILayout.HelpBox(a.message, messageType);
                GUILayout.Space(a.down);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return -2;
        }
    }
}
