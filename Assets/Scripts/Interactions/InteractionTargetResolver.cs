using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Interactions
{
    /// <summary>
    /// Resolves "target descriptors" stored as strings inside InteractionEffects
    /// to live scene InteractableObject instances.
    ///
    /// Uses a lazily-rebuilt cache backed by Object.FindObjectsOfType so that
    /// effects can reference targets by archetypeId or by GameObject.name without
    /// needing a direct scene reference (which is forbidden in ScriptableObject
    /// assets).
    /// </summary>
    public static class InteractionTargetResolver
    {
        private static readonly Dictionary<string, List<InteractableObject>> _byArchetype =
            new Dictionary<string, List<InteractableObject>>(System.StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, InteractableObject> _byName =
            new Dictionary<string, InteractableObject>(System.StringComparer.OrdinalIgnoreCase);
        private static float _lastRebuildTime = -1f;
        private const float RebuildInterval = 1.0f;

        public static void Invalidate()
        {
            _lastRebuildTime = -1f;
        }

        private static void EnsureFresh()
        {
            float now = Time.realtimeSinceStartup;
            if (_lastRebuildTime > 0f && (now - _lastRebuildTime) < RebuildInterval) return;

            _byArchetype.Clear();
            _byName.Clear();

#if UNITY_2023_1_OR_NEWER
            var all = Object.FindObjectsByType<InteractableObject>(FindObjectsSortMode.None);
#else
            var all = Object.FindObjectsOfType<InteractableObject>();
#endif
            foreach (var io in all)
            {
                if (io == null) continue;
                string aid = string.IsNullOrEmpty(io.archetypeId) ? io.GetObjectName() : io.archetypeId;
                if (!_byArchetype.TryGetValue(aid, out var list))
                {
                    list = new List<InteractableObject>();
                    _byArchetype[aid] = list;
                }
                list.Add(io);

                if (!string.IsNullOrEmpty(io.gameObject.name))
                    _byName[io.gameObject.name] = io;
            }
            _lastRebuildTime = now;
        }

        /// <summary>
        /// Resolve all matching InteractableObjects given a (archetypeId, objectName) pair.
        /// Empty fields are ignored. If both are empty, returns empty.
        /// </summary>
        public static IEnumerable<InteractableObject> Resolve(string archetypeId, string objectName)
        {
            EnsureFresh();
            if (!string.IsNullOrEmpty(objectName))
            {
                if (_byName.TryGetValue(objectName, out var single) && single != null)
                {
                    yield return single;
                }
                yield break;
            }
            if (!string.IsNullOrEmpty(archetypeId))
            {
                if (_byArchetype.TryGetValue(archetypeId, out var list))
                {
                    foreach (var io in list) if (io != null) yield return io;
                }
            }
        }

        /// <summary>
        /// Resolve as GameObjects (for SetActive). Includes objects whose
        /// InteractableObject is currently inactive.
        /// </summary>
        public static IEnumerable<GameObject> ResolveGameObjects(string archetypeId, string objectName)
        {
            foreach (var io in Resolve(archetypeId, objectName))
            {
                if (io != null) yield return io.gameObject;
            }
        }
    }
}
