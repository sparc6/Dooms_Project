#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;

namespace NodeCanvas.Editor
{

    //Shows a GUI within a popup. The delegate includes the gui calls
    public class QuickPopup : PopupWindowContent
    {

        private System.Action call;
        private Rect myRect = new Rect(0, 0, 200, 10);

        public static void Show(System.Action Call, Vector2 pos = default) {
            var e = Event.current;
            pos = pos == default ? new Vector2(e.mousePosition.x, e.mousePosition.y) : pos;
            var rect = new Rect(pos.x, pos.y, 0, 0);
            PopupWindow.Show(rect, new QuickPopup(Call));
        }

        public QuickPopup(System.Action call) { this.call = call; }
        public override Vector2 GetWindowSize() { return new Vector2(myRect.xMin + myRect.xMax, myRect.yMin + myRect.yMax); }
        public override void OnGUI(Rect rect) {
            GUILayout.BeginVertical(GUI.skin.box);
            call();
            GUILayout.EndVertical();
            if ( Event.current.type == EventType.Repaint ) {
                myRect = GUILayoutUtility.GetLastRect();
            }
        }
    }
}

#endif