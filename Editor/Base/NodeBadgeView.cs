using System;
using Lattice.Utils;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor.Views
{
    public class NodeBadgeView : IconBadge
    {
        private Label label;
        private Texture icon;
        private Color color;
        private bool isCustom;

        public NodeBadgeView(string message, NodeMessageType messageType)
        {
            switch (messageType)
            {
                case NodeMessageType.Warning:
                    CreateCustom(message, EditorGUIUtility.IconContent("Collab.Warning").image, Color.yellow);
                    break;
                case NodeMessageType.Error:
                    CreateCustom(message, EditorGUIUtility.IconContent("Collab.Warning").image, Color.red);
                    break;
                case NodeMessageType.Info:
                    CreateCustom(message, EditorGUIUtility.IconContent("console.infoicon").image, Color.white);
                    break;
                default:
                case NodeMessageType.None:
                    CreateCustom(message, null, Color.grey);
                    break;
            }
        }

        public NodeBadgeView(string message, Texture icon, Color color)
        {
            CreateCustom(message, icon, color);
        }

        private void CreateCustom(string message, Texture icon, Color color)
        {
            badgeText = message;
            this.color = color;

            var image = this.Q<Image>("icon");
            image.image = icon;
            image.style.backgroundColor = color;
            style.color = color;
            // This will set a class name containing the hash code of the string
            // We use this little trick to retrieve the label once it is added to the graph
            visualStyle = badgeText.GetHashCode().ToString();
        }

        // [Obsolete(
        //     "ExecuteDefaultAction override has been removed because default event handling was migrated to HandleEventBubbleUp. Please use HandleEventBubbleUp.",
        //     false)]
        // protected override void ExecuteDefaultAction(EventBase evt)
        // {
        //     // When the mouse enter the icon, this will add the label to the hierarchy
        //     base.ExecuteDefaultAction(evt);
        //
        //     // if (evt.eventTypeId == MouseEnterEvent.TypeId())
        //     // {
        //     //     // And then we can fetch it here:
        //     //     GraphView gv = GetFirstAncestorOfType<GraphView>();
        //     //     var lab = gv.Q<Label>(classes: new[] { "icon-badge__text--" + badgeText.GetHashCode() });
        //     //     if (lab != null)
        //     //     {
        //     //         lab.style.color = color;
        //     //     }
        //     // }
        // }
    }
}
