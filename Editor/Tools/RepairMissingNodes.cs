using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Lattice.Editor.SearchProviders;
using Lattice.Nodes;
using Lattice.StandardLibrary;
using Lattice.Utils;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UIElements;

namespace Lattice.Editor.Tools
{
    /// <summary>
    ///     An editor window / workflow to refactor missing references in our LatticeGraph assets. Node names and
    ///     namespaces change easily, so it's helpful to detect that and provide an easy way to refactor them.
    /// </summary>
    public class RepairMissingNodes : EditorWindow
    {
        // Represents a missing Type, usually a LatticeNode that's been moved / refactored.
        // Represents equality only through the first three components.
        private struct MissingType
        {
            public readonly string Assembly;
            public readonly string Namespace;
            public readonly string Type;

            public MissingType(string assembly, string ns, string c)
            {
                Assembly = assembly;
                Namespace = ns;
                Type = c;
            }

            public bool Equals(MissingType other)
            {
                return Assembly == other.Assembly && Namespace == other.Namespace && Type == other.Type;
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                return obj is MissingType other && Equals(other);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return HashCode.Combine(Assembly, Namespace, Type);
            }
        }

        public class NewMethodType
        {
            public SerializableMethodInfo Method;
        }

        [MenuItem("Lattice/Tools/Repair Missing Nodes")]
        public static void OpenRepairWindow()
        {
            var window = GetWindow<RepairMissingNodes>();
            window.titleContent = new GUIContent("Lattice: Repair Missing Nodes");
            window.ShowPopup();
        }

        private void OnEnable()
        {
            RefreshUI();
        }

        private void CreateGUI()
        {
            RefreshUI();
        }

        private void RefreshUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.Add(new Label(
                "If Lattice Nodes are missing in the C# assembly, they will show up in a list below. You can use this window to replace the missing references with the correct nodes.\n\n"));

