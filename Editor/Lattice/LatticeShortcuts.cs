using Lattice.Editor.Events;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Lattice.Editor
{
    /// <summary>
    ///     The editor shortcuts for Lattice.<br />
    ///     <b>Defaults:</b><br />
    ///     - H: Toggle Hidden State of Selected Edges<br />
    ///     <b>Built-in:</b><br />
    ///     - A: Frame All<br />
    ///     - O: Frame Origin<br />
    ///     - [: Frame Previous<br />
    ///     - ]: Frame Next<br />
    ///     - Space: Insert Node<br />
    /// </summary>
    internal static class LatticeShortcuts
    {
        // Note to avoid nameof() in IDs to prevent them being altered when refactoring.
        private const string RootId = "Lattice/";

        [Shortcut(
            RootId + "ToggleHiddenForSelectedEdges",
            typeof(LatticeGraphWindow),
            KeyCode.H,
            displayName = RootId + "Toggle Hidden State of Selected Edges"
        )]
        private static void ToggleHiddenForSelectedEdges(ShortcutArguments args)
        {
            LatticeGraphWindow window = (LatticeGraphWindow)args.context;
            using ToggleEdgeHiddenEvent evt = ToggleEdgeHiddenEvent.GetPooled();
            window.rootVisualElement?.SendEvent(evt);
        }
    }
}
