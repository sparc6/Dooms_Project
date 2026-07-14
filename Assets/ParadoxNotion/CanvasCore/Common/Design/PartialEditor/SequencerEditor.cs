#if UNITY_EDITOR

using System.Collections.Generic;
using System.Linq;
using ParadoxNotion.Animation;
using UnityEditor;
using UnityEngine;

namespace ParadoxNotion.Design
{

    public static class SequencerEditor
    {

        private static ISequencableTrack currentClickedTrack;

        ///Show a minisequencer for all tracks using GUILayout.
        public static void ShowTracks(float trackHeight, IEnumerable<ISequencableTrack> tracks, float fixedLength, float currentTime) {

            fixedLength = ResolveTotalLength(tracks, fixedLength, currentTime);

            GUILayout.Space(2);
            GUILayout.BeginVertical();

            GUILayout.Box(string.Empty, GUIStyle.none, GUILayout.Height(14), GUILayout.ExpandWidth(true));
            var timelineRect = GUILayoutUtility.GetLastRect();
            var trackSpace = 2;
            DrawTimes(timelineRect, trackHeight + trackSpace, tracks, fixedLength);

            GUILayout.Space(trackSpace);

            foreach ( var track in tracks ) {
                GUILayout.Box(string.Empty, GUIStyle.none, GUILayout.Height(trackHeight), GUILayout.ExpandWidth(true));
                var lastRect = GUILayoutUtility.GetLastRect();
                ShowTrack(lastRect, track, fixedLength);
                GUILayout.Space(trackSpace);
            }

            GUILayout.EndVertical();

            if ( currentTime > 0 ) {
                var lastRect = GUILayoutUtility.GetLastRect();
                DrawRuntimeCarret(lastRect, fixedLength, currentTime);
            }
        }

        ///...
        public static float ResolveTotalLength(IEnumerable<ISequencableTrack> tracks, float fixedLenth, float currentTime) {
            if ( fixedLenth <= 0 ) {
                foreach ( var track in tracks ) {
                    fixedLenth = Mathf.Max(fixedLenth, track.time + track.length);
                }
                return Mathf.Max(fixedLenth, currentTime, 9) + 1;
            }
            return fixedLenth;
        }

        ///...
        public static void DrawTimes(Rect rect, float trackHeight, IEnumerable<ISequencableTrack> tracks, float fixedLenth) {
            var previousTimePos = float.NegativeInfinity;
            for ( var i = 0; i <= Mathf.FloorToInt(fixedLenth); i++ ) {
                var timePos = Mathf.Lerp(rect.x, rect.xMax, Mathf.InverseLerp(0, fixedLenth, i));
                if ( timePos - previousTimePos < 20 ) {
                    continue;
                }
                previousTimePos = timePos;
                var timeRect = new Rect(timePos - 1, rect.yMax - 2, 2, 2);
                var labelRect = new Rect(0, 0, 20, 20);
                labelRect.center = new Vector2(timePos, timeRect.y - 10);
                var timeLineRect = timeRect;
                timeLineRect.width = 1;
                timeLineRect.height = tracks.Count() * trackHeight;
                GUI.color = Color.black.WithAlpha(0.2f);
                GUI.DrawTexture(timeLineRect, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.DrawTexture(timeRect, Texture2D.whiteTexture);
                GUI.Label(labelRect, string.Format("<size=9>{0}</size>", i.ToString()), Styles.bottomCenterLabel);
            }
        }

        ///...
        public static void ShowTrack(Rect rect, ISequencableTrack track, float fixedLength) {
            GUI.color = Color.white.WithAlpha(0.5f);
            GUI.Box(rect, string.Empty, Styles.roundedBox);
            GUI.color = Color.white;
            var timePos = Mathf.Lerp(rect.x, rect.xMax, Mathf.InverseLerp(0, fixedLength, track.time));
            var lengthPos = Mathf.Lerp(rect.x, rect.xMax, Mathf.InverseLerp(0, fixedLength, track.time + track.length));
            var clipRect = Rect.MinMaxRect(timePos, rect.y + 2, lengthPos, rect.yMax - 2);
            clipRect.width = Mathf.Max(clipRect.width, 4);

            GUI.color = Color.grey;
            GUI.DrawTexture(clipRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            var labelRect = rect;
            labelRect.x += 4;
            GUI.color = track == currentClickedTrack ? Color.white.WithAlpha(0.4f) : Color.white;
            GUI.Label(labelRect, track.name, Styles.leftLabel);
            GUI.color = Color.white;

            EditorGUIUtility.AddCursorRect(clipRect, MouseCursor.ResizeHorizontal);
            var e = Event.current;
            if ( clipRect.Contains(e.mousePosition) && e.type == EventType.MouseDown && e.button == 0 ) {
                currentClickedTrack = track;
                e.Use();
            }
            if ( currentClickedTrack == track ) {
                GUI.Label(Rect.MinMaxRect(rect.xMin, rect.yMin, clipRect.xMin, rect.yMax), track.time.ToString(".00"), Styles.rightLabel);
                if ( e.type == EventType.MouseDrag ) {
                    timePos += e.delta.x;
                    track.time = Mathf.Lerp(0, fixedLength, Mathf.InverseLerp(rect.x, rect.xMax, timePos));
                }
            }
            if ( e.rawType == EventType.MouseUp ) {
                currentClickedTrack = null;
            }
        }

        ///...
        public static void DrawRuntimeCarret(Rect rect, float fixedLength, float currentTime) {
            if ( currentTime > 0 ) {
                var currentTimePos = Mathf.Lerp(rect.x, rect.xMax, Mathf.InverseLerp(0, fixedLength, currentTime));
                var carretRect = new Rect(currentTimePos, rect.y, 3, rect.height);
                GUI.color = Color.yellow;
                GUI.DrawTexture(carretRect, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }
    }
}

#endif