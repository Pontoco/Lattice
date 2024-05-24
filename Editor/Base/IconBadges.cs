using System;
using System.Collections.Generic;
using System.Reflection;
using Lattice.Utils;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor
{
    /// <summary>Helper class for managing <see cref="IconBadge" />s on an element.</summary>
    public sealed class IconBadges
    {
        private readonly VisualElement root;
        private readonly VisualElement attachmentTarget;
        private readonly List<IconBadge> badges = new();

        public IconBadges(VisualElement root, VisualElement attachmentTarget)
        {
            this.attachmentTarget = attachmentTarget;
            this.root = root;
        }

        /// <summary>Adds a message view (an attached icon and message).</summary>
        public void AddMessageView(string message, NodeMessageType messageType, SpriteAlignment alignment = SpriteAlignment.TopRight, bool allowsRemoval = true)
        {
            IconBadge badge;
            switch (messageType)
            {
                case NodeMessageType.Warning:
                    badge = new CustomIconBadge(message, NodeMessageType.Warning);
                    break;
                case NodeMessageType.Error:
                    badge = IconBadge.CreateError(message);

                    // Hack to make the error badge text box much larger.
                    // Note that the text element is not present in the hierarchy until hovered.
                    Label text =
                        (Label)typeof(IconBadge).GetField("m_TextElement",
                                                    BindingFlags.Instance | BindingFlags.NonPublic)!
                                                .GetValue(badge);
                    text.style.width = 800;
                    text.style.maxWidth = 800;

                    break;
                case NodeMessageType.Info:
                    badge = IconBadge.CreateComment(message);
                    break;
                default:
                case NodeMessageType.None:
                    badge = new CustomIconBadge(message, null, Color.grey);
                    break;
            }

            // Force any children of the root to be un-pickable.
            // This makes it easy to detect if you're hovering an IconBadge, while not changing its behaviour.
            for (int i = 0; i < badge.childCount; i++)
            {
                badge[i].pickingMode = PickingMode.Ignore;
            }

            AddBadge();
            return;

            void AddBadge()
            {
                root.Add(badge);
                if (allowsRemoval)
                {
                    badges.Add(badge);
                }
                badge.AttachTo(attachmentTarget, alignment);
            }
        }

        /// <summary>Removes a message view with the matching <paramref name="message" />.</summary>
        public void RemoveMessageView(string message)
        {
            RemoveBadge(b => b.badgeText == message);
            return;

            void RemoveBadge(Func<IconBadge, bool> callback)
            {
                badges.RemoveAll(b =>
                {
                    if (!callback(b))
                    {
                        return false;
                    }
                    b.Detach();
                    b.RemoveFromHierarchy();
                    return true;
                });
            }
        }

        /// <summary>Removes all message views.</summary>
        public void ClearMessageViews()
        {
            foreach (IconBadge b in badges)
            {
                b.Detach();
                b.RemoveFromHierarchy();
            }

            badges.Clear();
        }

        private sealed class CustomIconBadge : IconBadge
        {
            private Label label;
            private Texture icon;
            private bool isCustom;

            public CustomIconBadge(string message, NodeMessageType messageType)
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

            public CustomIconBadge(string message, Texture icon, Color color)
            {
                CreateCustom(message, icon, color);
            }

            private void CreateCustom(string message, Texture icon, Color color)
            {
                badgeText = message;
                Image image = this.Q<Image>("icon");
                image.image = icon;
                image.style.backgroundColor = color;
                style.color = color;
            }
        }
    }
}
