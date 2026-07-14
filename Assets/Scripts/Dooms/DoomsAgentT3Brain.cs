using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;
using MLA_SIM;
using MLA_SIM.Interactions;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Phase 3: T3 NPC local brain. Polls the backend deterministic picker
    /// (/api/dooms/npc_action) for the next action + target_class. If any
    /// <see cref="DoomsAgentNeeds"/> value is critical, locally overrides
    /// the backend pick with a need-satisfying action and reports the
    /// override via /api/dooms/npc_override.
    ///
    /// Required components: DoomsAgentTag (tier=3), DoomsAgentNeeds,
    /// NavMeshAgent, Animator + AnimatorLocomotionDriver.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DoomsAgentTag))]
    [RequireComponent(typeof(DoomsAgentNeeds))]
    [AddComponentMenu("MLA_SIM/DOOMS/Dooms Agent T3 Brain (Deprecated)")]
    [System.Obsolete("DOOMS T3 deprecated in scene architecture v1; use T4 + Scenes (SceneDirector). Kept for backward compatibility.")]
    public class DoomsAgentT3Brain : MonoBehaviour
    {
        [Header("Backend")]
        [Tooltip("Leave empty to auto-resolve from DoomsConfigPusher or BackendCommunicator.")]
        public string backendBaseUrlOverride = "";
        [Range(1f, 30f)] public float requestTimeoutSec = 5f;
        [Range(1f, 15f)] public float pollIntervalMin = 3f;
        [Range(1f, 15f)] public float pollIntervalMax = 5f;

        [Header("Need-Override Activities")]
        [Tooltip("What to do when energy is critical.")]
        public OverrideAction sleepOverride = new OverrideAction { targetClass = "Bed",        animatorStateName = "Sleep", holdSeconds = 8f, need = DoomsAgentNeeds.NeedType.Energy, restoreAmount = 0.8f };
        public OverrideAction eatOverride   = new OverrideAction { targetClass = "FoodSpot",   animatorStateName = "Eat",   holdSeconds = 4f, need = DoomsAgentNeeds.NeedType.Hunger, restoreAmount = 0.7f };
        public OverrideAction talkOverride  = new OverrideAction { targetClass = "SocialSpot", animatorStateName = "Talk",  holdSeconds = 6f, need = DoomsAgentNeeds.NeedType.Social, restoreAmount = 0.6f };

        [Serializable]
        public class OverrideAction
        {
            public string targetClass = "";
            public string animatorStateName = "";
            public float holdSeconds = 4f;
            public DoomsAgentNeeds.NeedType need = DoomsAgentNeeds.NeedType.Energy;
            public float restoreAmount = 0.5f;
        }

        [Serializable]
        private class NpcActionResponse
        {
            public bool ok;
            public string agent_id;
            public string action;
            public string targetClass;
            public string animatorStateName;
            public float holdSeconds;
            public string source;
            public float ttl_sec;
        }

        private DoomsAgentTag _tag;
        private DoomsAgentNeeds _needs;
        private NavMeshAgent _nav;
        private MLA_SIM.AnimatorLocomotionDriver _anim;
        private TargetTransformAnchor _heldAnchor;
        private DoomsConfigPusher _siblingPusher;
        private string _baseUrlCached;

        private void Awake()
        {
            _tag = GetComponent<DoomsAgentTag>();
            _needs = GetComponent<DoomsAgentNeeds>();
            _nav = GetComponent<NavMeshAgent>();
            _anim = GetComponent<MLA_SIM.AnimatorLocomotionDriver>();
            _siblingPusher = GetComponent<DoomsConfigPusher>() ?? FindFirstObjectByType<DoomsConfigPusher>();
        }

        private void OnEnable()
        {
            string agentId = _tag != null ? _tag.agentId : gameObject.name;
            Debug.Log($"[DOOMS] T3 Agent '{agentId}' Brain enabled.");
            StartCoroutine(BrainLoop());
        }

        private IEnumerator BrainLoop()
        {
            string agentId = _tag != null ? _tag.agentId : gameObject.name;
            // Startup stagger so polling is not synchronized across agents
            yield return new WaitForSeconds(UnityEngine.Random.Range(0f, pollIntervalMax));

            while (true)
            {
                // 1. Need override? (local, no network)
                OverrideAction critical = null;
                if (_needs != null)
                {
                    if (_needs.IsCritical(DoomsAgentNeeds.NeedType.Energy)) critical = sleepOverride;
                    else if (_needs.IsCritical(DoomsAgentNeeds.NeedType.Hunger)) critical = eatOverride;
                    else if (_needs.IsCritical(DoomsAgentNeeds.NeedType.Social)) critical = talkOverride;
                }

                if (critical != null)
                {
                    Debug.Log($"[DOOMS] T3 Agent '{agentId}' local critical override triggered: need={critical.need}, targetClass={critical.targetClass}, actionState={critical.animatorStateName}");
                    yield return StartCoroutine(FetchBackendAction(backendAction =>
                    {
                        // fire-and-forget report of override (non-blocking)
                        StartCoroutine(ReportOverride(backendAction?.action ?? "", critical.need, GuessActionFromNeed(critical.need)));
                    }));
                    yield return StartCoroutine(ExecuteOverride(critical));
                }
                else
                {
                    NpcActionResponse action = null;
                    yield return StartCoroutine(FetchBackendAction(r => action = r));
                    if (action != null && action.ok && !string.IsNullOrEmpty(action.targetClass))
                    {
                        Debug.Log($"[DOOMS] T3 Agent '{agentId}' executing backend action '{action.action}' on targetClass '{action.targetClass}'");
                        yield return StartCoroutine(ExecuteBackend(action));
                    }
                    else
                    {
                        string reason = action == null ? "Fetch failed/null response" : (!action.ok ? "ok=false" : "empty targetClass");
                        Debug.Log($"[DOOMS] T3 Agent '{agentId}' got no actionable backend result ({reason}). Stalling for a brief idle wait.");
                        // no actionable result — short idle wait
                        yield return new WaitForSeconds(UnityEngine.Random.Range(pollIntervalMin, pollIntervalMax));
                    }
                }
            }
        }

        private string GuessActionFromNeed(DoomsAgentNeeds.NeedType n)
        {
            switch (n)
            {
                case DoomsAgentNeeds.NeedType.Energy: return "Sleep";
                case DoomsAgentNeeds.NeedType.Hunger: return "Eat";
                case DoomsAgentNeeds.NeedType.Social: return "Talk";
                default: return "Idle";
            }
        }

        // ---- Backend I/O -------------------------------------------------
        private string ResolveBaseUrl()
        {
            if (!string.IsNullOrEmpty(_baseUrlCached)) return _baseUrlCached;
            if (!string.IsNullOrWhiteSpace(backendBaseUrlOverride))
            {
                _baseUrlCached = backendBaseUrlOverride.TrimEnd('/');
                return _baseUrlCached;
            }
            if (_siblingPusher != null && !string.IsNullOrWhiteSpace(_siblingPusher.backendBaseUrlOverride))
            {
                _baseUrlCached = _siblingPusher.backendBaseUrlOverride.TrimEnd('/');
                return _baseUrlCached;
            }
            // Reflection lookup of BackendCommunicator, mirroring DoomsNarratorTicker.
            try
            {
                var t = Type.GetType("MLA_SIM.BackendCommunicator, Assembly-CSharp");
                if (t != null)
                {
                    var obj = UnityEngine.Object.FindFirstObjectByType(t) as Component;
                    if (obj != null)
                    {
                        var cfgField = t.GetField("config");
                        var cfg = cfgField != null ? cfgField.GetValue(obj) : null;
                        if (cfg != null)
                        {
                            var prop = cfg.GetType().GetProperty("ApiBaseUrl");
                            var val = prop != null ? prop.GetValue(cfg) as string : null;
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                _baseUrlCached = val.TrimEnd('/');
                                return _baseUrlCached;
                            }
                        }
                    }
                }
            }
            catch { /* ignore */ }
            return null;
        }

        private IEnumerator FetchBackendAction(Action<NpcActionResponse> onDone)
        {
            string baseUrl = ResolveBaseUrl();
            string agentId = _tag != null ? _tag.agentId : "";
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(agentId))
            {
                Debug.LogWarning($"[DOOMS] T3 Agent cannot fetch action: baseUrl='{baseUrl}', agentId='{agentId}'");
                onDone?.Invoke(null);
                yield break;
            }
            string url = baseUrl + "/dooms/npc_action?agentId=" + UnityWebRequest.EscapeURL(agentId);
            Debug.Log($"[DOOMS] T3 Agent '{agentId}' sending request to: {url}");
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = Mathf.CeilToInt(requestTimeoutSec);
                yield return req.SendWebRequest();
