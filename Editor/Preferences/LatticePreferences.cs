using UnityEditor;
using UnityEngine;

namespace Lattice.Editor
{
	/// <summary>
	/// The per-user preferences for lattice.
	/// See <see cref="LatticePreferencesProvider"/> for the window.
	/// </summary>
	[FilePath(Path, FilePathAttribute.Location.PreferencesFolder)]
	internal sealed class LatticePreferences : ScriptableSingleton<LatticePreferences>
	{
		private const string Path = nameof(LatticePreferences) + ".asset";

		[Header("Window preferences")]
		[Tooltip("Multiple windows are opened when opening a " + nameof(LatticeGraph) + " asset")]
		public bool OpenGraphAssetsInNewTab = true;

        [Header("Searcher preferences")]
        [Tooltip("The minium size of the search windows when opened; the " + CreateNodeMenuProvider.TitleText + " menu for example.\n" +
                 "Windows can still be manually resized above this and the value will be retained until domain reload.")]
        public Vector2 MinimumSearchWindowSize = new(450, 350);
		
		public void Save() => Save(true);
	}
}
