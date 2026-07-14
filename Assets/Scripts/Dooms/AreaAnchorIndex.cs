using System.Collections.Generic;
using UnityEngine;
using MLA_SIM.Dooms.Scenes;

namespace MLA_SIM.Dooms
{
    public static class AreaAnchorIndex
    {
        private static readonly List<AreaAnchor> _allAreas = new List<AreaAnchor>();

        public static void Register(AreaAnchor area)
        {
            if (area == null) return;
            if (!_allAreas.Contains(area))
            {
                _allAreas.Add(area);
            }
        }

        public static void Unregister(AreaAnchor area)
        {
            if (area == null) return;
            _allAreas.Remove(area);
        }

        public static IEnumerable<AreaAnchor> Query(string areaTag, string factionId = null)
        {
            var results = new List<AreaAnchor>();
            string sceneId = ActiveSceneId();
            for (int i = 0; i < _allAreas.Count; i++)
            {
                var a = _allAreas[i];
                if (a == null) continue;

                if (a.MatchesTag(areaTag, sceneId))
                {
                    if (string.IsNullOrEmpty(factionId) || a.IsFactionAllowed(factionId))
                    {
                        results.Add(a);
                    }
                }
            }
            return results;
        }

        public static AreaAnchor Nearest(Vector3 origin, string areaTag, string factionId = null)
        {
            AreaAnchor best = null;
            float bestDist = float.MaxValue;
            string sceneId = ActiveSceneId();

            for (int i = 0; i < _allAreas.Count; i++)
            {
                var a = _allAreas[i];
                if (a == null) continue;

                if (a.MatchesTag(areaTag, sceneId))
                {
                    if (string.IsNullOrEmpty(factionId) || a.IsFactionAllowed(factionId))
                    {
                        float dist = Vector3.Distance(origin, a.transform.position);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = a;
                        }
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Re-evaluate all AreaAnchors in the scene for the given active sceneId.
        /// Scene-gated anchors are registered/unregistered as appropriate.
        /// Called by SceneDirector on phase/scene transitions.
        /// </summary>
        public static void RefreshAll(string activeSceneId)
        {
            var all = UnityEngine.Object.FindObjectsByType<AreaAnchor>(UnityEngine.FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null) all[i].RefreshRegistration(activeSceneId);
            }
        }

        public static void Clear()
        {
            _allAreas.Clear();
        }

        private static string ActiveSceneId()
        {
            if (SceneDirector.Instance == null || SceneDirector.Instance.CurrentContext == null || SceneDirector.Instance.CurrentContext.scene == null)
                return "";
            return SceneDirector.Instance.CurrentContext.scene.sceneId;
        }
    }
}
