using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// One DOOMS faction, fully Inspector-configured. Drop one of these on
    /// an empty GameObject per faction (builders, server, neo_luddite,
    /// security, clone_king). DoomsConfigPusher discovers all instances
    /// at runtime and serializes them into the /api/dooms/config payload.
    ///
    /// Routine masks: per beat id, list of routine action names that the
    /// backend `routine_actions.pick` may select for tier-3 agents of this
    /// faction. Beat id "*" acts as a wildcard fallback when the current
    /// beat has no explicit entry.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MLA_SIM/DOOMS/Dooms Faction Config")]
    public class DoomsFactionConfig : MonoBehaviour
    {
        [System.Serializable]
        public class RoutineMaskEntry
        {
            [Tooltip("Beat id. Use \"*\" for a wildcard mask that applies when the current beat is unspecified.")]
            public string beatId = "*";

            [Tooltip("Allowed routine action names for this faction during this beat.")]
            public List<string> routineNames = new List<string>();
        }

        [System.Serializable]
        public class NamedFloat
        {
            public string key = "";
            public float value = 0f;
        }

        [Header("Identity")]
        [Tooltip("Faction id. Must match the factionId on DoomsAgentTag components.")]
        public string factionId = "builders";

        [Header("Routine Masks (per beat)")]
        public List<RoutineMaskEntry> routineMaskPerBeat = new List<RoutineMaskEntry>
        {
            new RoutineMaskEntry { beatId = "*", routineNames = new List<string> { "Idle" } },
        };

        [Header("Pressure Thresholds")]
        public List<NamedFloat> pressureThresholds = new List<NamedFloat>();

        [Header("Default Emotional State")]
        public List<NamedFloat> defaultEmotionalState = new List<NamedFloat>();
    }
}
