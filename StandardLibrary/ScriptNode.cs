using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Lattice.Base;
using Lattice.IR;
using Lattice.Nodes;
using Lattice.Utils;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Serialization;
using Exception = System.Exception;

#if UNITY_EDITOR
using UnityEditor;
using Lattice.Editor.Utils;
#endif

namespace Lattice.StandardLibrary
{
    /// <summary>
    ///     This is the node type that exposes [LatticeNode] and [LatticeNodes] C# functions to the Lattice editor. The node
    ///     interprets the method's input parameters and outputs and builds a node you can use in your graphs.
    /// </summary>
    /// <remarks>
    ///     Also handles setting up state for the function, via 'this ref' parameter inputs. Handles splitting tuple
    ///     return types into several sub-node outputs.
    /// </remarks>
    [Serializable]
    public class ScriptNode : LatticeNode
    {
        // This is the only field that we must serialize.
        [FormerlySerializedAs("MethodInfo")]
        [HideInInspector]
        public SerializableMethodInfo Method;

        public override string DefaultName => Method.Name();

        [NonSerialized]
        private bool isInitialized;

        /// <summary>
        ///     If this function returns a tuple (ie. multiple outputs), this list contains the names of each of the tuple
        ///     elements in order. Ex: (bool isOn, float progress) would be ["isOn", "progress"].
        /// </summary>
        /// <remarks>If a tuple does not have element names, or this does not return a tuple, this is null.</remarks>
        [NonSerialized]
        [CanBeNull]
        private List<string> tupleElementNames;

        // todo: propagate through compilation
        public override bool DependsOnTime => true;

        // todo: Serializing this by reference stores the type into the serialized data. This isn't "ideal", but fine until
        // we take a pass to improve it. Unfortunately there's no "type" for the params of a method, because it's all
        // generated from an attribute on a random method. We'll probably need a custom serialization protocol for
        // the defaults, based on param name, ordering, or adding additional validation checks. Json might work.
        // 
        // This doesn't live on LatticeNode, because most nodes don't have a dynamic set of defaults. Just static, which can
        // be embedded directly in the authoring node. Static is better for lots of reasons, not the least storage format.
        // But it also avoids needing [SerializeReference] which is fragile and encodes type information.
        /// <summary>
        ///     The values (of type PortDefault) of each port as set in the inspector by the user. Used when no value is
        ///     provided to a port. Only for side ports.
        /// </summary>
        /// <seealso cref="PortDefault{T}" />
        [FormerlySerializedAs("DefaultValues")]
        [HideInInspector]
        [SerializeReference]
        internal List<IPortDefault> PortDefaultValues = new();

