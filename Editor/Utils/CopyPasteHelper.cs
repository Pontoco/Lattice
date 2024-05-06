using System;
using System.Collections.Generic;

namespace Lattice.Editor.Utils
{
    [Serializable]
    public class CopyPasteHelper
    {
        public List<JsonElement> copiedNodes = new List<JsonElement>();

        public List<JsonElement> copiedGroups = new List<JsonElement>();

        public List<JsonElement> copiedEdges = new List<JsonElement>();
    }
}
