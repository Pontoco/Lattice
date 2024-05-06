using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using GrEmit;
using Lattice.IR;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace Lattice.Editor.Views
{
    public class LatticeGraphToolbar : VisualElement
    {
        protected enum ElementType
        {
            Button,
            Toggle,
            DropDownButton,
            Separator,
            Custom,
            FlexibleSpace,
        }

        protected class ToolbarButtonData
        {
            public GUIContent content;
            public ElementType type;
            public bool value;
            public bool visible = true;
            public Action buttonCallback;
            public Action<bool> toggleCallback;
            public int size;
            public Action customDrawFunction;
        }

        private readonly List<ToolbarButtonData> leftButtonDatas = new List<ToolbarButtonData>();
        private readonly List<ToolbarButtonData> rightButtonDatas = new List<ToolbarButtonData>();
        protected LatticeGraphView graphView;

        public LatticeGraphToolbar(LatticeGraphView graphView)
        {
            name = "ToolbarView";
            this.graphView = graphView;

            leftButtonDatas.Clear();
            rightButtonDatas.Clear();
            AddButtons();

            Add(new IMGUIContainer(DrawImGUIToolbar));
        }

        private void AddButtons()
        {
            AddButton(new GUIContent("Center", "Frame the graph contents"), () => graphView.FrameAll());
            AddButton("Show in GraphViz", () =>
            {
                // Render graphviz just for this graph.
                GraphCompilation compilation = GraphCompiler.CompileStandalone(graphView.Graph);
                string dotString = GraphCompilation.ToDot(compilation);
                OpenGraphviz(dotString);
            }, left: false);

            AddButton("Show In Project", () => EditorGUIUtility.PingObject(graphView.Graph), false);
            AddButton("Show IL", ShowIL, left: false);

            // Entity selectors.
            AddCustom(() =>
            {
                IRExecution execution = ExecutionHistory.MostRecent;
                if (execution == null || !execution.Graph.SourceGraphs.Contains(graphView.Graph))
                {
                    GUI.enabled = false;
                    EditorGUILayout.DropdownButton(new GUIContent("Not Executed"),
                        FocusType.Passive,
                        EditorStyles.toolbarDropDown);
                    GUI.enabled = true;
                    return;
                }
                
                if (execution.DebugData == null)
                {
                    GUI.enabled = false;
                    EditorGUILayout.DropdownButton(new GUIContent("Lattice Debug Disabled"),
                        FocusType.Passive,
                        EditorStyles.toolbarDropDown);
                    GUI.enabled = true;
                    return;
                }

                // A Lattice Graph may have nodes within it that run *for different entity types*. This is uncommon, 
                // but can be useful if you want to add behavior to an entity, or filter through information on other
                // entities. When a graph is 'qualified' for several entity types like this, we render a list of entity
                // selectors.
                bool showNames = graphView.ViewingEntities.Count > 1;
                var entitiesByQualifier = execution.EntitiesInGraph(graphView.Graph);

                foreach (var (q, entities) in entitiesByQualifier)
                {
                    Entity current = graphView.ViewingEntities.GetValueOrDefault(q, Entity.Null);
                    
                    string titleName = showNames ? $"{q.ShortName()}:{current}" : current.ToString();
                    if (EditorGUILayout.DropdownButton(new GUIContent(titleName),
                            FocusType.Passive,
                            EditorStyles.toolbarDropDown))
                    {
                        GenericMenu menu = new();

                        foreach (Entity entity in entities)
                        {
                            string name = entity == Entity.Null ? "Shared" : entity.ToString();

                            menu.AddItem(new GUIContent(name), current == entity,
                                () =>
                                {
                                    graphView.ViewingEntities[q] = entity;
                                    graphView.DisplayExecution(execution);
                                });
                        }

                        menu.ShowAsContext();
                    }
                }
            });

            AddButton("Log Execution", () => IRExecution.LogExecution = !IRExecution.LogExecution, false);
            AddButton("Clear/Recompile", () =>
            {
                GraphCompiler.ClearCompilation();
                if (graphView.Graph != null)
                {
                    GraphCompiler.AddToCompilation(graphView.Graph);
                }
                GraphCompiler.RecompileIfNeeded();
            }, false);

            // Execution selector from execution history. (todo: needs more work for UI, mostly)
            // AddCustom(() =>
            // {
            //     if (EditorGUILayout.DropdownButton(new GUIContent("Select Session"),
            //             FocusType.Passive,
            //             EditorStyles.toolbarDropDown))
            //     {
            //         GenericMenu menu = new();
            //
            //         foreach (var execution in ExecutionHistory.History)
            //         {
            //             menu.AddItem(new GUIContent(execution.time.ToString()), graphView.Execution == execution.execution,
            //                 () => graphView.Execution = execution.execution);
            //         }
            //
            //         menu.ShowAsContext();
            //     }
            // });
        }

        public static void OpenGraphviz(string dot)
        {
            string encodedDotString = UnityWebRequest.EscapeURL(dot).Replace("+", "%20");
            string url = $"https://dreampuf.github.io/GraphvizOnline/#{encodedDotString}";
            
            // Create temporary HTML file. The url generated here is too big to fit in a Win32 command line argument,
            // so we have to save it into a temp file and use a redirector to get the browser to load the page. 
            string tempHtmlPath = FileUtil.GetUniqueTempPathInProject() + "redirect.html";

            using (StreamWriter writer = new(tempHtmlPath))
            {
                writer.WriteLine("<!DOCTYPE html>");
                writer.WriteLine("<html>");
                writer.WriteLine("<head>");
                writer.WriteLine($"<meta http-equiv=\"refresh\" content=\"0; url={url}\" />");
                writer.WriteLine("<title>Redirecting...</title>");
                writer.WriteLine("</head>");
                writer.WriteLine("<body>");
                writer.WriteLine($"If you are not redirected automatically, please click <a href=\"{url}\">here</a>");
                writer.WriteLine("</body>");
                writer.WriteLine("</html>");
            }

            // Open temporary file with default browser
            EditorUtility.OpenWithDefaultApp(tempHtmlPath); 
        }

        private void ShowIL()
        {
            var graph = GraphCompiler.RecompileIfNeeded();

            DynamicMethod method = new DynamicMethod("ExecuteLattice", typeof(void),
                new[] { typeof(IRExecution), typeof(EntityManager) });
            GroboIL emit = new GroboIL(method);

            if (!ILGeneration.EmitNodeExecutionIL(emit, graph, graph.Nodes.ToHashSet()))
            {
                Debug.LogError("(Lattice) IL Generation failed.");
                return;
            }

            Debug.Log(emit);

            var path = FileUtil.GetUniqueTempPathInProject() + ".msil";
            File.WriteAllText(path, emit.ToString());

            EditorUtility.OpenWithDefaultApp(path);
        }


        protected ToolbarButtonData AddButton(string name, Action callback, bool left = true)
            => AddButton(new GUIContent(name), callback, left);

        protected ToolbarButtonData AddButton(GUIContent content, Action callback, bool left = true)
        {
            var data = new ToolbarButtonData
            {
                content = content,
                type = ElementType.Button,
                buttonCallback = callback
            };
            (left ? leftButtonDatas : rightButtonDatas).Add(data);
            return data;
        }

        protected void AddSeparator(int sizeInPixels = 10, bool left = true)
        {
            var data = new ToolbarButtonData
            {
                type = ElementType.Separator,
                size = sizeInPixels,
            };
            (left ? leftButtonDatas : rightButtonDatas).Add(data);
        }

        protected void AddCustom(Action imguiDrawFunction, bool left = true)
        {
            if (imguiDrawFunction == null)
            {
                throw new ArgumentException("imguiDrawFunction can't be null");
            }

            var data = new ToolbarButtonData
            {
                type = ElementType.Custom,
                customDrawFunction = imguiDrawFunction,
            };
            (left ? leftButtonDatas : rightButtonDatas).Add(data);
        }

        protected void AddFlexibleSpace(bool left = true)
        {
            (left ? leftButtonDatas : rightButtonDatas).Add(new ToolbarButtonData { type = ElementType.FlexibleSpace });
        }

        protected ToolbarButtonData AddToggle(string name, bool defaultValue, Action<bool> callback, bool left = true)
            => AddToggle(new GUIContent(name), defaultValue, callback, left);

        protected ToolbarButtonData AddToggle(GUIContent content, bool defaultValue, Action<bool> callback,
                                              bool left = true)
        {
            var data = new ToolbarButtonData
            {
                content = content,
                type = ElementType.Toggle,
                value = defaultValue,
                toggleCallback = callback
            };
            (left ? leftButtonDatas : rightButtonDatas).Add(data);
            return data;
        }

        protected ToolbarButtonData AddDropDownButton(string name, Action callback, bool left = true)
            => AddDropDownButton(new GUIContent(name), callback, left);

        protected ToolbarButtonData AddDropDownButton(GUIContent content, Action callback, bool left = true)
        {
            var data = new ToolbarButtonData
            {
                content = content,
                type = ElementType.DropDownButton,
                buttonCallback = callback
            };
            (left ? leftButtonDatas : rightButtonDatas).Add(data);
            return data;
        }

        /// <summary>Also works for toggles</summary>
        /// <param name="name"></param>
        /// <param name="left"></param>
        private void RemoveButton(string name, bool left)
        {
            (left ? leftButtonDatas : rightButtonDatas).RemoveAll(b => b.content.text == name);
        }

        /// <summary>Hide the button</summary>
        /// <param name="name">Display name of the button</param>
        private void HideButton(string name)
        {
            leftButtonDatas.Concat(rightButtonDatas).All(b =>
            {
                if (b?.content?.text == name)
                {
                    b.visible = false;
                }
                return true;
            });
        }

        /// <summary>Show the button</summary>
        /// <param name="name">Display name of the button</param>
        private void ShowButton(string name)
        {
            leftButtonDatas.Concat(rightButtonDatas).All(b =>
            {
                if (b?.content?.text == name)
                {
                    b.visible = true;
                }
                return true;
            });
        }

        private void DrawImGUIButtonList(List<ToolbarButtonData> buttons)
        {
            foreach (var button in buttons.ToList())
            {
                if (!button.visible)
                {
                    continue;
                }

                switch (button.type)
                {
                    case ElementType.Button:
                        if (GUILayout.Button(button.content, EditorStyles.toolbarButton) &&
                            button.buttonCallback != null)
                        {
                            button.buttonCallback();
                        }
                        break;
                    case ElementType.Toggle:
                        EditorGUI.BeginChangeCheck();
                        button.value = GUILayout.Toggle(button.value, button.content, EditorStyles.toolbarButton);
                        if (EditorGUI.EndChangeCheck() && button.toggleCallback != null)
                        {
                            button.toggleCallback(button.value);
                        }
                        break;
                    case ElementType.DropDownButton:
                        if (EditorGUILayout.DropdownButton(button.content, FocusType.Passive,
                                EditorStyles.toolbarDropDown))
                        {
                            button.buttonCallback();
                        }
                        break;
                    case ElementType.Separator:
                        EditorGUILayout.Separator();
                        EditorGUILayout.Space(button.size);
                        break;
                    case ElementType.Custom:
                        button.customDrawFunction();
                        break;
                    case ElementType.FlexibleSpace:
                        GUILayout.FlexibleSpace();
                        break;
                }
            }
        }

        protected virtual void DrawImGUIToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            DrawImGUIButtonList(leftButtonDatas);

            GUILayout.FlexibleSpace();

            DrawImGUIButtonList(rightButtonDatas);

            GUILayout.EndHorizontal();
        }
    }
}
