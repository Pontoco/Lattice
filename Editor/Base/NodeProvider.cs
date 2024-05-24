using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lattice.Base;
using Lattice.Editor.Views;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Lattice.Editor.Utils
{
    /// <summary>
    ///     A default node template that doesn't set any defaults or do any customization. This is used for every type
    ///     with the attribute [NodeMenuItem].
    /// </summary>
    public readonly struct BasicNodeBuilder : INodeTemplate
    {
        public readonly Type Type;

        public BasicNodeBuilder(Type type)
        {
            Type = type;
        }

        public BaseNode Build()
        {
            if (!Type.IsSubclassOf(typeof(BaseNode)))
            {
                Debug.LogError("Tried to build a type that didn't extend BaseNode. " + Type);
                return null;
            }

            return Activator.CreateInstance(Type) as BaseNode;
        }

        public Type NodeType => Type;
    }

    /// <summary>
    ///     Static functions for getting the list of nodes available to the graph. Contains a bunch of caches for type
    ///     reflection, attributes, etc.
    /// </summary>
    public static class NodeProvider
    {
        public struct NodeCreationByPort
        {
            public INodeTemplate nodeBuilder;
            public Type portType;
            public bool isInput;
            public string portIdentifier;
            public string portDisplayName;
        }

        private static readonly Dictionary<Type, Type> nodeViewPerType = new();

        public class NodeDescriptions
        {
            public Dictionary<string, INodeTemplate> nodePerMenuTitle = new();
            public List<NodeCreationByPort> nodeCreatePortDescription = new();
        }

        public struct NodeSpecificToGraph
        {
            public Type nodeType;
        }

        private static readonly Dictionary<BaseGraph, NodeDescriptions> specificNodeDescriptions = new();

        private static readonly List<NodeSpecificToGraph> specificNodes = new();

        private static readonly NodeDescriptions genericNodes = new();

        static NodeProvider()
        {
            // Build the node -> node view cache.
            foreach (var nodeViewType in TypeCache.GetTypesDerivedFrom<BaseNodeView>())
            {
                if (!nodeViewType.IsAbstract)
                {
                    AddNodeView(nodeViewType);
                }
            }

            BuildGenericNodeCache();
        }

        public static void LoadGraph(BaseGraph graph)
        {
            // Clear old graph data in case there was some
            specificNodeDescriptions.Remove(graph);
            var descriptions = new NodeDescriptions();
            specificNodeDescriptions.Add(graph, descriptions);

            foreach (var nodeInfo in specificNodes)
            {
                BuildCacheForNode(nodeInfo.nodeType, descriptions);
            }
        }

        public static void UnloadGraph(BaseGraph graph)
        {
            specificNodeDescriptions.Remove(graph);
        }

        // Uses the type cache to build the list of nodes that can be created across all graph types. (Generic Nodes)
        private static void BuildGenericNodeCache()
        {
            foreach (var nodeType in TypeCache.GetTypesDerivedFrom<BaseNode>())
            {
                if (!IsNodeAccessibleFromMenu(nodeType))
                {
                    continue;
                }

                BuildCacheForNode(nodeType, genericNodes);
            }

            // Extract all available node templates from the static methods marked [AddToNodeMenu]
            foreach (var nodeTemplateProvider in TypeCache.GetMethodsWithAttribute<AddToNodeMenuAttribute>())
            {
                var value = nodeTemplateProvider.Invoke(null, null);
                IEnumerable nodeTemplates = (IEnumerable)value;

                foreach (var templateObj in nodeTemplates)
                {
                    NodeTemplateMenuItem menuItem = (NodeTemplateMenuItem)templateObj;
                    genericNodes.nodePerMenuTitle[menuItem.MenuPath] = menuItem.Template;
                }
            }
        }

        private static void BuildCacheForNode(Type nodeType, NodeDescriptions targetDescription)
        {
            if (nodeType.GetCustomAttributes(typeof(NodeCreateMenuAttribute), false)
                is not NodeCreateMenuAttribute[] { Length: > 0 } attrs)
            {
                return;
            }

            foreach (var attr in attrs)
            {
                targetDescription.nodePerMenuTitle[attr.MenuPath] = new BasicNodeBuilder(nodeType);
            }
        }

        private static bool IsNodeAccessibleFromMenu(Type nodeType)
        {
            return !nodeType.IsAbstract && nodeType.GetCustomAttributes<NodeCreateMenuAttribute>().Any();
        }

        private static void AddNodeView(Type type)
        {
            var attrs = type.GetCustomAttributes(typeof(NodeCustomEditor), false);

            foreach (var attr in attrs)
            {
                Type nodeType = ((NodeCustomEditor)attr).NodeType;
                nodeViewPerType[nodeType] = type;
            }
        }

        public static Type GetNodeViewTypeFromType(Type nodeType)
        {
            Type view;

            if (nodeViewPerType.TryGetValue(nodeType, out view))
            {
                return view;
            }

            if (nodeType.IsGenericType && nodeViewPerType.TryGetValue(nodeType.GetGenericTypeDefinition(), out view))
            {
                return view;
            }

            Type nodeViewType = null;

            // Allow for inheritance in node views: multiple C# node using the same view
            foreach (var type in nodeViewPerType)
            {
                // Find a view (not first fitted view) of nodeType
                if (nodeType.IsSubclassIncludingGenerics(type.Key))
                {
                    if (nodeViewType != null)
                    {
                        // Find the most specific view type that matches the node type.
                        if (type.Value.IsSubclassOf(nodeViewType))
                        {
                            nodeViewType = type.Value;
                        }
                        else if (nodeViewType.IsSubclassOf(type.Value))
                        {
                            // do nothing, existing view is most specific.
                        }
                        else
                        {
                            Debug.LogError(
                                $"Custom NodeView [{type.Value}] conflicts with existing NodeView [{nodeViewType}]. One must extend the other.");
                        }
                    }
                    else
                    {
                        nodeViewType = type.Value;
                    }
                }
            }

            if (nodeViewType != null)
            {
                return nodeViewType;
            }

            return view;
        }

        private static bool IsSubclassIncludingGenerics(this Type type, Type parent)
        {
            if (!parent.IsGenericTypeDefinition)
            {
                return type.IsSubclassOf(parent);
            }

            while (type != null && type != typeof(object))
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == parent)
                {
                    return true;
                }

                if (type == parent)
                {
                    return true;
                }

                type = type.BaseType;
            }
            return false;
        }

        public static IEnumerable<(string path, INodeTemplate builder)> GetNodeMenuEntries(
            BaseGraph graph = null)
        {
            foreach (var node in genericNodes.nodePerMenuTitle)
            {
                yield return (node.Key, node.Value);
            }

            if (graph != null && specificNodeDescriptions.TryGetValue(graph, out var specificNodes))
            {
                foreach (var node in specificNodes.nodePerMenuTitle)
                {
                    yield return (node.Key, node.Value);
                }
            }
        }

        public static IEnumerable<NodeCreationByPort> GetEdgeCreationNodeMenuEntry(
            PortView portView, BaseGraph graph = null)
        {
            foreach (var description in genericNodes.nodeCreatePortDescription)
            {
                if (!IsPortCompatible(description))
                {
                    continue;
                }

                yield return description;
            }

            if (graph != null && specificNodeDescriptions.TryGetValue(graph, out var specificNodes))
            {
                foreach (var description in specificNodes.nodeCreatePortDescription)
                {
                    if (!IsPortCompatible(description))
                    {
                        continue;
                    }
                    yield return description;
                }
            }

            bool IsPortCompatible(NodeCreationByPort description)
            {
                if ((portView.direction == Direction.Input && description.isInput) ||
                    (portView.direction == Direction.Output && !description.isInput))
                {
                    return false;
                }

                if (!BaseGraph.TypesAreConnectable(description.portType, portView.portType))
                {
                    return false;
                }

                return true;
            }
        }
    }
}
