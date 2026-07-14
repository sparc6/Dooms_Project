using System;
using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms
{
    [CreateAssetMenu(fileName = "ActivityCatalog", menuName = "DOOMS/Activity Catalog")]
    public class ActivityCatalogSO : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Faction this override applies to. Leave empty for no faction-specific override.")]
            public string factionId = "";
            // NOTE: This is the override-bucket key. Individual activity.factionId is
            // runtime metadata used for animation/selection filtering and does not
            // replace this list-level override routing key.
            public List<DoomsAgentT4Brain.Activity> activities = new List<DoomsAgentT4Brain.Activity>();
        }

        private static ActivityCatalogSO _instance;
        public static ActivityCatalogSO Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<ActivityCatalogSO>("Dooms/ActivityCatalog");
                    if (_instance == null)
                    {
                        var all = Resources.FindObjectsOfTypeAll<ActivityCatalogSO>();
                        if (all.Length > 0) _instance = all[0];
                    }
                }
                return _instance;
            }
        }

        [Header("Shared Activities")]
        [Tooltip("Default activities shared by all factions.")]
        public List<DoomsAgentT4Brain.Activity> shared = new List<DoomsAgentT4Brain.Activity>();

        [Header("Faction Overrides")]
        [Tooltip("Optional per-faction activity lists that override shared entries by targetClass.")]
        public List<Entry> factionOverrides = new List<Entry>();

        public List<DoomsAgentT4Brain.Activity> Resolve(string factionId)
        {
            var resolved = CloneList(shared);
            if (factionOverrides == null || factionOverrides.Count == 0)
            {
                return resolved;
            }

            for (int i = 0; i < factionOverrides.Count; i++)
            {
                var entry = factionOverrides[i];
                if (entry == null || string.IsNullOrEmpty(entry.factionId)) continue;
                if (!string.Equals(entry.factionId, factionId, StringComparison.OrdinalIgnoreCase)) continue;
                ApplyOverride(resolved, entry.activities);
            }

            return resolved;
        }

        private static void ApplyOverride(List<DoomsAgentT4Brain.Activity> target, List<DoomsAgentT4Brain.Activity> overrides)
        {
            if (target == null || overrides == null) return;

            for (int i = 0; i < overrides.Count; i++)
            {
                var ov = overrides[i];
                if (ov == null) continue;

                int replaceIndex = -1;
                for (int t = 0; t < target.Count; t++)
                {
                    var existing = target[t];
                    if (existing == null) continue;
                    if (!string.IsNullOrEmpty(ov.targetClass)
                        && string.Equals(existing.targetClass, ov.targetClass, StringComparison.OrdinalIgnoreCase))
                    {
                        replaceIndex = t;
                        break;
                    }
                }

                if (replaceIndex >= 0)
                    target[replaceIndex] = CloneActivity(ov);
                else
                    target.Add(CloneActivity(ov));
            }
        }

        private static List<DoomsAgentT4Brain.Activity> CloneList(List<DoomsAgentT4Brain.Activity> source)
        {
            var result = new List<DoomsAgentT4Brain.Activity>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                var a = source[i];
                if (a == null) continue;
                result.Add(CloneActivity(a));
            }
            return result;
        }

        private static DoomsAgentT4Brain.Activity CloneActivity(DoomsAgentT4Brain.Activity src)
        {
            return new DoomsAgentT4Brain.Activity
            {
                activityName = src.activityName,
                targetClass = src.targetClass,
                animatorStateName = src.animatorStateName,
                sequenceId = src.sequenceId,
                propId = src.propId,
                factionId = src.factionId,
                hostilityTag = src.hostilityTag,
                participantMode = src.participantMode,
                infectious = src.infectious,
                holdSeconds = src.holdSeconds,
                restoresNeed = src.restoresNeed,
                restoreAmount = src.restoreAmount,
                needWeight = src.needWeight,
                timeStartHour = src.timeStartHour,
                timeEndHour = src.timeEndHour,
                timeBonus = src.timeBonus,
                directiveMatchAction = src.directiveMatchAction
            };
        }
    }
}
