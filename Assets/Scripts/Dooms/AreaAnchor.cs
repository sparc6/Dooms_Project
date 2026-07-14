using System;
using System.Collections.Generic;
using UnityEngine;
using MLA_SIM.Dooms.Scenes;

namespace MLA_SIM.Dooms
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    [AddComponentMenu("DOOMS/Area Anchor")]
    public class AreaAnchor : MonoBehaviour
    {
        [Header("Area tags")]
        [Tooltip("Primary tag for this area (legacy-compatible).")]
        [RegistryDropdown(RegistryType.InteractionPoint)]
        public string areaTag = "";

        [Tooltip("Additional tags this area can satisfy. Empty means areaTag only.")]
        [RegistryDropdown(RegistryType.InteractionPoint)]
        public List<string> areaTags = new List<string>();

        [Tooltip("Optional faction filter. Empty = any faction allowed.")]
        public List<string> allowedFactions = new List<string>();

        [Header("Scene Gating")]
        [Tooltip("When non-empty, this AreaAnchor only registers in the index while one of these scenes is active. " +
                 "Empty = always active (backward compatible).")]
        public SceneSO[] activeInScenes = new SceneSO[0];

        [Header("Roaming parameters")]
        public float roamMinDwellSec = 3f;
        public float roamMaxDwellSec = 8f;
        [ActivityNameDropdown]
        [Tooltip("Optional pair activity id to prefer when this area hosts an encounter. Empty = resolver chooses from relation defaults.")]
        public string defaultPairActivity = "";

        [Serializable]
        public class SceneTagOverride
        {
            [Tooltip("Scene for which this tag set applies.")]
            public SceneSO scene;

            [Tooltip("Tags used while this scene is active. Empty means fallback to areaTag + areaTags.")]
            [RegistryDropdown(RegistryType.InteractionPoint)]
            public List<string> tags = new List<string>();
        }

        [Header("Scene-specific tags")]
        [Tooltip("Optional per-scene tag remap. Lets one area play different semantic roles in different scenes.")]
        public List<SceneTagOverride> sceneTagOverrides = new List<SceneTagOverride>();

        [System.Serializable]
        public class PointOfInterest
        {
            [Tooltip("Child transform that marks the point of interest location.")]
            public Transform pointTransform;

            [Tooltip("Optional animation sequence ID for dwell/playback at this POI.")]
            [RegistryDropdown(RegistryType.AnimationSequence)]
            public string sequenceId = "";

            [Tooltip("Optional fallback animator state name when no sequence is assigned.")]
            [RegistryDropdown(RegistryType.AnimationState)]
            public string animatorStateName = "";

            [Tooltip("Optional hold duration override for this POI. <= 0 uses the area/activity default.")]
            public float holdSeconds = -1f;

            [Tooltip("Optional faction lock for this POI. Empty = usable by any faction.")]
            [RegistryDropdown(RegistryType.Faction)]
            public string factionId = "";

            [System.NonSerialized]
            private HashSet<string> _occupants = new HashSet<string>();

            public Transform GetAnchor() => pointTransform != null ? pointTransform : null;
            public Vector3 Position => pointTransform != null ? pointTransform.position : Vector3.zero;
            public int OccupancyCount => _occupants != null ? _occupants.Count : 0;
            public bool HasFreeSlot => OccupancyCount == 0;

            public bool IsOccupiedBy(string agentId)
            {
                return !string.IsNullOrEmpty(agentId) && _occupants != null && _occupants.Contains(agentId);
            }

            public bool TryOccupy(string agentId)
            {
                if (string.IsNullOrEmpty(agentId)) return false;
                if (_occupants == null) _occupants = new HashSet<string>();
                if (_occupants.Contains(agentId)) return true;
                if (_occupants.Count > 0) return false;
                _occupants.Add(agentId);
                return true;
            }

            public void Release(string agentId)
            {
                if (string.IsNullOrEmpty(agentId) || _occupants == null) return;
                _occupants.Remove(agentId);
            }

            public string ResolveAnimationId(string fallback)
            {
                if (!string.IsNullOrEmpty(sequenceId)) return sequenceId;
                if (!string.IsNullOrEmpty(animatorStateName)) return animatorStateName;
                return fallback;
            }

            public float ResolveHoldSeconds(float fallback)
            {
                return holdSeconds > 0f ? holdSeconds : fallback;
            }

            public bool IsFactionAllowed(string faction)
            {
                if (string.IsNullOrEmpty(factionId)) return true;
                if (string.IsNullOrEmpty(faction)) return false;
                return string.Equals(factionId, faction, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Header("Points of Interest")]
        [Tooltip("Optional authored POIs inside the area. Leave empty for random roam only.")]
        public List<PointOfInterest> pointsOfInterest = new List<PointOfInterest>();

        private Collider _collider;
        private readonly HashSet<string> _occupants = new HashSet<string>();

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            if (_collider != null)
            {
                _collider.isTrigger = true;
            }
        }

        private void OnValidate()
        {
            if (!string.IsNullOrEmpty(areaTag))
            {
                if (areaTags == null) areaTags = new List<string>();
                bool found = false;
                for (int i = 0; i < areaTags.Count; i++)
                {
                    if (string.Equals(areaTags[i], areaTag, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    areaTags.Insert(0, areaTag);
            }
        }

        public List<string> GetTagsForScene(string sceneId)
        {
            var tags = new List<string>();

            if (!string.IsNullOrEmpty(sceneId) && sceneTagOverrides != null)
            {
                for (int i = 0; i < sceneTagOverrides.Count; i++)
                {
                    var ov = sceneTagOverrides[i];
                    if (ov == null || ov.scene == null) continue;
                    if (!string.Equals(ov.scene.sceneId, sceneId, StringComparison.OrdinalIgnoreCase)) continue;
                    AddUnique(tags, ov.tags);
                    break;
                }
            }

            if (tags.Count == 0)
            {
                if (!string.IsNullOrEmpty(areaTag)) AddUnique(tags, areaTag);
                AddUnique(tags, areaTags);
            }

            return tags;
        }

        public string GetPrimaryTag(string sceneId = "")
        {
            var tags = GetTagsForScene(sceneId);
            return tags.Count > 0 ? tags[0] : "";
        }

        public bool MatchesTag(string tag, string sceneId = "")
        {
            if (string.IsNullOrEmpty(tag)) return false;
            var tags = GetTagsForScene(sceneId);
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void AddUnique(List<string> sink, string value)
        {
            if (sink == null || string.IsNullOrEmpty(value)) return;
            for (int i = 0; i < sink.Count; i++)
            {
                if (string.Equals(sink[i], value, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            sink.Add(value);
        }

        private static void AddUnique(List<string> sink, List<string> source)
        {
            if (sink == null || source == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                AddUnique(sink, source[i]);
            }
        }

        public bool IsFactionAllowed(string factionId)
        {
            if (allowedFactions == null || allowedFactions.Count == 0) return true;
            if (string.IsNullOrEmpty(factionId)) return false;
            for (int i = 0; i < allowedFactions.Count; i++)
            {
                if (string.Equals(allowedFactions[i], factionId, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public int OccupancyCount => _occupants.Count;

        public bool TryOccupy(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return false;
            if (_occupants.Contains(agentId)) return true;
            _occupants.Add(agentId);
            return true;
        }

        public void Release(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return;
            _occupants.Remove(agentId);
        }

        public bool TryGetNearestFreePointOfInterest(Vector3 origin, string agentId, out PointOfInterest pointOfInterest)
        {
            return TryGetNearestFreePointOfInterest(origin, agentId, "", out pointOfInterest);
        }

        public bool TryGetNearestFreePointOfInterest(Vector3 origin, string agentId, string factionId, out PointOfInterest pointOfInterest)
        {
            pointOfInterest = null;

            if (pointsOfInterest == null || pointsOfInterest.Count == 0)
            {
                return false;
            }

            float bestDistance = float.MaxValue;
            bool foundFactionSpecific = false;
            for (int i = 0; i < pointsOfInterest.Count; i++)
            {
                var candidate = pointsOfInterest[i];
                if (candidate == null || candidate.GetAnchor() == null) continue;
                if (!candidate.HasFreeSlot && !candidate.IsOccupiedBy(agentId)) continue;
                if (!candidate.IsFactionAllowed(factionId)) continue;

                bool isFactionSpecific = !string.IsNullOrEmpty(candidate.factionId)
                                         && !string.IsNullOrEmpty(factionId)
                                         && string.Equals(candidate.factionId, factionId, StringComparison.OrdinalIgnoreCase);
                if (foundFactionSpecific && !isFactionSpecific) continue;
                if (isFactionSpecific && !foundFactionSpecific)
                {
                    foundFactionSpecific = true;
                    pointOfInterest = null;
                    bestDistance = float.MaxValue;
                }

                float distance = Vector3.Distance(origin, candidate.Position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    pointOfInterest = candidate;
                }
            }

            if (pointOfInterest == null)
            {
                return false;
            }

            return pointOfInterest.TryOccupy(agentId);
        }

        public void ReleasePointOfInterest(PointOfInterest pointOfInterest, string agentId)
        {
            if (pointOfInterest == null) return;
            pointOfInterest.Release(agentId);
        }

        /// <summary>
        /// Sample a random valid point within the area bounds on the NavMesh.
        /// </summary>
        public Vector3 GetRandomPointWithin()
        {
            if (_collider == null) _collider = GetComponent<Collider>();
            
            Bounds b = _collider != null ? _collider.bounds : new Bounds(transform.position, Vector3.one * 5f);
            
            // Try up to 10 times to find a sample point
            for (int i = 0; i < 10; i++)
            {
                float rx = UnityEngine.Random.Range(b.min.x, b.max.x);
                float ry = UnityEngine.Random.Range(b.min.y, b.max.y);
                float rz = UnityEngine.Random.Range(b.min.z, b.max.z);
                Vector3 randPt = new Vector3(rx, ry, rz);

                if (_collider != null && !_collider.bounds.Contains(randPt))
                {
                    randPt = _collider.ClosestPoint(randPt);
                }

                // Sample NavMesh near the point
                UnityEngine.AI.NavMeshHit hit;
                if (UnityEngine.AI.NavMesh.SamplePosition(randPt, out hit, 4f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    return hit.position;
                }
            }

            return transform.position;
        }

        /// <summary>
        /// Returns true if this anchor is scene-gated AND the given sceneId matches
        /// one of its activeInScenes entries — or if it has no scene filter (always active).
        /// </summary>
        public bool IsActiveForScene(string sceneId)
        {
            if (activeInScenes == null || activeInScenes.Length == 0) return true;
            if (string.IsNullOrEmpty(sceneId)) return false;
            for (int i = 0; i < activeInScenes.Length; i++)
            {
                if (activeInScenes[i] != null &&
                    string.Equals(activeInScenes[i].sceneId, sceneId, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Re-evaluate whether this anchor should be in the index for the given active scene.
        /// Called by AreaAnchorIndex.RefreshAll() on scene transitions.
        /// </summary>
        public void RefreshRegistration(string activeSceneId)
        {
            if (!isActiveAndEnabled) return;
            if (IsActiveForScene(activeSceneId))
                AreaAnchorIndex.Register(this);
            else
                AreaAnchorIndex.Unregister(this);
        }

        private void OnEnable()
        {
            // Register unconditionally on enable; scene gating is applied by RefreshAll().
            // This preserves backward compat: ungated anchors are always registered.
            if (activeInScenes == null || activeInScenes.Length == 0)
                AreaAnchorIndex.Register(this);
        }

        private void OnDisable()
        {
            AreaAnchorIndex.Unregister(this);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (_collider == null) _collider = GetComponent<Collider>();
            if (_collider == null) return;

            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.2f);
            if (_collider is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.7f);
                Gizmos.DrawWireCube(box.center, box.size);
            }
            else
            {
                Gizmos.DrawCube(_collider.bounds.center, _collider.bounds.size);
                Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.7f);
                Gizmos.DrawWireCube(_collider.bounds.center, _collider.bounds.size);
            }
        }
#endif
    }
}