        private void InitializeReflection()
        {
            if (isInitialized)
            {
                return;
            }

            MethodInfo method;
            try
            {
                method = Method.Resolve();
            }
            catch (Exception e)
            {
                MalformedReason = e;
                isInitialized = true;
                return;
            }

            bool hasThisParam = method.IsDefined(typeof(ExtensionAttribute), false);

            int paramIndex = 0;
            foreach (var param in method.GetParameters())
            {
                bool isRef = param.HasRefKeyword();
                bool isThis = hasThisParam && paramIndex++ == 0;
                Type paramType = param.ParameterType.IsByRef
                    ? param.ParameterType.GetElementType()
                    : param.ParameterType;

                if (!Extensions.TypeIsSupported(paramType))
                {
                    throw new Exception(
                        $"Parameter [{param.Name}] must have an unmanaged type [{paramType}]. In method [{method.DeclaringType}.{method.Name}]"
#if UNITY_EDITOR
                        + $" (at {SourceUtility.GetMethodSourceInfoFormattedAsLink(method)})"
#endif
                    );
                }

                if (param.IsIn || param.IsOut)
                {
                    throw new Exception(
                        $"Lattice does not support parameters with 'in' or 'out'. In method [{method.DeclaringType}.{method.Name}]"
#if UNITY_EDITOR
                        + $" (at {SourceUtility.GetMethodSourceInfoFormattedAsLink(method)})"
#endif
                    );
                }

                if (method.ReturnType.IsGenericType &&
                    method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTuple<,,,,,,,>))
                {
                    Debug.LogError(
                        $"Lattice does not support nodes with more than 7 outputs, yet. In method [{method.DeclaringType}.{method.Name}]"
#if UNITY_EDITOR
                        + $" (at {SourceUtility.GetMethodSourceInfoFormattedAsLink(method)})"
#endif
                    );
                }

                if (isRef)
                {
                    if (isThis)
                    {
                        StateType = paramType;
                    }
                    else
                    {
                        ActionPorts.Add(param.Name);
                    }
                }
                else if (param.GetCustomAttribute<PropAttribute>() != null)
                {
                    // Add a port default value if none exists already. Long term, we want to be more thoughtful about
                    // how port defaults are stored, so they only get serialized if the defaults are changed. 
                    if (PortDefaultValues.All(d => d.PortId != param.Name))
                    {
                        // Create a new default value wrapper struct.
                        Type portDefaultType = typeof(PortDefault<>).MakeGenericType(paramType);
                        IPortDefault portDefault = (IPortDefault)Activator.CreateInstance(portDefaultType);

                        portDefault.PortId = param.Name;

                        if (param.HasDefaultValue)
                        {
                            // Set it with the default value from the method definition.
                            portDefault.Set(param.DefaultValue);
                        }
                        else
                        {
                            // Set it with the default value for the given type.
                            portDefault.Set(Activator.CreateInstance(paramType));
                        }

                        PortDefaultValues.Add(portDefault);
                    }
                }
            }

            // Extract tuple element names from return type, if this function returns a tuple.
            if (typeof(ITuple).IsAssignableFrom(method.ReturnType))
            {
                bool foundElementNames = false;
                foreach (var attr in method.ReturnTypeCustomAttributes.GetCustomAttributes(true))
                {
                    if (attr is TupleElementNamesAttribute names)
                    {
                        if (foundElementNames)
                        {
                            // This shouldn't happen as far as I know. If it does, we'll have to figure out how to select from the several.
                            throw new Exception(
                                "LatticeNode function had multiple TupleElementNames attributes specified on the return type! That's weird?");
                        }

                        tupleElementNames = names.TransformNames.ToList();

                        foundElementNames = true;
                    }
                }
            }

            isInitialized = true;
        }

        // A generic function that just returns the default value for a given type.
        private static T GetDefaultValue<T>()
        {
            return default;
        }

        private static T GetPortDefault<T>(LatticeNode node, string port)
        {
            ScriptNode snode = (ScriptNode)node;
            return (T)snode.GetPortDefaultDynamic(port);
        }

        public object GetPortDefaultDynamic(string port)
        {
            foreach (var def in PortDefaultValues)
            {
                if (def.PortId == port)
                {
                    return def.Get();
                }
            }

            throw new Exception($"No default stored for port [{port}]");
        }

        public IPortDefault GetPortDefaultWrapper(string port)
        {
            foreach (var def in PortDefaultValues)
            {
                if (def.PortId == port)
                {
                    return def;
                }
            }

            throw new Exception($"No default stored for port [{port}]");
        }

        // ReSharper disable once UnusedParameter.Local
        // Just waits for the barrier and returns the boxedRef again.
        private static BoxedRef<T> ReborrowBoxedRef<T>(BoxedRef<T> boxedRef, Unit barrier)
        {
            return boxedRef;
        }

        // Waits for the function to complete and then just unboxes the state ref as a value.
        private static TStateType WaitAndUnbox<TStateType, TFuncReturn>(BoxedRef<TStateType> boxedRef,
                                                                        TFuncReturn resultUnused)
        {
            return boxedRef.Value;
        }

