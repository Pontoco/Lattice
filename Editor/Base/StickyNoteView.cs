using Unity.Mathematics;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor.Views
{
    public class StickyNoteView : StickyNote
    {
        public Base.StickyNote Note;

        public StickyNoteView()
        {
            fontSize = StickyNoteFontSize.Small;
            theme = StickyNoteTheme.Classic;
        }

        public void Initialize(BaseGraphView graphView, Base.StickyNote note)
        {
            Note = note;

            this.Q<TextField>("title-field").RegisterCallback<ChangeEvent<string>>(e =>
            {
                note.title = e.newValue;
            });
            this.Q<TextField>("contents-field").RegisterCallback<ChangeEvent<string>>(e =>
            {
                note.content = e.newValue;
            });

            title = note.title;
            contents = note.content;
            
            SetPosition(note.position);
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);

            if (Note != null)
            {
                Note.position = newPos.position;
            }
        }
        
        public void SetPosition(float2 pos)
        {
            var rect = GetPosition();
            rect.position = pos;
            SetPosition(rect);
        }

        public override void OnResized()
        {
            Note.position = layout.position;
        }
    }
}
