using UnityEngine;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Shared, inspector-authored tuning for the procedural "extras" crowd layer
    /// driven by ExtrasDirector. Read-only at runtime. Place an asset under a
    /// Resources/Dooms/ folder named "ExtrasProfile" for the singleton to auto-load,
    /// or assign however your bootstrap prefers. If none exists, the director uses
    /// these defaults.
    /// </summary>
    [CreateAssetMenu(fileName = "ExtrasProfile", menuName = "DOOMS/Extras Profile")]
    public class ExtrasProfileSO : ScriptableObject
    {
        private static ExtrasProfileSO _instance;
        public static ExtrasProfileSO Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<ExtrasProfileSO>("Dooms/ExtrasProfile");
                    if (_instance == null)
                    {
                        var all = Resources.FindObjectsOfTypeAll<ExtrasProfileSO>();
                        if (all.Length > 0) _instance = all[0];
                    }
                }
                return _instance;
            }
        }

        [Header("Director cadence")]
        [Tooltip("Seconds between director coordination ticks.")]
        public float tickInterval = 1f;
        [Tooltip("How long (s) an agent keeps an assigned AreaAnchor goal before the director may re-route it for variety.")]
        public float goalDurationSec = 12f;

        [Header("Where agents go (area choice)")]
        [Tooltip("Bonus when an area explicitly lists the agent's faction in allowedFactions.")]
        public float factionHomeBonus = 0.5f;
        [Tooltip("Score penalty per metre of distance to the area.")]
        public float distancePenaltyPerMeter = 0.01f;
        [Tooltip("Score penalty scaled by how full the area already is (0..1 of capacity).")]
        public float crowdPenalty = 1.0f;
        [Tooltip("Score bonus (scaled by directive intensity) when the active faction directive targets this area's tag.")]
        public float directiveBias = 1.5f;
        [Tooltip("Fallback interest when no T4 activity matches an area's tag (lets agents still wander).")]
        public float baseWanderInterest = 0.05f;
        [Tooltip("Random jitter added to interest to de-sync and spread the crowd.")]
        public float interestJitter = 0.1f;
        [Tooltip("Assumed capacity for areas that have no authored POIs.")]
        public int softAreaCapacity = 4;
        [Tooltip("Agents farther than this from an area won't consider it.")]
        public float maxAssignDistance = 60f;
        [Tooltip("Last-resort fallback: if ON, an agent with no eligible faction-appropriate area may be sent to a FOREIGN faction's area (and perform its signature activity) rather than roam in place. OFF keeps faction discipline (agents fall back to roam/idle near home). Default OFF.")]
        public bool allowCrossFactionAreaFallback = false;

        [Header("When agents interact (encounters)")]
        [Tooltip("Two agents within this distance are candidates for an encounter.")]
        public float encounterScanRadius = 4f;
        [Range(0f, 1f)]
        [Tooltip("Per-tick chance the director stages a found candidate pair.")]
        public float encounterChancePerTick = 0.5f;
        [Tooltip("Max new encounters the director stages per tick (keeps the crowd legible).")]
        public int maxEncountersPerTick = 4;
        [Tooltip("Max new hostile fights the director stages per tick.")]
        public int maxFightsPerTick = 1;
        [Tooltip("If true, hostile pairs only fight when the scene mood is Tense/Hostile or a faction hostility directive is active.")]
        public bool requireMoodForFights = true;
        [Tooltip("Minimum faction directive intensity that counts as 'hostility allowed' for fights.")]
        [Range(0f, 1f)] public float hostilityDirectiveThreshold = 0.4f;

        [Header("Infectious spread")]
        [Tooltip("Chance per extras tick for a nearby idle agent to join an infectious activity.")]
        [Range(0f, 1f)] public float infectiousJoinChance = 0.25f;
        [Tooltip("Maximum nearby participants promoted by infectious spread around one source activity.")]
        public int maxInfectiousParticipants = 3;

        [Header("Violence & lethality")]
        [Tooltip("Master gate: lethal (Shoot) encounters are only allowed when this is true OR a runtime escalation has temporarily enabled them. Keep off to preserve an orderly opening.")]
        public bool lethalEncountersEnabled = false;
        [Tooltip("An individual's personal hostility (persona affinity inverse) toward another faction at/above this lets a fight or shooting stage even under a Calm scene with no directive. Lower = more eager confrontations; raise toward 1 to require scene mood/directive as before.")]
        [Range(0f, 1f)] public float personalHostilityTrigger = 0.6f;
        [Tooltip("How strongly the INDIVIDUAL's persona hostility toward the other faction weighs into the pair-action decision.")]
        [Range(0f, 2f)] public float hostilityPersonaWeight = 0.6f;
        [Tooltip("How strongly the faction-level relation (Hostile) weighs into the pair-action decision.")]
        [Range(0f, 2f)] public float hostilityRelationWeight = 0.5f;
        [Tooltip("How strongly the personal aggression trait weighs into the pair-action decision.")]
        [Range(0f, 2f)] public float hostilityAggressionWeight = 0.3f;
        [Tooltip("Blended hostility at/above which a hostile pair escalates from Talk/Flee to a melee Fight.")]
        [Range(0f, 2f)] public float fightHostilityThreshold = 0.4f;

        [Header("Witnessing")]
        [Tooltip("Radius (m) around a violent act within which bystanders update faction affinity.")]
        public float witnessRadius = 12f;
        [Tooltip("Base affinity nudge magnitude applied to witnesses (scaled by proximity, lethality, sociability).")]
        [Range(0f, 0.5f)] public float witnessDriftBase = 0.05f;
        [Tooltip("Multiplier applied to witness drift when the act is lethal (a killing skews harder than a brawl).")]
        public float lethalDriftMultiplier = 2.5f;
        [Tooltip("Local ambient mood tag injected at a violent act so the crowd reacts (flee/watch) via the existing ambient path.")]
        public string violenceMoodTag = "violent";
        [Tooltip("Influence radius (m) of the violence mood injection.")]
        public float violenceInfluenceRadius = 10f;
    }
}
