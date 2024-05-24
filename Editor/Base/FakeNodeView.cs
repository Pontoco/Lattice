using System;
using Lattice.Editor.Events;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UIElements;

namespace Lattice.Editor.Views
{
	/// <summary>
	///     A fake node that is styled to look like a <see cref="LatticeNodeView" />, for use when previewing a node
	///     that's outside the current view.
	/// </summary>
	internal sealed class FakeNodeView : GraphElement
	{
		private readonly VisualElement root;

		/// <summary>The original port used to create this view.</summary>
		private readonly PortView originPort;

		/// <summary>The port created under this view if you used the <see cref="FakeNodeView(PortView)"/> constructor.</summary>
		private readonly PortView port;

		/// <summary>Create a <see cref="FakeNodeView" /> that looks like the input <paramref name="node" />, but lacking any ports.</summary>
		public FakeNodeView(BaseNodeView node)
		{
			layer = 101;
			pickingMode = PickingMode.Ignore;
			style.position = Position.Absolute;
			// Remove the annoying padding and margin added to all GraphElements.
			RemoveFromClassList("graphElement");

			root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.pontoco.lattice/Editor/UI/FakeNodeView.uxml").Instantiate()[0];
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pontoco.lattice/Editor/UI/TooltipView.uss"));
            Add(root);

			// Get details from node.
			VisualElementStyleSheetSet nodeStyleSheets = node.styleSheets;
			VisualElement nodeTitleContainer = node.Q(BaseNodeView.TitleContainerName);
			Label nodeSubtitle = nodeTitleContainer.Q<Label>(BaseNodeView.SubtitleLabelName);
			VisualElement nodeTitleIcon = nodeTitleContainer.Q(BaseNodeView.TitleIconName);

			// Set up:
			// Copy style sheets
			for (int i = 0; i < nodeStyleSheets.count; i++)
			{
				styleSheets.Add(nodeStyleSheets[i]);
			}

			// Title and subtitle
			VisualElement titleContainer = root.Q(BaseNodeView.TitleContainerName);
			Label title = titleContainer.Q<Label>(BaseNodeView.TitleLabelName);
			Label subtitle = titleContainer.Q<Label>(BaseNodeView.SubtitleLabelName);
			title.text = node.title;
			subtitle.text = nodeSubtitle.text;
            DisplayStyle subtitleDisplay = nodeSubtitle.resolvedStyle.display;
            subtitle.style.display = subtitleDisplay;
            titleContainer.Q(null, BaseNodeView.TitleNameContainerUssClassName)
                          .EnableInClassList(BaseNodeView.TitleNameContainerHasCustomNameUssClassName, subtitleDisplay == DisplayStyle.Flex);
            

			// Icon
			VisualElement titleIcon = titleContainer.Q(BaseNodeView.TitleIconName);
			if (nodeTitleIcon != null)
			{
				titleIcon.ClearClassList();
				foreach (string @class in nodeTitleIcon.GetClasses())
				{
					titleIcon.AddToClassList(@class);
				}
			}
			else
			{
				titleIcon.style.display = DisplayStyle.None;
			}

			root.Q("node-border").style.overflow = Overflow.Hidden;
		}

		/// <summary>
		///     Create a <see cref="FakeNodeView" /> that looks like the node associated with the input
		///     <paramref name="originPort" />, but where it's the only visible port.
		/// </summary>
		public FakeNodeView(PortView originPort) : this(originPort.Owner)
		{
			this.originPort = originPort;
			port = PortView.CreatePortView(null, originPort.direction, originPort.PortData, null);
			port.pickingMode = PickingMode.Ignore;
            // Fake the port as connected by adding the connected class.
			port.EnableInClassList(PortView.ConnectedUssClassName, originPort.connected);

			VisualElement container = originPort.Location switch
			{
				PortViewLocation.Top => root.Q(BaseNodeView.TopPortContainerName),
				PortViewLocation.Bottom => root.Q(BaseNodeView.BottomPortContainerName),
				PortViewLocation.Left => root.Q(BaseNodeView.LeftPortContainerName),
				PortViewLocation.Right => root.Q(BaseNodeView.RightPortContainerName),
				PortViewLocation.State => root.Q(BaseNodeView.StatePortContainerName),
				PortViewLocation.BottomLeft => root.Q(BaseNodeView.BottomLeftPortContainerName),
				_ => throw new ArgumentOutOfRangeException()
			};
			container.Add(port);
		}

		/// <summary>Position this element on screen, positioned on the line between the port this was created from and <paramref cref="connectedPort"/>.</summary>
		public void PositionOnScreenAlignedTowardsPort(PortView connectedPort, bool showTooltip)
		{
			Assert.IsNotNull(originPort, "Cannot align towards connected port if this view was not created with a port.");
			Assert.IsNotNull(port, "Cannot align towards connected port if this view was not created with a port.");

			// If the layout has not been calculated:
			if (float.IsNaN(layout.width))
			{
				// Hide while it's potentially at an invalid position.
				visible = false;
				// Retry positioning later.
				port.schedule.Execute(() => PositionOnScreenAlignedTowardsPort(connectedPort, showTooltip));
				return;
			}
			style.visibility = new StyleEnum<Visibility>(StyleKeyword.Null);

			GraphView parentGraph = connectedPort.Owner!.Owner;
			Vector2 originPortPosition = originPort.ChangeCoordinatesTo(parentGraph, Vector2.zero);

			// The position of this fake node if we placed its port on the origin port.
			Vector2 positionOnOriginPort = originPortPosition - port.ChangeCoordinatesTo(this, Vector2.zero);

			// Shift the position on screen.
			Rect fakeLocalBound = localBound;
			Rect graphLocalBound = parentGraph.localBound;
			Vector2 proposedPosition = new(
				Mathf.Clamp(positionOnOriginPort.x, 0, graphLocalBound.width - fakeLocalBound.width),
				Mathf.Clamp(positionOnOriginPort.y, 0, graphLocalBound.height - fakeLocalBound.height)
			);

			Vector2 proposedOffset = proposedPosition - positionOnOriginPort;
			Vector2 otherPortPosition = connectedPort.ChangeCoordinatesTo(parentGraph, Vector2.zero);
			Vector2 direction = (otherPortPosition - originPortPosition).normalized;
			// Find the maximum proposed offset, and use that distance to drive the other axis.
			if (Mathf.Abs(proposedOffset.x) > Mathf.Abs(proposedOffset.y))
			{
				proposedOffset.y = proposedOffset.x * (direction.y / direction.x);
			}
			else
			{
				proposedOffset.x = proposedOffset.y * (direction.x / direction.y);
			}

            proposedPosition = positionOnOriginPort + proposedOffset;
            
            // Final position is relative to the contentViewContainer.
			Vector2 finalPosition = parentGraph.ChangeCoordinatesTo(parentGraph.contentViewContainer, proposedPosition);
			
			style.left = finalPosition.x;
			style.top = finalPosition.y;
			
			if (showTooltip)
            {
                // Show tooltip later once this node has been repositioned.
                RegisterCallbackOnce<GeometryChangedEvent, PortView>(static (_, port) =>
                {
                    port?.GetFirstAncestorOfType<LatticeGraphView>()?.ShowGraphTooltip(port);
                }, port);
			}
		}
	}
}
