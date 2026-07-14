using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using MLA_SIM.Dooms.Scenes;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Procedural "extras" crowd coordinator (Option D, pairs-first).
    ///
    /// Runs a low-frequency tick over all UNASSIGNED T4 agents and does the
    /// things an individual brain cannot:
    ///   1. Assigns each idle agent its MOST INTERESTING free AreaAnchor (need +
    ///      faction + crowd-density scored) so the crowd spreads instead of
    ///      clumping on the nearest anchor.
    ///   2. Arbitrates peer encounters (talk / fight / flee) by invoking
    ///      T4EncounterResolver directly, gated by global budgets and the active
    ///      scene mood, so the crowd reads as coherent rather than random.
    ///
    /// Agents keep executing locally; the director only decides WHAT and WITH WHOM.
    /// When present it sets T4EncounterResolver.DirectorControlled = true, disabling
    /// the resolver's autonomous random self-triggering.
    /// </summary>
    [AddComponentMenu("DOOMS/Extras Director")]
    public class ExtrasDirector : MonoBehaviour
    {
        private static ExtrasDirector _instance;
        public static ExtrasDirector Instance => _instance;

        [Tooltip("Verbose logging of goal assignment and encounter staging.")]
        public bool verboseLogging = false;

        private sealed class Goal
        {
            public AreaAnchor area;
            public float assignedAt;
        }

        private readonly Dictionary<string, Goal> _goals =
            new Dictionary<string, Goal>(StringComparer.OrdinalIgnoreCase);

        private float _nextTick;

        private void OnEnable()
        {
            _instance = this;
            T4EncounterResolver.DirectorControlled = true;
            _nextTick = Time.time + 0.5f;
        }

        private void OnDisable()
        {
            if (_instance == this) _instance = null;
            T4EncounterResolver.DirectorControlled = false;
            _goals.Clear();
        }

        /// <summary>
        /// Read by DoomsAgentT4Brain: the AreaAnchor this agent should currently
        /// roam/act inside. Returns false when no director or no valid goal.
        /// </summary>
        public static bool TryGetAreaGoal(string agentId, out AreaAnchor area)
        {
            area = null;
            if (_instance == null || string.IsNullOrEmpty(agentId)) return false;
            if (_instance._goals.TryGetValue(agentId, out var g) && g != null
                && g.area != null && g.area.isActiveAndEnabled)
            {
                area = g.area;
                return true;
            }
            return false;
        }

        private void Update()
        {
            var profile = ExtrasProfileSO.Instance;
            float interval = profile != null ? Mathf.Max(0.25f, profile.tickInterval) : 1f;
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + interval;
            Tick(profile);
        }

        private void Tick(ExtrasProfileSO p)
        {
            var tags = FindObjectsByType<DoomsAgentTag>(FindObjectsSortMode.None);
            var areas = FindObjectsByType<AreaAnchor>(FindObjectsSortMode.None);

            PruneStaleGoals(p);

            // Build a projected-occupancy ledger from current goals so the director
            // never oversubscribes a small area (visual coherence).
            var projected = new Dictionary<AreaAnchor, int>();
            foreach (var kv in _goals)
            {
                var ar = kv.Value != null ? kv.Value.area : null;
                if (ar == null) continue;
                projected.TryGetValue(ar, out int c);
                projected[ar] = c + 1;
            }

            int assignedThisTick = AssignGoals(tags, areas, projected, p);
            int stagedThisTick = StageEncounters(tags, p);

            if (verboseLogging || DoomsDebug.Enabled)
            {
                int eligible = 0;
                for (int i = 0; i < tags.Length; i++)
                {
                    if (IsControllableExtra(tags[i])) eligible++;
                }
                string _tickMsg = $"tick: tags={tags.Length} eligible={eligible} assigned={assignedThisTick} goalsLive={_goals.Count} staged={stagedThisTick} areas={areas.Length}";
                if (verboseLogging) Debug.Log($"[DOOMS][Extras] {_tickMsg}");
                DoomsDebug.Log(DoomsDebug.Category.Extras, _tickMsg);
            }
        }

        private void PruneStaleGoals(ExtrasProfileSO p)
        {
            float maxAge = p != null ? p.goalDurationSec : 12f;
            List<string> stale = null;
            foreach (var kv in _goals)
            {
                var g = kv.Value;
                bool invalid = g == null || g.area == null || !g.area.isActiveAndEnabled
                               || (Time.time - g.assignedAt) > maxAge;
                if (invalid)
                {
                    (stale ??= new List<string>()).Add(kv.Key);
                }
            }
            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++) _goals.Remove(stale[i]);
            }
        }

        private int AssignGoals(DoomsAgentTag[] tags, AreaAnchor[] areas,
                                Dictionary<AreaAnchor, int> projected, ExtrasProfileSO p)
        {
            if (areas == null || areas.Length == 0) return 0;

            float factionHomeBonus = p != null ? p.factionHomeBonus : 0.5f;
            float distPenalty = p != null ? p.distancePenaltyPerMeter : 0.01f;
            float crowdPenalty = p != null ? p.crowdPenalty : 1.0f;
            float directiveBias = p != null ? p.directiveBias : 1.5f;
            float baseWander = p != null ? p.baseWanderInterest : 0.05f;
            float jitter = p != null ? p.interestJitter : 0.1f;
            int softCap = p != null ? p.softAreaCapacity : 4;
            float maxDist = p != null ? p.maxAssignDistance : 60f;
            int assignedCount = 0;

            for (int i = 0; i < tags.Length; i++)
            {
                var t = tags[i];
                if (!IsControllableExtra(t)) continue;
                if (_goals.ContainsKey(t.agentId)) continue;

                var brain = t.GetComponent<DoomsAgentT4Brain>()
                           ?? t.GetComponentInChildren<DoomsAgentT4Brain>(true)
                           ?? t.GetComponentInParent<DoomsAgentT4Brain>();
                if (brain == null) continue;

                string effectiveFaction = DoomsFactionRuntime.EffectiveFactionOf(t);
                if (string.IsNullOrEmpty(effectiveFaction)) continue;

                var directive = FactionDirectiveBoard.Get(effectiveFaction);

                AreaAnchor best = null;
                float bestScore = float.MinValue;

                if (!TryFindBestArea(tags[i], effectiveFaction, areas, projected, brain, directive,
                                     factionHomeBonus, distPenalty, crowdPenalty, directiveBias,
                                     baseWander, jitter, softCap, maxDist,
                                     requireCapacity: true, requireDistance: true, requireFactionMatch: true,
                                     out best, out bestScore))
                {
                    // Relax pass #1: allow temporary overflow when all areas are full.
                    TryFindBestArea(tags[i], effectiveFaction, areas, projected, brain, directive,
                                    factionHomeBonus, distPenalty, crowdPenalty, directiveBias,
                                    baseWander, jitter, softCap, maxDist,
                                    requireCapacity: false, requireDistance: true, requireFactionMatch: true,
                                    out best, out bestScore);
                }

                if (best == null)
                {
                    // Relax pass #2: ignore max distance too (still scored by distance penalty).
                    TryFindBestArea(tags[i], effectiveFaction, areas, projected, brain, directive,
                                    factionHomeBonus, distPenalty, crowdPenalty, directiveBias,
                                    baseWander, jitter, softCap, maxDist,
                                    requireCapacity: false, requireDistance: false, requireFactionMatch: true,
                                    out best, out bestScore);
                }

                if (best == null && p != null && p.allowCrossFactionAreaFallback)
                {
                    // Relax pass #3 (opt-in): ignore faction filter as last resort so agents
                    // without a dedicated faction area still roam rather than stand still.
                    // OFF by default — keeps faction discipline (no borrowing foreign-faction
                    // signature activities). Agents with no eligible area fall back to the
                    // brain's personal/needs layer (roam/idle near home) instead.
                    TryFindBestArea(tags[i], effectiveFaction, areas, projected, brain, directive,
                                    factionHomeBonus, distPenalty, crowdPenalty, directiveBias,
                                    baseWander, jitter, softCap, maxDist,
                                    requireCapacity: false, requireDistance: false, requireFactionMatch: false,
                                    out best, out bestScore);
                }

                if (best != null)
                {
                    _goals[t.agentId] = new Goal { area = best, assignedAt = Time.time };
                    projected.TryGetValue(best, out int c2);
                    projected[best] = c2 + 1;
                    assignedCount++;
                    string _assignMsg = $"'{t.agentId}' -> area '{best.GetPrimaryTag(ActiveSceneId())}' ({best.gameObject.name}) score={bestScore:F2}";
                    if (verboseLogging) Debug.Log($"[DOOMS][Extras] {_assignMsg}");
                    DoomsDebug.Log(DoomsDebug.Category.Extras, _assignMsg);
                }
                else
                {
                    string _noAreaMsg = $"'{t.agentId}' no assignable area (areas={areas.Length} faction={effectiveFaction}).";
                    if (verboseLogging) Debug.Log($"[DOOMS][Extras] {_noAreaMsg}");
                    DoomsDebug.Log(DoomsDebug.Category.Extras, _noAreaMsg);
                }
            }

            return assignedCount;
        }

        private bool TryFindBestArea(DoomsAgentTag tag, string effectiveFaction, AreaAnchor[] areas, Dictionary<AreaAnchor, int> projected,
                                     DoomsAgentT4Brain brain, FactionDirectiveBoard.Directive directive,
                                     float factionHomeBonus, float distPenalty, float crowdPenalty, float directiveBias,
                                     float baseWander, float jitter, int softCap, float maxDist,
                                     bool requireCapacity, bool requireDistance, bool requireFactionMatch,
                                     out AreaAnchor best, out float bestScore)
        {
            best = null;
            bestScore = float.MinValue;
            if (tag == null || brain == null || areas == null || string.IsNullOrEmpty(effectiveFaction)) return false;
            string sceneId = ActiveSceneId();

            for (int j = 0; j < areas.Length; j++)
            {
                var area = areas[j];
                if (area == null || !area.isActiveAndEnabled) continue;
                if (requireFactionMatch && !area.IsFactionAllowed(effectiveFaction)) continue;

                float dist = Vector3.Distance(tag.transform.position, area.transform.position);
                if (requireDistance && dist > maxDist) continue;

                int cap = (area.pointsOfInterest != null && area.pointsOfInterest.Count > 0)
                    ? area.pointsOfInterest.Count
                    : Mathf.Max(1, softCap);
                projected.TryGetValue(area, out int cur);
                if (requireCapacity && cur >= cap) continue;

                var tags = area.GetTagsForScene(sceneId);
                float interest = brain.GetAreaTagsInterest(tags);
                if (interest < 0f) interest = baseWander;

                float occupancyRatio = cur / (float)Mathf.Max(1, cap);
                if (!requireCapacity && cur >= cap)
                {
                    // Overflow is allowed only in relaxed passes, penalize harder.
                    occupancyRatio += 1f;
                }

                float score = interest
                    - distPenalty * dist
                    - crowdPenalty * occupancyRatio
                    + UnityEngine.Random.Range(0f, jitter);

                if (area.allowedFactions != null && area.allowedFactions.Count > 0)
                    score += factionHomeBonus;

                if (directive != null && AreaMatchesDirective(area, directive))
                    score += directiveBias * Mathf.Clamp01(directive.intensity);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = area;
                }
            }

            return best != null;
        }

        private static bool AreaMatchesDirective(AreaAnchor area, FactionDirectiveBoard.Directive directive)
        {
            if (area == null || directive == null) return false;

            var tags = area.GetTagsForScene(ActiveSceneId());
            for (int i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                if (string.IsNullOrEmpty(tag)) continue;
                if (directive.ContainsTargetClass(tag)) return true;
            }

            return false;
        }

        private static string ActiveSceneId()
        {
            if (SceneDirector.Instance == null || SceneDirector.Instance.CurrentContext == null || SceneDirector.Instance.CurrentContext.scene == null)
                return "";
            return SceneDirector.Instance.CurrentContext.scene.sceneId;
        }

        private int StageEncounters(DoomsAgentTag[] tags, ExtrasProfileSO p)
        {
            float scanRadius = p != null ? p.encounterScanRadius : 4f;
            float chance = p != null ? p.encounterChancePerTick : 0.5f;
            int maxEncounters = p != null ? p.maxEncountersPerTick : 4;
            int maxFights = p != null ? p.maxFightsPerTick : 1;
            bool requireMood = p == null || p.requireMoodForFights;
            float hostilityThreshold = p != null ? p.hostilityDirectiveThreshold : 0.4f;

            var eligible = new List<DoomsAgentTag>();
            for (int i = 0; i < tags.Length; i++)
            {
                var t = tags[i];
                if (IsControllableExtra(t) && !T4EncounterResolver.IsAgentLocked(t.agentId) && !IsWalking(t))
                    eligible.Add(t);
            }

            var relations = FactionRelationsSO.Instance;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int staged = 0;
            int fights = 0;

            for (int i = 0; i < eligible.Count && staged < maxEncounters; i++)
            {
                var a = eligible[i];
                if (used.Contains(a.agentId)) continue;

                for (int j = i + 1; j < eligible.Count; j++)
                {
                    var b = eligible[j];
                    if (used.Contains(a.agentId)) break;
                    if (used.Contains(b.agentId)) continue;

                    if (Vector3.Distance(a.transform.position, b.transform.position) > scanRadius) continue;
                    if (UnityEngine.Random.value > chance) continue;

                    string factionA = DoomsFactionRuntime.EffectiveFactionOf(a);
                    string factionB = DoomsFactionRuntime.EffectiveFactionOf(b);
                    if (string.IsNullOrEmpty(factionA) || string.IsNullOrEmpty(factionB)) continue;

                    Relation rel = relations != null
                        ? relations.GetRelation(factionA, factionB)
                        : Relation.Neutral;
                    // A strong individual grudge counts as hostile for fight budgeting and
                    // gating, so persona-driven confrontations aren't treated as social.
                    bool hostile = rel == Relation.Hostile || PersonalHostilityAllowed(a, b, factionA, factionB);

                    if (hostile)
                    {
                        if (fights >= maxFights) continue;
                        if (requireMood && !HostilityAllowed(a, b, hostilityThreshold)
                            && !PersonalHostilityAllowed(a, b, factionA, factionB)) continue;
                    }

                    var resolver = a.GetComponent<T4EncounterResolver>();
                    if (resolver == null) continue;

                    if (resolver.TryStageEncounterWith(b))
                    {
                        used.Add(a.agentId);
                        used.Add(b.agentId);
                        staged++;
                        if (hostile) fights++;
                        string _encMsg = $"staged encounter '{a.agentId}' x '{b.agentId}' rel={rel}";
                        if (verboseLogging) Debug.Log($"[DOOMS][Extras] {_encMsg}");
                        DoomsDebug.Log(DoomsDebug.Category.Encounter, _encMsg);
                        break;
                    }
                }
            }

            return staged;
        }

        private static bool IsWalking(DoomsAgentTag t)
        {
            if (t == null) return false;
            var nav = t.GetComponent<NavMeshAgent>();
            if (nav == null || !nav.enabled || !nav.isOnNavMesh) return false;
            return !nav.isStopped && nav.hasPath && nav.remainingDistance > nav.stoppingDistance + 0.25f;
        }

        private static bool HostilityAllowed(DoomsAgentTag a, DoomsAgentTag b, float threshold)
        {
            if (AmbientMoodBoard.Active && AmbientMoodBoard.Mood != SceneMood.Calm) return true;
            var da = FactionDirectiveBoard.Get(DoomsFactionRuntime.EffectiveFactionOf(a));
            if (da != null && da.intensity >= threshold) return true;
            var db = FactionDirectiveBoard.Get(DoomsFactionRuntime.EffectiveFactionOf(b));
            if (db != null && db.intensity >= threshold) return true;
            return false;
        }

        // An individual's personal grudge is its own trigger: a sufficiently hostile
        // agent will confront across factions even under a Calm scene with no directive.
        private static bool PersonalHostilityAllowed(DoomsAgentTag a, DoomsAgentTag b, string factionA, string factionB)
        {
            float trigger = ExtrasProfileSO.Instance != null ? ExtrasProfileSO.Instance.personalHostilityTrigger : 0.6f;
            var pa = a != null ? a.GetComponent<DoomsAgentPersona>() : null;
            if (pa != null && pa.HostilityToward(factionB) >= trigger) return true;
            var pb = b != null ? b.GetComponent<DoomsAgentPersona>() : null;
            if (pb != null && pb.HostilityToward(factionA) >= trigger) return true;
            return false;
        }

        private static bool IsControllableExtra(DoomsAgentTag t)
        {
            if (t == null) return false;
            if (string.IsNullOrEmpty(t.agentId) || string.IsNullOrEmpty(t.factionId)) return false;
            if (t.tierEnum != DoomsAgentTag.DoomsTier.AnimationOnly
                && t.tier != (int)DoomsAgentTag.DoomsTier.AnimationOnly) return false;
            if (!string.IsNullOrEmpty(t.reservedBySceneId)) return false;
            if (FactionDirectiveBoard.GetForAgent(t.agentId) != null) return false;
            return true;
        }
    }
}