        public override void CompileToIR(GraphCompilation compilation)
        {
            InitializeReflection();

            var method = Method.Resolve();
            Assert.IsNotNull(method);

            if (method.ReturnType != typeof(void) && !Extensions.TypeIsSupported(method.ReturnType))
            {
                if (!method.ReturnType.IsValueType)
                {
                    throw new Exception(
                        $"Node function has managed return type. [{method.ReturnType}] Return types must be unmanaged.");
                }
                throw new Exception($"Node function has unsupported return type. [{method.ReturnType}]");
            }

            if (method.ReturnType.IsGenericType &&
                method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTuple<,,,,,,,>))
            {
                throw new Exception(
                    $"Lattice does not support nodes with more than 7 outputs, yet. In method [{method.DeclaringType}.{method.Name}]");
            }

            // The primary execution node, that calls the user's function!
            var primaryNode = compilation.AddNode(this, method.Name, new FunctionIRNode(method));

            // Set node phase if it's marked with the attribute.
            var phase = method.GetCustomAttribute<LatticePhaseAttribute>();
            if (phase != null)
            {
                primaryNode.SystemPhase = phase.Phase;
            }

            bool hasThis = method.IsDefined(typeof(ExtensionAttribute), false);

            int i = 0;
            foreach (var param in method.GetParameters())
            {
                if (param.ParameterType == typeof(EntityManager) || param.ParameterType == typeof(LatticeNode))
                {
                    // Do nothing, this input is added automatically during execution.
                    i++;
                    continue;
                }

                // If the function has state (ref this), pass the previous state in as a reference.
                if (i == 0 && param.HasRefKeyword() && hasThis)
                {
                    var previousNode = compilation.AddNode(this, new PreviousIRNode( /* set below */ null));

                    // For now, all past state defaults to the default value for the type. But we should allow overriding this.
                    var defaultNode = compilation.AddNode(this, "DefaultValue",
                        FunctionIRNode.FromStaticMethod<ScriptNode>(nameof(GetDefaultValue), StateType));
                    defaultNode.CheckExceptions = false;

                    previousNode.AddInput(PreviousIRNode.DefaultValuePort, defaultNode);

                    // First boxedRef sandwich to expose the state to other nodes.
                    var (boxedRefNode, barrierNode) = compilation.AddBoxedRefSandwich(this, previousNode, StateType);
                    compilation.Mappings[this].StateRefNode = boxedRefNode;
                    compilation.Mappings[this].StateRefBarrierNode = barrierNode;

                    // Wait for those mutators, and reborrow the state ref to pass into this function.
                    IRNode reborrowNode = compilation.AddNode(this,
                        FunctionIRNode.FromStaticMethod<ScriptNode>(nameof(ReborrowBoxedRef), StateType));
                    reborrowNode.CheckExceptions = false;
                    reborrowNode.AddInput("boxedRef", boxedRefNode);
                    reborrowNode.AddInput("barrier", barrierNode);

                    primaryNode.AddInput(param.Name, reborrowNode);

                    // Unwrap the boxedRef so the previous node can reference it.
                    var unwrapNode = compilation.AddNode(this, "StateAfterExecute",
                        FunctionIRNode.FromStaticMethod<ScriptNode>(
                            nameof(WaitAndUnbox), StateType, 
                            // If we need a barrier node below, this node takes a Unit type as input.
                            method.ReturnType != typeof(void) ? method.ReturnType : typeof(Unit)));
                    unwrapNode.CheckExceptions = false;
                    unwrapNode.AddInput("boxedRef", reborrowNode);

                    if (method.ReturnType != typeof(void))
                    {
                        unwrapNode.AddInput("resultUnused", primaryNode);
                    }
                    else
                    {
                        // If the method returns void, we can't directly depend on it as an input to StateAfterExecute.
                        // So, use a barrier node which can actually take an input of type void, and returns Unit.
                        // We could do this for all stateful script nodes, but it'd add extraneous nodes to the graph.
                        var barrier = compilation.AddNode(this, "VoidBarrier", new BarrierIRNode());
                        barrier.AddInput(BarrierIRNode.PortWriters, primaryNode);
                        unwrapNode.AddInput("resultUnused", barrier);
                    }

                    compilation.Mappings[this].StateAfterExecute = unwrapNode;

                    previousNode.BackRef = unwrapNode;
                }
                else
                {
                    LatticeNode connected =
                        (LatticeNode)GetPort(param.Name).GetEdges().Select(e => e.fromNode).FirstOrDefault();
                    if (connected != null)
                    {
                        // nothing to do, the port will just map properly.
                    }
                    else if (param.GetCustomAttribute<PropAttribute>() != null)
                    {
                        Assert.IsFalse(param.ParameterType.IsByRef);

                        // Load the stored property and plug that into the input port.
                        var portName = compilation.AddNode(this, compilation.LiteralString(param.Name));
                        var defaultVal = compilation.AddNode(this, "PortDefault_" + param.Name,
                            FunctionIRNode.FromStaticMethod<ScriptNode>(nameof(GetPortDefault), param.ParameterType));
                        defaultVal.AddInput("port", portName);
                        primaryNode.AddInput(param.Name, defaultVal);
                    }
                    else if (!param.IsOptional)
                    {
                        // No input was connected for this param. Can't calculate this function! Throw error instead.
                        throw new Exception($"Input on port [{param.Name}] is required.");
                    }
                }

                i++;
            }

