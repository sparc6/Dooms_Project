using System;
using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms
{
    public static class TimelineAnchorIndex
    {
        private static readonly List<TimelineAnchor> _allTimelines = new List<TimelineAnchor>();

        public static void Register(TimelineAnchor timeline)
        {
            if (timeline == null) return;
            if (!_allTimelines.Contains(timeline))
            {
                _allTimelines.Add(timeline);
            }
        }

        public static void Unregister(TimelineAnchor timeline)
        {
            if (timeline == null) return;
            _allTimelines.Remove(timeline);
        }

        public static TimelineAnchor Find(string timelineAnchorId)
        {
            if (string.IsNullOrEmpty(timelineAnchorId)) return null;
            return _allTimelines.Find(t => string.Equals(t.timelineAnchorId, timelineAnchorId, StringComparison.OrdinalIgnoreCase));
        }

        public static void Clear()
        {
            _allTimelines.Clear();
        }
    }
}
