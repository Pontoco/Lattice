using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Lattice.Editor.Views
{
    public class MiniMapView : MiniMap
    {
        private new BaseGraphView graphView;
        private Vector2 size;

        public MiniMapView(BaseGraphView baseGraphView)
        {
            graphView = baseGraphView;
            SetPosition(new Rect(0, 0, 100, 100));
            size = new Vector2(100, 100);
        }
    }
}
