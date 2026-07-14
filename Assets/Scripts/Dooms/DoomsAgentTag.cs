using UnityEngine;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Marks an AgentController GameObject as participating in the DOOMS
    /// narrative pipeline. Pure Inspector configuration — no code anywhere
    /// else references this type by name; DoomsConfigPusher discovers it by
    /// reflection so deleting the Dooms/ folder leaves the rest of the
    /// project compiling.
    ///
    /// Tier semantics (matches agent_core/dooms/tier_dispatcher.py):
    ///   1 = LeadFull    -> full dual-loop reasoning. Caches reasoning for
    ///                      same-faction T2 shadows (Plan M6).
    ///   2 = LeadShadow  -> faction lead reusing T1 cached reasoning with a
    ///                      shorter prompt (Plan M6). Falls back to full
    ///                      reasoning when no T1 cache is available.
    ///   3 = Npc         -> deterministic backend pool picker (Plan M7).
    ///   4 = AnimationOnly -> backend short-circuits; Unity owns visuals.
    ///
    /// Removing this component from an agent immediately reverts that agent
    /// to the default (untagged) reasoned path on the next config push.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MLA_SIM/DOOMS/Dooms Agent Tag")]
    public class DoomsAgentTag : MonoBehaviour
    {
        private static int _t3Counter = 300;
        private static int _t4Counter = 400;

        public enum DoomsTier
        {
            LeadFull = 1,
            LeadShadow = 2,
            // Legacy alias kept so older scenes still deserialize. Treated
            // identically to LeadShadow at runtime.
            Lead = 2,
            Npc = 3,
            // Legacy alias preserved for older scenes.
            Routine = 3,
            AnimationOnly = 4,
        }

        [Header("Identity")]
        [Tooltip("Agent ID that matches AgentController.agentID. Leave empty to auto-bind from a sibling AgentController at Awake.")]
        public string agentId = "";

        [Header("DOOMS Tier")]
        [Tooltip("Reasoning tier. Lead uses full LLM. Routine uses deterministic backend picker. AnimationOnly is fully Unity-owned.")]
        public DoomsTier tierEnum = DoomsTier.Routine;

        [Tooltip("Numeric mirror of tierEnum, used for serialization. Updated automatically.")]
        public int tier = 3;

        [Header("Faction & Archetype")]
        [Tooltip("Faction id. Must match a DoomsFactionConfig.factionId in the scene.")]
        public string factionId = "";

        [Tooltip("Optional archetype label (e.g. 'preacher', 'recruit', 'patrol').")]
        public string archetype = "";

        [Header("Scene Reservation (Runtime)")]
        [Tooltip("Scene ID that currently has this agent reserved. Empty means free.")]
        public string reservedBySceneId = "";

        [Header("Camera")]
        [Tooltip("0..1 weight used by DoomsCameraDirector to bias focus toward this agent.")]
        [Range(0f, 1f)]
        public float visibilityImportance = 0f;

        void Awake()
        {
            // Mirror enum -> int so DoomsConfigPusher can serialize a stable int.
            tier = (int)tierEnum;
            TryAutoBind();
        }

        void Start()
        {
            TryAutoBind();
        }

        private void TryAutoBind()
        {
            if (string.IsNullOrWhiteSpace(agentId))
            {
                // Best-effort auto-bind. Uses reflection to avoid a hard
                // compile-time dependency on the core AgentController type;
                // if the core class is renamed, the tag still compiles.
                var ac = GetComponent("AgentController") ?? GetComponent("MLA_SIM.AgentController");
                if (ac == null)
                {
                    // Fallback to searching all components by Type name
                    foreach (var comp in GetComponents<Component>())
                    {
                        if (comp != null && comp.GetType().Name == "AgentController")
                        {
                            ac = comp;
                            break;
                        }
                    }
                }

                if (ac != null)
                {
                    var idField = ac.GetType().GetField("agentID");
                    if (idField != null)
                    {
                        var v = idField.GetValue(ac) as string;
                        if (!string.IsNullOrWhiteSpace(v))
                        {
                            agentId = v;
                            Debug.Log($"[DoomsAgentTag] Auto-bound empty AgentId to '{agentId}' on GameObject '{gameObject.name}'");
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(agentId))
                {
                    if (tierEnum == DoomsTier.Npc || tierEnum == DoomsTier.Routine)
                    {
                        agentId = $"dooms_t3_{_t3Counter++}";
                        Debug.Log($"[DoomsAgentTag] Auto-assigned T3 AgentId '{agentId}' on '{gameObject.name}'");
                    }
                    else if (tierEnum == DoomsTier.AnimationOnly)
                    {
                        agentId = $"dooms_t4_{_t4Counter++}";
                        Debug.Log($"[DoomsAgentTag] Auto-assigned T4 AgentId '{agentId}' on '{gameObject.name}'");
                    }
                }
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            tier = (int)tierEnum;
        }
#endif
    }
}
