using UnityEngine;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Master on/off switch for the DOOMS narrative add-on.
    ///
    /// This MonoBehaviour is the single place a client can enable or disable
    /// the DOOMS pipeline from the Unity Inspector. It exposes a compact set
    /// of top-level parameters that apply to the whole scenario.
    ///
    /// Deletion: removing the Dooms/ folder (Inspector components + this file)
    /// leaves the rest of the project unchanged. No other Unity script under
    /// Assets/Scripts/ depends on anything in MLA_SIM.Dooms.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MLA_SIM/DOOMS/Dooms Toggle")]
    public class DoomsToggle : MonoBehaviour
    {
        [Header("Master")]
        [Tooltip("Master on/off. When false, no config is pushed to the backend and the DOOMS pipeline stays dormant.")]
        public bool enableDooms = false;

        [Tooltip("Free-form scenario identifier. Default 'dooms'. Used as the folder name under configs/scenarios/ on the backend cache.")]
        public string scenarioId = "dooms";

        [Header("Config Push")]
        [Tooltip("Push the current Inspector configuration to the backend on scene start.")]
        public bool pushOnStart = true;

        [Tooltip("Push the configuration again whenever Inspector values change at edit-time. Runtime edits still require manual re-push.")]
        public bool pushOnValidate = false;

        [Header("World State Polling")]
        [Tooltip("Poll /api/dooms/state for the current beat and camera focus hint. Disable to run in strict offline/local mode.")]
        public bool pollWorldState = true;

        [Tooltip("Interval in seconds between /api/dooms/state polls.")]
        [Range(0.25f, 10f)]
        public float worldStatePollIntervalSec = 1.5f;

        [Header("Safety")]
        [Tooltip("When DOOMS is active, silence any WorldNarrator LLM calls. Prevents the unconstrained narrator from competing with the Director.")]
        public bool silenceWorldNarrator = true;

        [Header("Phase D — World Narrator (constrained)")]
        [Tooltip("Enable the DOOMS-aware World Narrator. The backend produces short news-style headlines from telemetry; they appear in the news ticker and bias the deterministic beat scheduler.")]
        public bool enableWorldNarrator = true;

        [Tooltip("How strongly the narrator's suggested_beat_bias influences beat selection (0 = ignored, 1 = on par with pressures). Sent to the backend on every config push.")]
        [Range(0f, 1f)]
        public float narratorInfluence = 0.3f;

        [Tooltip("Interval in seconds between news-ticker polls of /api/dooms/state.scene_narrative.")]
        [Range(0.5f, 30f)]
        public float narratorPollIntervalSec = 4f;

        [Tooltip("How often the backend narrator loop ticks (seconds). Lower = more reactive, more LLM calls.")]
        [Range(2f, 60f)]
        public float narratorTickSec = 5f;

        [Tooltip("Minimum seconds the beat orchestrator stays on a beat before switching (anti-thrash dwell).")]
        [Range(5f, 120f)]
        public float narratorMinDwellSec = 20f;

        [Tooltip("Max tokens for a single narrator LLM call. Keep low (150-300) to save context budget.")]
        [Range(50, 500)]
        public int narratorMaxTokens = 220;

        [Tooltip("LLM temperature for the narrator. Lower = more deterministic/factual headlines.")]
        [Range(0f, 1f)]
        public float narratorTemperature = 0.5f;

        [Tooltip("Override the narrator system prompt. Leave empty to use configs/scenarios/dooms/narrator_prompt.json. Multi-line.")]
        [TextArea(3, 8)]
        public string narratorSystemPromptOverride = "";

        [Tooltip("Override the allowed theme tags (one per line). Leave empty to use defaults from the prompt file.")]
        [TextArea(2, 6)]
        public string narratorThemesOverride = "";

        [Header("Ongoing Story Generation")]
        [Tooltip("The starting setup/premise of the story. The narrator uses this to initiate the narrative continuation loop.")]
        [TextArea(5, 12)]
        public string startingNarrative = "The great under-construction tower stands as a monument to progress and power. Builders work tirelessly on scaffolding, while server managers tend to row upon row of data nodes. But beneath the orderly facade, deep anxieties are brewing. Whispers of apocalyptic visions seen inside the DOOM room are beginning to spread...";

        [Tooltip("Guidelines for the narrator on how to construct, pacing, and style the ongoing narrative (e.g. 'Build suspense slowly', 'Reflect builder anxiety').")]
        public string[] narrativePrinciples = new string[] {
            "Maintain a dark, slightly eerie, pseudo-religious atmosphere about the technological environment.",
            "Integrate agent telemetry and spatial triggers (such as visiting the server/DOOM rooms) directly into the story events.",
            "Escalate tensions gradually through building rumors, anti-tech sentiments, and protest plans.",
            "Contrast the orderly visual labor of construction with the spiritual and existential dread of server visions."
        };

        /// <summary>
        /// Public accessor used by other DOOMS components to check whether
        /// the add-on should be active. Centralized here so all DOOMS
        /// components share the same source of truth.
        /// </summary>
        public static bool IsActive
        {
            get
            {
                var t = Object.FindFirstObjectByType<DoomsToggle>();
                return t != null && t.enableDooms;
            }
        }
    }
}
