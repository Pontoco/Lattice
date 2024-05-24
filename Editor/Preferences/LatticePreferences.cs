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
		
		public void Save() => Save(true);
	}
}
