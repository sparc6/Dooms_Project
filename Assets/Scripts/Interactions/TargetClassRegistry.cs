using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Interactions
{
    /// <summary>
    /// Plan M4: Process-wide registry of <see cref="TargetTransformAnchor"/>
    /// instances, keyed by their <see cref="TargetTransformAnchor.targetClass"/>.
    ///
    /// Two consumer paths:
    ///   - Runtime: AgentActionSystem (M5) calls <see cref="FindClosestFree"/>
    ///     to resolve "Build at a ConstructionSite" into a concrete anchor.
    ///   - Backend: <see cref="ExportToJson"/> writes
    ///     ``configs/target_transforms.json`` so the npc_action_picker (M7)
    ///     and narrator (M3) can discover what target classes the scene
    ///     actually exposes.
    ///
    /// Pure runtime; no Inspector surface.
    /// </summary>
    public static class TargetClassRegistry
    {
        private static readonly Dictionary<string, List<TargetTransformAnchor>> _byClass
            = new Dictionary<string, List<TargetTransformAnchor>>(System.StringComparer.OrdinalIgnoreCase);

        public static void Register(TargetTransformAnchor anchor)
        {
            if (anchor == null) return;
            string cls = anchor.targetClass;
            if (string.IsNullOrEmpty(cls)) return;
            if (!_byClass.TryGetValue(cls, out var list))
            {
                list = new List<TargetTransformAnchor>();
                _byClass[cls] = list;
            }
            if (!list.Contains(anchor)) list.Add(anchor);
        }

        public static void Unregister(TargetTransformAnchor anchor)
        {
            if (anchor == null) return;
            foreach (var list in _byClass.Values)
            {
                list.Remove(anchor);
            }
        }

        public static IEnumerable<string> GetAllClasses() => _byClass.Keys;

        public static IReadOnlyList<TargetTransformAnchor> GetAnchorsOfClass(string targetClass)
        {
            if (string.IsNullOrEmpty(targetClass)) return System.Array.Empty<TargetTransformAnchor>();
            return _byClass.TryGetValue(targetClass, out var list)
                ? list
                : (IReadOnlyList<TargetTransformAnchor>)System.Array.Empty<TargetTransformAnchor>();
        }

        /// <summary>
        /// Find the closest free (HasFreeSlot && faction-allowed) anchor of the
        /// given class to the supplied position. Returns null if none.
        /// </summary>
        public static TargetTransformAnchor FindClosestFree(string targetClass, Vector3 fromPosition,
                                                            string factionId)
        {
            if (string.IsNullOrEmpty(targetClass)) return null;
            if (!_byClass.TryGetValue(targetClass, out var list) || list == null || list.Count == 0)
                return null;
            TargetTransformAnchor best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a == null) continue;
                if (!a.HasFreeSlot) continue;
                if (!a.IsFactionAllowed(factionId)) continue;
                float d = (a.GetAnchor().position - fromPosition).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = a; }
            }
            return best;
        }

        // ---------------------------------------------------------------
        // JSON exporter. Writes a stable, sorted snapshot of every target
        // class in the scene to configs/target_transforms.json so the
        // backend (npc_action_picker, narrator) sees the same target_classes
        // the scene actually carries.
        //
        // The schema mirrors how global_objects.json is structured: a top
        // level map keyed by class id, with anchor positions + capacity.
        // ---------------------------------------------------------------
        [System.Serializable]
        private class ExportAnchor
        {
            public string id;
            public Vector3 position;
            public int capacity;
            public bool infectious;
            public string[] allowed_factions;
        }

        [System.Serializable]
        private class ExportClass
        {
            public string class_id;
            public List<ExportAnchor> anchors = new List<ExportAnchor>();
        }

        [System.Serializable]
        private class ExportRoot
        {
            public string version = "1";
            public List<ExportClass> classes = new List<ExportClass>();
        }

        public static string BuildExportJson()
        {
            var root = new ExportRoot();
            var classKeys = new List<string>(_byClass.Keys);
            classKeys.Sort(System.StringComparer.OrdinalIgnoreCase);
            foreach (var k in classKeys)
            {
                var bucket = new ExportClass { class_id = k };
                var list = _byClass[k];
                if (list == null) { root.classes.Add(bucket); continue; }
                for (int i = 0; i < list.Count; i++)
                {
                    var a = list[i];
                    if (a == null) continue;
                    var t = a.GetAnchor();
                    bucket.anchors.Add(new ExportAnchor
                    {
                        id = a.gameObject.name,
                        position = t != null ? t.position : Vector3.zero,
                        capacity = Mathf.Max(1, a.capacity),
                        infectious = a.infectious,
                        allowed_factions = a.allowedFactions ?? new string[0],
                    });
                }
                root.classes.Add(bucket);
            }
            return JsonUtility.ToJson(root, prettyPrint: true);
        }

        /// <summary>
        /// Write the registry snapshot to ``configs/target_transforms.json``
        /// next to the project's other config files. Safe to call from a
        /// scene boot bootstrap or from a debug menu item.
        /// </summary>
        public static bool ExportToFile(string absoluteOrRelativePath = null)
        {
            try
            {
                string path = absoluteOrRelativePath;
                if (string.IsNullOrEmpty(path))
                {
                    // <project>/configs/target_transforms.json
                    string projectRoot = System.IO.Path.GetFullPath(
                        System.IO.Path.Combine(Application.dataPath, "..", "..", ".."));
                    path = System.IO.Path.Combine(projectRoot, "configs", "target_transforms.json");
                }
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }
                System.IO.File.WriteAllText(path, BuildExportJson());
                Debug.Log($"[TargetClassRegistry] Exported {_byClass.Count} target classes to {path}");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[TargetClassRegistry] Export failed: {e.Message}");
                return false;
            }
        }
    }
}