            compilation.MapInputPorts(this, primaryNode);
            compilation.SetPrimaryNode(this, primaryNode);

            // Create all of the output ports.
            // ============================
            Type returnType = method.ReturnType;
            if (typeof(ITuple).IsAssignableFrom(returnType))
            {
                // Tuples can be used to generate several outputs.
                int fieldNum = 0;
                foreach (var field in returnType.GetFields())
                {
                    compilation.AddFieldAccessor(this, primaryNode, field, tupleElementNames?[fieldNum++]);
                }
            }
            else if (returnType != typeof(void))
            {
                // single value returned, just set the main node as primary output
                compilation.Mappings[this].OutputPortMap["output"] = primaryNode;
            }

            // no output if the return type is void.
        }

        /// <summary>
        ///     Returns a default value for the type. Only implemented for certain types, as most structs/classes default
        ///     value isn't usually what you want. (ie. a default LocalTransform is never useful)
        /// </summary>
        public bool TryGetDefaultValue(Type type, out object defaultValue)
        {
            if (type == typeof(bool))
            {
                defaultValue = false;
                return true;
            }

            if (type == typeof(float3))
            {
                defaultValue = float3.zero;
                return true;
            }

            defaultValue = default;
            return false;
        }

        public object FixupReflectionTypes(object value, Type intendedType)
        {
            // Convert Entity.Null to a simple null reference.
            if (intendedType == typeof(Entity?) && (Entity)value == Entity.Null)
            {
                return null;
            }

            return value;
        }

        protected override IEnumerable<PortData> GenerateInputPorts()
        {
            InitializeReflection();
            if (MalformedReason != null)
            {
                // todo: Carry over the ports from the last successful compilation? or maybe use the existing connected edges?
                yield break;
            }

            bool hasThisParam = Method.Resolve()!.IsDefined(typeof(ExtensionAttribute), false);
            int paramIndex = 0;
            foreach (var param in Method.Resolve()!.GetParameters())
            {
                if (param.ParameterType == typeof(EntityManager) || param.ParameterType == typeof(LatticeNode))
                {
                    // Do nothing, this input is added automatically during execution.
                    paramIndex++;
                    continue;
                }

                bool isThis = hasThisParam && paramIndex++ == 0;

                if (param.HasRefKeyword())
                {
                    if (isThis)
                    {
                        // No default state port yet.

                        yield return new PortData(param.Name, optional: true)
                        {
                            defaultType = param.ParameterType.GetElementType()!,
                            acceptMultipleEdges = true, // todo: eventually we want to be able to order these
                            secondaryPort = true,
                            sizeInPixel = PortSizeStateRefs,
                            isRefType = true
                        };
                    }

                    // Normal refs are RW edges, which are rendered as outputs.
                    continue;
                }

                if (!Extensions.TypeIsSupported(param.ParameterType))
                {
                    Debug.LogError(
                        $"Parameter [{param.Name}] must have an unmanaged type [{param.ParameterType}]. In method [{Method}]");
                    continue;
                }

                bool isProp = param.HasCustomAttribute<PropAttribute>();
                yield return new PortData(param.Name)
                {
                    defaultType = param.ParameterType,
                    acceptMultipleEdges = false,
                    sizeInPixel = isProp ? PortSizeSideInputs : PortSizePrimaryValue,
                    vertical = !isProp,
                    hasDefault = isProp, // store a default value!
                };
            }
        }

        protected override IEnumerable<PortData> GenerateOutputPorts()
        {
            InitializeReflection();
            if (MalformedReason != null)
            {
                // todo: Carry over the ports from the last successful compilation? or maybe use the existing connected edges?
                yield break;
            }

            Type returnType = Method.Resolve()!.ReturnType;

            if (typeof(ITuple).IsAssignableFrom(returnType))
            {
                // Tuples can be used to generate several outputs.
                int fieldNum = 0;
                foreach (var field in returnType.GetFields())
                {
                    string elementName = tupleElementNames?[fieldNum++] ?? field.Name;
                    Type portType = field.FieldType;

                    yield return new PortData(elementName)
                    {
                        defaultType = portType,
                        acceptMultipleEdges = true,
                    };
                }
            }
            else if (returnType != typeof(void)) // no output if the return type is void.
            {
                Type portType = returnType;

                yield return new PortData("output")
                {
                    defaultType = portType,
                    acceptMultipleEdges = true,
                };
            }

            // Add ports for action rw outputs.
            bool hasThisParam = Method.Resolve()!.IsDefined(typeof(ExtensionAttribute), false);
            int paramIndex = 0;
            foreach (var param in Method.Resolve()!.GetParameters())
            {
                bool isThis = hasThisParam && paramIndex++ == 0;

                if (param.HasRefKeyword() && !isThis)
                {
                    // Add action ports to bottom.
                    yield return new PortData(param.Name, false)
                    {
                        displayName = "",
                        defaultType = param.ParameterType.GetElementType(),
                        acceptMultipleEdges = false,
                        isRefType = true
                    };
                }
            }
        }
    }

    /// <summary>Marks that this static function represents a Lattice Node, and can be called from within a Lattice Graph.</summary>
    /// <seealso cref="LatticeNodesAttribute" />
    [AttributeUsage(AttributeTargets.Method)]
    public class LatticeNodeAttribute : Attribute { }

    /// <summary>
    ///     Marks that all methods under a static class are Lattice Nodes. This is convenient when you have a big list of
    ///     Lattice functions and don't want to mark [LatticeNode] on each of them.
    /// </summary>
    /// <seealso cref="LatticeNodeAttribute" />
    public class LatticeNodesAttribute : Attribute
    {
        /// <summary>The folder that the nodes will be placed under in the create menu. Format: "my/nested/folder"</summary>
        public string OverrideMenuFolder;

        public LatticeNodesAttribute() { }

        /// <param name="overrideMenuFolder">
        ///     The folder that the nodes will be placed under in the create menu. Format:
        ///     "my/nested/folder"
        /// </param>
        public LatticeNodesAttribute(string overrideMenuFolder)
        {
            OverrideMenuFolder = overrideMenuFolder;
        }
    }

    /// <summary>Exposes ScriptNodes to the CreateNode menu.</summary>
    public class ScriptNodeTemplate : INodeTemplate
    {
        public const BindingFlags SupportedMethods = BindingFlags.Public | BindingFlags.Static;

        public MethodInfo Definition;

        public BaseNode Build()
        {
            return new ScriptNode
            {
                Method = new SerializableMethodInfo(Definition, SupportedMethods),
            };
        }

        public Type NodeType => typeof(ScriptNode);

#if UNITY_EDITOR
        [AddToNodeMenu]
        public static IEnumerable<NodeTemplateMenuItem> AvailableTemplates()
        {
            List<(MethodInfo, string pathOverride)> visualNodeMethods = new();

            // Add all methods under a class with [LatticeNodes]
            foreach (var vnodeType in TypeCache.GetTypesWithAttribute<LatticeNodesAttribute>())
            {
                var attr = vnodeType.GetCustomAttribute<LatticeNodesAttribute>();
                foreach (var method in vnodeType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    visualNodeMethods.Add((method, $"{attr.OverrideMenuFolder}/{method.Name}"));
                }
            }

            foreach (var vNodeMethod in TypeCache.GetMethodsWithAttribute<LatticeNodeAttribute>())
            {
                visualNodeMethods.Add((vNodeMethod, null));
            }

            foreach (var (nodeMethod, overridePath) in visualNodeMethods)
            {
                if (!nodeMethod.IsStatic || !nodeMethod.IsPublic)
                {
                    Debug.LogWarning(
                        $"Method will not be parsed by the VS compiler. Must be 'public static'. [{nodeMethod.Name}]");
                    continue;
                }

                // Generate a node based on the reflected type signature..
                yield return new NodeTemplateMenuItem
                {
                    Template = new ScriptNodeTemplate
                    {
                        Definition = nodeMethod
                    },

                    MenuPath = string.IsNullOrEmpty(overridePath)
                        ? $"{nodeMethod.DeclaringType!.Namespace?.Replace(".", "/")}/{nodeMethod.DeclaringType.Name}/{nodeMethod.Name}"
                        : overridePath
                };
            }
        }
#endif
    }

    public static class Extensions
    {
        private class U<T> where T : unmanaged { }

        public static bool CanBeAssignedValue(this Type t, object value)
        {
            if (value == null)
            {
                // Type must be nullable to accept a null value.
                return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>);
            }

            return t.IsInstanceOfType(value);
        }

        public static bool TypeIsSupported(this Type t)
        {
            // Nullables need custom handling. Technically they're not unmanaged in c#! Which is super weird.
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                return TypeIsSupported(t.GetGenericArguments()[0]);
            }

            // We allow a special exception for AnimationCurve, which is a manged object. However, there's no great
            // alternate for Burst/HPC#, so for now we just use the managed object. Eventually we come back in and 
            // update these.
            if (t == typeof(AnimationCurve))
            {
                return true;
            }

            // We support LatticeNode as an implicit injection. But it's really a hidden feature for implementing custom nodes.
            if (t == typeof(LatticeNode))
            {
                return true;
            }

            // For all other types we try constructing a generic type, which has the 'unmanaged' constraint! Reflection ftw!
            try
            {
                var _ = typeof(U<>).MakeGenericType(t);
                return true;
            }
            catch (Exception) { return false; }
        }

        public static bool HasRefKeyword(this ParameterInfo parameter)
        { // https://stackoverflow.com/a/38110036
            return parameter.ParameterType.IsByRef && !parameter.IsIn && !parameter.IsOut;
        }
    }

    /// <summary>The stored default value for a port, configured via the editor.</summary>
    /// <remarks>
    ///     We use a generic class here, because the Unity editor *needs* static typing to choose an inspector. This is
    ///     true of the classic Unity inspector, and the new PropertyElement/InspectorElement system in the Entities.UI
    ///     package. The editor also requires edited types to have a top-level PropertyBag "Container", it can't create an
    ///     automatic editor for a raw float (where would it set the value?). So this also provides that top-level container.
    /// </remarks>
    [Serializable]
    public class PortDefault<T> : IPortDefault
    {
        /// <summary>The default value stored.</summary>
        public T Value;

        /// <summary>Id of the port this default is for.</summary>
        [HideInInspector]
        public string Port;

        public string PortId
        {
            get => Port;
            set => Port = value;
        }

        public object Get() => Value;
        public void Set(object o) => Value = (T)o;
    }

    /// <summary>A convenience interface for working with <see cref="PortDefault{T}" /> without typing.</summary>
    public interface IPortDefault
    {
        public string PortId { get; set; }

        public object Get();
        public void Set(object o);
    }

    /// <summary>
    ///     Output of the core ScriptNode node. Wraps up the state and output value together, which is then broken apart
    ///     by field accessors.
    /// </summary>
    public struct ValueAndState
    {
        public object Value;
        public object State;
    }
}