            // Collect missing types in serialized graphs
            Dictionary<MissingType, HashSet<LatticeGraph>> missingTypes = new();
            // Collect missing methods
            Dictionary<SerializableMethodInfo, HashSet<ScriptNode>> missingMethods = new();
            
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(LatticeGraph)))
            {
                LatticeGraph graph = AssetDatabase.LoadAssetAtPath<LatticeGraph>(AssetDatabase.GUIDToAssetPath(guid));

                if (SerializationUtility.HasManagedReferencesWithMissingTypes(graph))
                {
                    foreach (ManagedReferenceMissingType missingRef in SerializationUtility
                                 .GetManagedReferencesWithMissingTypes(graph))
                    {
                        var missing = new MissingType(missingRef.assemblyName, missingRef.namespaceName,
                            missingRef.className);
                        if (!missingTypes.ContainsKey(missing))
                        {
                            missingTypes.Add(missing, new HashSet<LatticeGraph>());
                        }
                        missingTypes[missing].Add(graph);
                    }
                }

                foreach (var scriptNode in graph.LatticeNodes<ScriptNode>())
                {
                    var method = scriptNode.Method;
                    if (method == null)
                    {
                        Debug.LogError($"ScriptNode has null method. [{scriptNode}]");
                        continue;
                    }
                    
                    if (!method.IsValid())
                    {
                        Debug.LogError($"ScriptNode has empty/null method. [{scriptNode}]");
                        continue;
                    }
                    
                    if (!method.Exists())
                    {
                        if (!missingMethods.ContainsKey(method))
                        {
                            missingMethods.Add(method, new HashSet<ScriptNode>());
                        }
                        missingMethods[method].Add(scriptNode);
                    }
                }
            }

            List<(MissingType type, TextField text)> replaceTypes = new();

            foreach (var (missing, graphs) in missingTypes)
            {
                VisualElement row = CreateMissingRow(string.Join("\n", graphs), $"{missing.Assembly}, {missing.Namespace}.{missing.Type}");
                TextField text = row.Q<TextField>();
                row.Q<Button>().clicked += () =>
                {
                    SearchWindow.Open(new SearchWindowContext(GUIUtility.GUIToScreenPoint(Event.current.mousePosition),
                        800), TypesSearchProvider.Instance);
                    TypesSearchProvider.Instance.Callback = type =>
                    {
                        text.value = type.AssemblyQualifiedName;
                    };
                };

                Button killButton = new Button();
                killButton.text = "💀";
                killButton.tooltip = "Delete objects with this type.";
                killButton.clicked += () =>
                {
                    if (EditorUtility.DisplayDialog($"Kill All", $"Kill all nodes with type: [{missing.Type}]? In graphs:\n" +
                                                                 $"{string.Join("\n", graphs)}", "ok",
                            "hold up"))
                    {
                        foreach (var graph in graphs)
                        {
                            foreach (var m in SerializationUtility.GetManagedReferencesWithMissingTypes(graph))
                            {
                                if (new MissingType(m.assemblyName, m.namespaceName, m.className).Equals(missing))
                                {
                                    SerializationUtility.ClearManagedReferenceWithMissingType(graph,
                                        m.referenceId);
                                }
                            }
                            EditorUtility.SetDirty(graph);
                        }
                        EditorUtility.DisplayDialog("Wiped", $"Wiped in graphs [{string.Join(",", graphs)}]", "Thanks");
                        RefreshUI();
                    }
                };
                row.Add(killButton);
            
                rootVisualElement.Add(row);
                replaceTypes.Add((missing, text));
            }
            
            rootVisualElement.Add(new Label("\n\nMissing Methods:"));
            
            
            List<(SerializableMethodInfo method, NewMethodType newMethod)> replaceMethods = new();
            foreach (var (missingMethod, nodes) in missingMethods)
            {
                VisualElement row = CreateMissingRow(string.Join("\n", nodes), missingMethod.ToString());
                TextField text = row.Q<TextField>();
                NewMethodType newMethod = new();
                row.Q<Button>().clicked += () =>
                {
                    SearchWindow.Open(new SearchWindowContext(GUIUtility.GUIToScreenPoint(Event.current.mousePosition),
                        800), ScriptNodeMethodSearchProvider.Instance);
                    
                    ScriptNodeMethodSearchProvider.Instance.Callback = method =>
                    {
                        text.value = $"{method.DeclaringType!.AssemblyQualifiedName}::{method.Name}";
                        newMethod.Method = new SerializableMethodInfo(method, BindingFlags.Public | BindingFlags.Static);
                    };
                };
                rootVisualElement.Add(row);
                replaceMethods.Add((missingMethod, newMethod));
            }

            var submit = new Button();
            submit.text = "Refactor";
            rootVisualElement.Add(submit);
            submit.clicked += () =>
            {
                bool checkedForSave = false;
                bool modified = false;
                foreach (var (type, text) in replaceTypes)
                {
                    if (string.IsNullOrEmpty(text.value))
                    {
                        continue;
                    }

                    var newType = Type.GetType(text.value);
                    if (newType == null)
                    {
                        EditorUtility.DisplayDialog("Invalid Type",
                            $"Type [{text.value}] could not be located. Check typos?", "Ok");
                        return;
                    }

                    if (!checkedForSave)
                    {
                        checkedForSave = true;
                        if (!EditorUtility.DisplayDialog("Careful!",
                                "This will modify asset files on disk. It's recommended to have a clean Git before continuiing.",
                                "Ok", "Abort"))
                        {
                            return;
                        }
                    }

                    // Update assets with correct type.
                    foreach (var graph in missingTypes[type])
                    {
                        RefactorTypeInAsset(graph, type, newType);
                        modified = true;
                    }
                }
                
                foreach (var (oldMethod, newMethod) in replaceMethods)
                {
                    if (newMethod.Method == null)
                    {
                        continue;
                    }
                
                    // Update assets with correct type.
                    foreach (var node in missingMethods[oldMethod])
                    {
                        // Set the new method in the scriptnode
                        Assert.IsNotNull(newMethod.Method);
                        node.Method = newMethod.Method;
                        EditorUtility.SetDirty(node.Graph);
                        modified = true;
                    }
                }

                if (modified)
                {
                    AssetDatabase.Refresh();
                    EditorUtility.RequestScriptReload();
                }
                else
                {
                    EditorUtility.DisplayDialog("Notice",
                        "No types were updated.", "Ok");
                }

                RefreshUI();
            };
        }

        private static VisualElement CreateMissingRow(string tooltip, string label)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row
                }
            };

            Label graphHint = new("[?]")
            {
                tooltip = tooltip
            };
            row.Add(graphHint);
            row.Add(new Label(label));

            var textField = new TextField()
            {
                style =
                {
                    width = 200,
                    flexGrow = 1
                }
            };
            row.Add(textField);

            Button button = new()
            {
                text = "🔎"
            };
            row.Add(button);
            
            return row;
        }

        private static void RefactorTypeInAsset(LatticeGraph graph, MissingType original,
                                                Type replacement)
        {
            var path = AssetDatabase.GetAssetPath(graph);
            var fileText = File.ReadAllText(path);

            // This supports "RefIds" aka. [SerializeReferences].
            fileText = fileText.Replace(
                $"type: {{class: {original.Type}, ns: {original.Namespace}, asm: {original.Assembly}}}",
                $"type: {{class: {replacement.Name}, ns: {replacement.Namespace}, asm: {replacement.Assembly.GetName().Name}}}");

            File.WriteAllText(path, fileText);
            // Todo: Support generics!
            // Todo: Support our SerializedType class.
        }
    }
}