#if UNITY_2020_1_OR_NEWER
                bool failed = req.result != UnityWebRequest.Result.Success;
#else
                bool failed = req.isNetworkError || req.isHttpError;
#endif
                if (failed)
                {
                    Debug.LogError($"[DOOMS] T3 Agent '{agentId}' request FAILED: url={url}, code={req.responseCode}, error={req.error}");
                    onDone?.Invoke(null);
                    yield break;
                }
                try
                {
                    string rawText = req.downloadHandler.text;
                    Debug.Log($"[DOOMS] T3 Agent '{agentId}' received raw action payload: {rawText}");
                    var resp = JsonUtility.FromJson<NpcActionResponse>(rawText);
                    onDone?.Invoke(resp);
                }
                catch (Exception parseEx)
                {
                    Debug.LogError($"[DOOMS] T3 Agent '{agentId}' failed to parse JSON: {parseEx.Message}. Raw text: {req.downloadHandler.text}");
                    onDone?.Invoke(null);
                }
            }
        }

        private IEnumerator ReportOverride(string backendAction, DoomsAgentNeeds.NeedType need, string chosen)
        {
            string baseUrl = ResolveBaseUrl();
            if (string.IsNullOrEmpty(baseUrl)) yield break;
            string agentId = _tag != null ? _tag.agentId : "";
            string url = baseUrl + "/dooms/npc_override";
            string payload = "{\"agent_id\":\"" + agentId + "\",\"overridden_action\":\"" + backendAction +
                             "\",\"chosen_action\":\"" + chosen + "\",\"need\":\"" + need.ToString() + "\"}";
            using (var req = new UnityWebRequest(url, "POST"))
            {
                byte[] body = Encoding.UTF8.GetBytes(payload);
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = Mathf.CeilToInt(requestTimeoutSec);
                yield return req.SendWebRequest();
            }
        }

        // ---- Execution ---------------------------------------------------
        private IEnumerator ExecuteOverride(OverrideAction a)
        {
            yield return ExecuteAnchored(a.targetClass, a.animatorStateName, a.holdSeconds, a.need, a.restoreAmount);
        }

        private IEnumerator ExecuteBackend(NpcActionResponse a)
        {
            // Backend pool/directive actions do not restore needs; restore
            // Duty slightly so agents doing their job feel productive.
            yield return ExecuteAnchored(a.targetClass, a.animatorStateName, a.holdSeconds,
                                         DoomsAgentNeeds.NeedType.Duty, 0.2f);
        }

        private IEnumerator ExecuteAnchored(string targetClass, string stateName, float holdSeconds,
                                            DoomsAgentNeeds.NeedType restoreNeed, float restoreAmount)
        {
            string agentId = _tag != null ? _tag.agentId : gameObject.name;
            TargetTransformAnchor anchor = null;
            if (!string.IsNullOrEmpty(targetClass))
            {
                string factionId = _tag != null ? _tag.factionId : "";
                Debug.Log($"[DOOMS] T3 Agent '{agentId}' looking for closest free targetClass '{targetClass}' (faction={factionId})");
                anchor = TargetClassRegistry.FindClosestFree(targetClass, transform.position, factionId);
            }

            if (anchor == null || !anchor.TryOccupy(agentId))
            {
                Debug.LogWarning($"[DOOMS] T3 Agent '{agentId}' failed to occupy targetClass '{targetClass}'. No free spots or anchor null. Backing off...");
                yield return new WaitForSeconds(UnityEngine.Random.Range(pollIntervalMin, pollIntervalMax));
                yield break;
            }
            _heldAnchor = anchor;
            Debug.Log($"[DOOMS] T3 Agent '{agentId}' successfully occupied anchor '{anchor.gameObject.name}'. Navigating...");

            var anchorT = anchor.GetAnchor();
            Vector3 destPos = anchorT != null ? anchorT.position : anchor.transform.position;

            if (_nav != null && _nav.isOnNavMesh)
            {
                _nav.SetDestination(destPos);
                float timeout = 20f;
                float t = 0f;
                while (t < timeout)
                {
                    if (!_nav.pathPending && _nav.remainingDistance <= Mathf.Max(0.5f, _nav.stoppingDistance))
                        break;
                    yield return new WaitForSeconds(0.2f);
                    t += 0.2f;
                }
            }

            if (anchorT != null)
            {
                transform.position = anchorT.position;
                Vector3 fwd = anchorT.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            }

            float hold = Mathf.Max(1f, holdSeconds);
            Debug.Log($"[DOOMS] T3 Agent '{agentId}' reached anchor. Playing animator state '{stateName}' for {hold} seconds.");
            if (_anim != null && !string.IsNullOrEmpty(stateName))
                _anim.PlayActionState(stateName, 0.15f, hold);

            yield return new WaitForSeconds(hold);

            if (_needs != null && restoreAmount > 0f)
            {
                _needs.Restore(restoreNeed, restoreAmount);
                Debug.Log($"[DOOMS] T3 Agent '{agentId}' restored need '{restoreNeed}' by {restoreAmount}.");
            }

            anchor.Release(agentId);
            _heldAnchor = null;
            Debug.Log($"[DOOMS] T3 Agent '{agentId}' released anchor '{anchor.gameObject.name}' and completed action.");

            yield return new WaitForSeconds(UnityEngine.Random.Range(pollIntervalMin, pollIntervalMax));
        }

        private void OnDisable()
        {
            if (_heldAnchor != null && _tag != null) _heldAnchor.Release(_tag.agentId);
            _heldAnchor = null;
        }
    }
}
