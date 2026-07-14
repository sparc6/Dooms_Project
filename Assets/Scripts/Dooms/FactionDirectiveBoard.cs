using System;
using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Phase 1: Shared board of narrator-emitted faction directives.
    ///
    /// Populated by <see cref="DoomsNarratorTicker"/> after each /dooms/state
    /// poll. Consumed by T3 brains (as an override hint) and T4 brains (as a
    /// scoring bonus). Entries auto-expire after their TTL so stale directives
    /// don't influence behavior.
    ///
    /// Pure singleton; no Inspector surface. Safe to call from any thread that
    /// reaches back into Unity's main thread via the ticker.
    /// </summary>
    public static class FactionDirectiveBoard
    {
        [Serializable]
        public class Directive
        {
            public string factionId = "";
            public string[] allowedActions = Array.Empty<string>();
            public string[] infectiousActions = Array.Empty<string>();
            public string[] targetClasses = Array.Empty<string>();
            public string scopeHint = "";
            public float intensity = 0f;
            public float ttlSec = 30f;
            public float receivedAt = 0f;

            public bool IsExpired => (Time.time - receivedAt) > Mathf.Max(1f, ttlSec);

            public bool ContainsAction(string action)
            {
                if (string.IsNullOrEmpty(action) || allowedActions == null) return false;
                for (int i = 0; i < allowedActions.Length; i++)
                    if (string.Equals(allowedActions[i], action, StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }

            public bool ContainsTargetClass(string targetClass)
            {
                if (string.IsNullOrEmpty(targetClass) || targetClasses == null) return false;
                for (int i = 0; i < targetClasses.Length; i++)
                    if (string.Equals(targetClasses[i], targetClass, StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }
        }

        private static readonly Dictionary<string, Directive> _byFaction
            = new Dictionary<string, Directive>(StringComparer.OrdinalIgnoreCase);

        [Serializable]
        public class AgentDirective
        {
            public string directiveKind = "Point"; // Point, Area, Timeline
            public string pointTag = "";
            public string animationState = "";
            
            // Area-specific
            public string areaTag = "";
            public string behavior = "Loiter";
            public string preferredBlendTree = "";
            public string pairWithFactionId = "";

            // Timeline-specific
            public string timelineAnchorId = "";
            public string timelineSlotId = "";

            public float ttlSec = 30f;
            public string sceneId = "";
            public string phaseId = "";
            public float receivedAt = 0f;

            public bool IsExpired => (Time.time - receivedAt) > Mathf.Max(1f, ttlSec);
        }

        private static readonly Dictionary<string, AgentDirective> _byAgent
            = new Dictionary<string, AgentDirective>(StringComparer.OrdinalIgnoreCase);

        public static event Action<string, Directive> OnDirectiveUpdated;
        public static event Action<string, AgentDirective> OnAgentDirectiveUpdated;

        public static void Publish(Directive d)
        {
            if (d == null || string.IsNullOrEmpty(d.factionId)) return;
            d.receivedAt = Time.time;
            _byFaction[d.factionId] = d;
            try { OnDirectiveUpdated?.Invoke(d.factionId, d); }
            catch (Exception e) { Debug.LogWarning($"[FactionDirectiveBoard] subscriber threw: {e.Message}"); }
        }

        public static void Publish(string agentId, AgentDirective d)
        {
            if (string.IsNullOrEmpty(agentId) || d == null) return;
            d.receivedAt = Time.time;
            _byAgent[agentId] = d;
            try { OnAgentDirectiveUpdated?.Invoke(agentId, d); }
            catch (Exception e) { Debug.LogWarning($"[FactionDirectiveBoard] agent subscriber threw: {e.Message}"); }
        }

        /// <summary>Returns the current directive for a faction, or null if missing/expired.</summary>
        public static Directive Get(string factionId)
        {
            if (string.IsNullOrEmpty(factionId)) return null;
            if (!_byFaction.TryGetValue(factionId, out var d) || d == null) return null;
            if (d.IsExpired) { _byFaction.Remove(factionId); return null; }
            return d;
        }

        /// <summary>Remove an agent's per-agent directive entirely (used by SceneDirector on phase exit).</summary>
        public static void RemoveAgent(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return;
            if (_byAgent.Remove(agentId))
            {
                try { OnAgentDirectiveUpdated?.Invoke(agentId, null); }
                catch (Exception e) { Debug.LogWarning($"[FactionDirectiveBoard] remove subscriber threw: {e.Message}"); }
            }
        }

        /// <summary>Returns the current directive for a specific agent, or null if missing/expired.</summary>
        public static AgentDirective GetForAgent(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return null;
            if (!_byAgent.TryGetValue(agentId, out var d) || d == null) return null;
            if (d.IsExpired) { _byAgent.Remove(agentId); return null; }
            return d;
        }

        public static IEnumerable<Directive> AllActive()
        {
            var stale = new List<string>();
            foreach (var kv in _byFaction)
            {
                if (kv.Value == null || kv.Value.IsExpired) { stale.Add(kv.Key); continue; }
                yield return kv.Value;
            }
            foreach (var k in stale) _byFaction.Remove(k);
        }

        public static void Clear()
        {
            _byFaction.Clear();
            _byAgent.Clear();
        }
    }
}
