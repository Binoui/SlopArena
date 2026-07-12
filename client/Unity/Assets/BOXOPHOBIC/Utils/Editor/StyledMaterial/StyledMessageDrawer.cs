// Cristian Pop - https://boxophobic.com/

using UnityEngine;
using UnityEditor;
using System;
using Boxophobic.Utility;

namespace Boxophobic.StyledGUI
{
    public class StyledMessageDrawer : MaterialPropertyDrawer
    {
        public string type;
        public string message;
        public string messageLong;
        public string keyword;
        public float value;
        public float top;
        public float down;

        public bool useMessageLong;

        MessageType messageType;

        public StyledMessageDrawer(string type, string message)
        {
            this.type = type;
            this.message = message;
            this.messageLong = "";
            keyword = null;

            this.top = 0;
            this.down = 0;
        }

        public StyledMessageDrawer(string type, string message, float top, float down)
        {
            this.type = type;
            this.message = message;
            this.messageLong = "";
            keyword = null;

            this.top = top;
            this.down = down;
        }

        public StyledMessageDrawer(string type, string message, string keyword, float value, float top, float down)
        {
            this.type = type;
            this.message = message;
            this.messageLong = "";
            this.keyword = keyword;
            this.value = value;

            this.top = top;
            this.down = down;
        }

        public StyledMessageDrawer(string type, string message, string messageLong)
        {
            this.type = type;
            this.message = message;
            this.messageLong = messageLong;
            keyword = null;

            this.top = 0;
            this.down = 0;
        }

        public StyledMessageDrawer(string type, string message, string messageLong, float top, float down)
        {
            this.type = type;
            this.message = message;
            this.messageLong = messageLong;
            keyword = null;

            this.top = top;
            this.down = down;
        }

        public StyledMessageDrawer(string type, string message, string messageLong, string keyword, float value, float top, float down)
        {
            this.type = type;
            this.message = message;
            this.messageLong = messageLong;
            this.keyword = keyword;
            this.value = value;

            this.top = top;
            this.down = down;
        }

        public override void OnGUI(Rect position, MaterialProperty prop, String label, MaterialEditor materialEditor)
        {
            Material material = materialEditor.target as Material;

            if (type == "None")
            {
                messageType = MessageType.None;
            }
            else if (type == "Info")
            {
                messageType = MessageType.Info;
            }
            else if (type == "Warning")
            {
                messageType = MessageType.Warning;
            }
            else if (type == "Error")
            {
                messageType = MessageType.Error;
            }

            if (keyword != null)
            {
                if (material.HasProperty(keyword))
                {
                    if (material.GetFloat(keyword) == value)
                    {
                        GUILayout.Space(top);
                        DrawMessage(position, prop);
                        GUILayout.Space(down);
                    }
                }
            }
            else
            {
                GUILayout.Space(top);
                DrawMessage(position, prop);
                GUILayout.Space(down);
            }
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            return -2;
        }

        void DrawMessage(Rect position, MaterialProperty prop)
        {
            if (messageLong == "")
            {
                message = BoxoUtils.FormatMessage(message);

                EditorGUILayout.HelpBox(message, messageType);
            }
            else
            {
                if (!useMessageLong)
                {
                    message = BoxoUtils.FormatMessage(message);

                    EditorGUILayout.HelpBox(message, messageType);
                }
                else
                {
                    messageLong = BoxoUtils.FormatMessage(messageLong);

                    EditorGUILayout.HelpBox(messageLong, messageType);
                }

                var lastRect = GUILayoutUtility.GetLastRect();

                if (GUI.Button(lastRect, GUIContent.none, GUIStyle.none))
                {
                    useMessageLong = !useMessageLong;
                }
            }
        }
    }
}
