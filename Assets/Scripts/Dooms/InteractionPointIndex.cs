using System;
using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms
{
    public static class InteractionPointIndex
    {
        private static readonly List<InteractionPoint> _allPoints = new List<InteractionPoint>();

        public static void Register(InteractionPoint point)
        {
            if (point == null) return;
            if (!_allPoints.Contains(point))
            {
                _allPoints.Add(point);
            }
        }

        public static void Unregister(InteractionPoint point)
        {
            if (point == null) return;
            _allPoints.Remove(point);
        }

        public static IEnumerable<InteractionPoint> Query(string pointTag, string factionId = null)
        {
            var results = new List<InteractionPoint>();
            for (int i = 0; i < _allPoints.Count; i++)
            {
                var p = _allPoints[i];
                if (p == null) continue;

                // Match tag (case-insensitive)
                if (string.Equals(p.pointTag, pointTag, StringComparison.OrdinalIgnoreCase))
                {
                    // Check faction and capacity
                    if (p.HasFreeSlot && (string.IsNullOrEmpty(factionId) || p.IsFactionAllowed(factionId)))
                    {
                        results.Add(p);
                    }
                }
            }
            return results;
        }

        /// <summary>
        /// True if at least one InteractionPoint with this tag exists for the faction,
        /// regardless of whether it currently has a free slot. Lets callers prefer
        /// "wait for a real point" over substituting an AreaAnchor random-spot fallback.
        /// </summary>
        public static bool Exists(string pointTag, string factionId = null)
        {
            for (int i = 0; i < _allPoints.Count; i++)
            {
                var p = _allPoints[i];
                if (p == null) continue;
                if (string.Equals(p.pointTag, pointTag, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrEmpty(factionId) || p.IsFactionAllowed(factionId)))
                {
                    return true;
                }
            }
            return false;
        }

        public static InteractionPoint Nearest(Vector3 origin, string pointTag, string factionId = null)
        {
            InteractionPoint best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < _allPoints.Count; i++)
            {
                var p = _allPoints[i];
                if (p == null) continue;

                if (string.Equals(p.pointTag, pointTag, StringComparison.OrdinalIgnoreCase))
                {
                    if (p.HasFreeSlot && (string.IsNullOrEmpty(factionId) || p.IsFactionAllowed(factionId)))
                    {
                        float dist = Vector3.Distance(origin, p.GetAnchor().position);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = p;
                        }
                    }
                }
            }

            return best;
        }

        public static void Clear()
        {
            _allPoints.Clear();
        }
    }
}
