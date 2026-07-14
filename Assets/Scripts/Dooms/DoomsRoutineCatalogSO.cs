using System;
using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// One block of an agent's day: between startHour and endHour, head to a
    /// targetClass InteractionPoint and perform there. An empty targetClass marks
    /// free/leisure time — the agent falls through to the procedural layer.
    /// </summary>
    [Serializable]
    public class RoutineBlock
    {
        public string label = "Block";
        [Range(0f, 24f)] public float startHour = 8f;
        [Range(0f, 24f)] public float endHour = 12f;

        [Tooltip("InteractionPoint class to use during this block. Empty = free/leisure (procedural).")]
        [RegistryDropdown(RegistryType.InteractionPoint)]
        public string targetClass = "";

        [Tooltip("Optional area confinement for this block.")]
        [RegistryDropdown(RegistryType.InteractionPoint)]
        public string areaTag = "";

        [Tooltip("Optional locomotion style applied while this block runs (e.g. 'Drunk' during Bar). Matches AnimatorLocomotionDriver loco style names. Empty = keep the agent's default loco style. Restored to default when the block ends. Does NOT override SceneSO directives (routine only runs when the agent is unreserved).")]
        public string locoStyle = "";

        [Tooltip("Chance per decision tick to do a short procedural filler instead of the scheduled task.")]
        [Range(0f, 1f)] public float socialSlack = 0.15f;

        [Tooltip("How long to hold the scheduled interaction (s).")]
        public float holdSeconds = 6f;
        public DoomsAgentNeeds.NeedType restoresNeed = DoomsAgentNeeds.NeedType.Duty;
        [Range(0f, 1f)] public float restoreAmount = 0.3f;
    }

    /// <summary>
    /// Shared, inspector-authored daily routines for T4 agents, keyed by faction.
    /// Place an asset at Resources/Dooms/RoutineCatalog for the singleton to
    /// auto-load (mirrors ActivityCatalogSO / ExtrasProfileSO).
    /// </summary>
    [CreateAssetMenu(fileName = "RoutineCatalog", menuName = "DOOMS/Routine Catalog")]
    public class DoomsRoutineCatalogSO : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Faction whose day this is. Its blocks REPLACE the shared day for that faction.")]
            public string factionId = "";
            public List<RoutineBlock> blocks = new List<RoutineBlock>();
        }

        private static DoomsRoutineCatalogSO _instance;
        public static DoomsRoutineCatalogSO Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<DoomsRoutineCatalogSO>("Dooms/RoutineCatalog");
                    if (_instance == null)
                    {
                        var all = Resources.FindObjectsOfTypeAll<DoomsRoutineCatalogSO>();
                        if (all != null && all.Length > 0) _instance = all[0];
                    }
                }
                return _instance;
            }
        }

        [Header("Shared day (fallback for factions without an override)")]
        public List<RoutineBlock> shared = new List<RoutineBlock>();

        [Header("Per-faction days (replace shared)")]
        public List<Entry> factionOverrides = new List<Entry>();

        public List<RoutineBlock> Resolve(string factionId)
        {
            if (!string.IsNullOrEmpty(factionId) && factionOverrides != null)
            {
                for (int i = 0; i < factionOverrides.Count; i++)
                {
                    var e = factionOverrides[i];
                    if (e == null || string.IsNullOrEmpty(e.factionId)) continue;
                    if (string.Equals(e.factionId, factionId, StringComparison.OrdinalIgnoreCase)
                        && e.blocks != null && e.blocks.Count > 0)
                        return e.blocks;
                }
            }
            return shared;
        }

        /// <summary>First block whose time window contains <paramref name="hour"/> (handles wraparound), or null.</summary>
        public RoutineBlock GetActiveBlock(string factionId, float hour)
        {
            var blocks = Resolve(factionId);
            if (blocks == null) return null;
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (b == null) continue;
                if (InWindow(hour, b.startHour, b.endHour)) return b;
            }
            return null;
        }

        public static bool InWindow(float h, float start, float end)
        {
            if (Mathf.Approximately(start, end)) return true;            // 24h block
            if (start < end) return h >= start && h < end;
            return h >= start || h < end;                                // wraparound (e.g. 21..6)
        }
    }
}
