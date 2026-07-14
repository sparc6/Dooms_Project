using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Polls /api/dooms/state and surfaces the constrained World Narrator
    /// output (headline + intensity + themes) to the UI as a news ticker.
    ///
    /// This component is purely a consumer: it never causes the narrator to
    /// run; the backend decides when to emit a headline based on telemetry
    /// deltas. If the backend plugin is disabled or deleted, this component
    /// silently no-ops (failed HTTP requests are logged at info level).
    ///
    /// Optional integrations:
    ///   - Assign `tickerLabel` (TextMesh / TMP / UnityEngine.UI.Text) to drive
    ///     a UI element. The component reflects on common label types so it
    ///     can drop into existing UIs without a hard TMPro dependency.
    ///   - Subscribe to `OnHeadlineChanged` from other scripts (e.g. camera
    ///     director, audio sting) for richer reactions.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MLA_SIM/DOOMS/Dooms Narrator Ticker")]
    public class DoomsNarratorTicker : MonoBehaviour
    {
        [Header("Backend")]
        [Tooltip("Override for the backend base URL. Leave empty to copy from a sibling DoomsConfigPusher.")]
        public string backendBaseUrlOverride = "";

        [Tooltip("Timeout per /state poll, in seconds.")]
        [Range(1f, 30f)]
        public float requestTimeoutSec = 5f;

        [Header("UI")]
        [Tooltip("Optional UI label component. Supports TMPro.TMP_Text, UnityEngine.UI.Text, and TextMesh via reflection.")]
        public Component tickerLabel;

        [Tooltip("Prefix added in front of the headline (e.g. '> NEWS:').")]
        public string headlinePrefix = "> ";

        [Tooltip("Show themes and intensity after the headline.")]
        public bool showTags = false;

        // Public event so other components can react to a new headline.
        public event Action<string, float, string[]> OnHeadlineChanged;

        // Cache last-seen headline to avoid spamming downstream listeners.
        private string _lastHeadline = "";
        private DoomsToggle _toggle;
        private DoomsConfigPusher _siblingPusher;

        private void Awake()
        {
            _toggle = GetComponent<DoomsToggle>() ?? FindFirstObjectByType<DoomsToggle>();
            _siblingPusher = GetComponent<DoomsConfigPusher>() ?? FindFirstObjectByType<DoomsConfigPusher>();
        }

        private void OnEnable()
        {
            StartCoroutine(PollLoop());
        }

        private float PollIntervalSec
        {
            get
            {
                if (_toggle != null) return Mathf.Max(0.5f, _toggle.narratorPollIntervalSec);
                return 4f;
            }
        }

        private bool ShouldPoll
        {
            get
            {
                if (_toggle == null) return true;
                return _toggle.enableDooms && _toggle.enableWorldNarrator;
            }
        }

        private string ResolveBaseUrl()
        {
            if (!string.IsNullOrWhiteSpace(backendBaseUrlOverride))
                return backendBaseUrlOverride.TrimEnd('/');
            if (_siblingPusher != null && !string.IsNullOrWhiteSpace(_siblingPusher.backendBaseUrlOverride))
                return _siblingPusher.backendBaseUrlOverride.TrimEnd('/');

            // Match DoomsConfigPusher: reflection lookup of BackendCommunicator.
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
                            if (!string.IsNullOrWhiteSpace(val)) return val.TrimEnd('/');
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DoomsNarratorTicker] Base URL auto-resolve failed: {e.Message}");
            }
            return null;
        }

        private IEnumerator PollLoop()
        {
            while (true)
            {
                if (!ShouldPoll)
                {
                    yield return new WaitForSeconds(PollIntervalSec);
                    continue;
                }

                string baseUrl = ResolveBaseUrl();
                if (!string.IsNullOrEmpty(baseUrl))
                {
                    yield return StartCoroutine(FetchOnce(baseUrl + "/dooms/state"));
                }
                yield return new WaitForSeconds(PollIntervalSec);
            }
        }

        private IEnumerator FetchOnce(string url)
        {
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
                    // Plugin may simply be disabled; keep this quiet.
                    yield break;
                }

                ParseAndApply(req.downloadHandler.text);
            }
        }

        // Permissive JSON poking. We do not pull TMPro/JSON.NET in here on
        // purpose; only need three fields out of /state.
        private void ParseAndApply(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            string headline = ExtractStringField(json, "\"headline\"");
            if (headline == null) return; // narrator not loaded yet

            float intensity = ExtractFloatField(json, "\"intensity\"");
            string[] themes = ExtractStringArray(json, "\"themes\"");

            // Phase 1: parse faction_directives and publish to the board
            // BEFORE the headline-dedup short-circuit, so directives update on
            // every poll even when the headline hasn't changed.
            PublishFactionDirectives(json, intensity);

            // Phase 4: parse active_scene and scene_intensity to activate choreographies
            TriggerActiveScene(json);

            if (headline == _lastHeadline) return;
            _lastHeadline = headline;

            ApplyToLabel(headline, intensity, themes);
            try { OnHeadlineChanged?.Invoke(headline, intensity, themes); }
            catch (Exception e) { Debug.LogWarning($"[DoomsNarratorTicker] subscriber threw: {e.Message}"); }
        }

        // ---- Phase 1: faction_directives parser --------------------------
        // The backend returns world_state.faction_directives as a JSON object
        // keyed by faction_id. We do a permissive, dependency-free parse and
        // push each entry to FactionDirectiveBoard.
        private void PublishFactionDirectives(string json, float intensity)
        {
            if (string.IsNullOrEmpty(json)) return;
            int dirKey = json.IndexOf("\"faction_directives\"", StringComparison.Ordinal);
            if (dirKey < 0) return;
            int open = json.IndexOf('{', dirKey);
            if (open < 0) return;
            // Find matching closing brace with depth counter
            int depth = 0;
            int end = -1;
            for (int i = open; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
            }
            if (end <= open) return;
            string body = json.Substring(open + 1, end - open - 1);

            // Split on top-level "factionId": { ... } entries.
            int idx = 0;
            while (idx < body.Length)
            {
                int qs = body.IndexOf('"', idx);
                if (qs < 0) break;
                int qe = body.IndexOf('"', qs + 1);
                if (qe < 0) break;
                string factionId = body.Substring(qs + 1, qe - qs - 1);
                int lb = body.IndexOf('{', qe);
                if (lb < 0) break;
                int d2 = 0; int rb = -1;
                for (int i = lb; i < body.Length; i++)
                {
                    if (body[i] == '{') d2++;
                    else if (body[i] == '}') { d2--; if (d2 == 0) { rb = i; break; } }
                }
                if (rb <= lb) break;
                string dirBody = body.Substring(lb, rb - lb + 1);

                var dir = new FactionDirectiveBoard.Directive
                {
                    factionId = factionId,
                    allowedActions = ExtractStringArray(dirBody, "\"allowed_actions\""),
                    infectiousActions = ExtractStringArray(dirBody, "\"infectious_actions\""),
                    targetClasses = ExtractStringArray(dirBody, "\"target_classes\""),
                    scopeHint = ExtractStringField(dirBody, "\"scope_hint\"") ?? "",
                    ttlSec = ExtractFloatField(dirBody, "\"ttl_sec\""),
                    intensity = intensity,
                };
                if (dir.ttlSec <= 0f) dir.ttlSec = 30f;
                FactionDirectiveBoard.Publish(dir);

                idx = rb + 1;
            }
        }

        private void TriggerActiveScene(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            // Find "world_state" block
            int wsIdx = json.IndexOf("\"world_state\"", StringComparison.Ordinal);
            if (wsIdx >= 0)
            {
                // Permissive nested extraction using substring
                int wsOpen = json.IndexOf('{', wsIdx);
                if (wsOpen >= 0)
                {
                    int wsDepth = 0;
                    int wsEnd = -1;
                    for (int i = wsOpen; i < json.Length; i++)
                    {
                        if (json[i] == '{') wsDepth++;
                        else if (json[i] == '}')
                        {
                            wsDepth--;
                            if (wsDepth == 0) { wsEnd = i; break; }
                        }
                    }

                    if (wsEnd > wsOpen)
                    {
                        string wsSub = json.Substring(wsOpen, wsEnd - wsOpen + 1);
                        string activeScene = ExtractStringField(wsSub, "\"active_scene\"");
                        float sceneIntensity = ExtractFloatField(wsSub, "\"scene_intensity\"");

                        var director = Scenes.SceneDirector.Instance;
                        if (director != null)
                        {
                            var current = director.CurrentContext;
                            if (!string.IsNullOrEmpty(activeScene))
                            {
                                // If no scene is running, or a different scene is requested, trigger it.
                                if (current == null || current.scene == null || current.scene.sceneId != activeScene)
                                {
                                    Debug.Log($"[DOOMS][NarratorTicker] Received new active_scene directive: '{activeScene}' (intensity: {sceneIntensity:F2}). Activating...");
                                    director.ActivateScene(activeScene, sceneIntensity);
                                }
                            }
                            else
                            {
                                // Narrator cleared the active scene -> ask the director to disperse.
                                if (current != null && current.scene != null)
                                {
                                    Debug.Log($"[DOOMS][NarratorTicker] active_scene cleared by narrator. Deactivating current scene '{current.scene.sceneId}'.");
                                    director.DeactivateScene("narrator cleared");
                                }
                            }
                        }
                    }
                }
            }
        }

        private void ApplyToLabel(string headline, float intensity, string[] themes)
        {
            if (tickerLabel == null) return;
            string text = headlinePrefix + (string.IsNullOrEmpty(headline) ? "(quiet)" : headline);
            if (showTags && themes != null && themes.Length > 0)
            {
                text += "  [" + string.Join(",", themes) + "]";
                text += $"  i={intensity:0.00}";
            }

            // Try TMP first (no compile-time dep), then UnityEngine.UI.Text, then TextMesh.
            var labelType = tickerLabel.GetType();
            var prop = labelType.GetProperty("text");
            if (prop != null && prop.CanWrite)
            {
                try { prop.SetValue(tickerLabel, text); return; }
                catch { /* fall through */ }
            }
            if (tickerLabel is TextMesh tm) { tm.text = text; return; }
        }

        // ---- minimal, dependency-free JSON helpers (best-effort) ---------

        private static string ExtractStringField(string json, string keyQuoted)
        {
            int k = json.IndexOf(keyQuoted, StringComparison.Ordinal);
            if (k < 0) return null;
            int colon = json.IndexOf(':', k);
            if (colon < 0) return null;
            int start = json.IndexOf('"', colon + 1);
            if (start < 0) return null;
            int end = start + 1;
            var sb = new System.Text.StringBuilder();
            while (end < json.Length)
            {
                char c = json[end];
                if (c == '\\' && end + 1 < json.Length)
                {
                    char n = json[end + 1];
                    if (n == '"') sb.Append('"');
                    else if (n == 'n') sb.Append('\n');
                    else if (n == '\\') sb.Append('\\');
                    else sb.Append(n);
                    end += 2;
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
                end++;
            }
            return sb.ToString();
        }

        private static float ExtractFloatField(string json, string keyQuoted)
        {
            int k = json.IndexOf(keyQuoted, StringComparison.Ordinal);
            if (k < 0) return 0f;
            int colon = json.IndexOf(':', k);
            if (colon < 0) return 0f;
            int i = colon + 1;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            int j = i;
            while (j < json.Length && "-+0123456789.eE".IndexOf(json[j]) >= 0) j++;
            float.TryParse(json.Substring(i, j - i), System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out float v);
            return v;
        }

        private static string[] ExtractStringArray(string json, string keyQuoted)
        {
            int k = json.IndexOf(keyQuoted, StringComparison.Ordinal);
            if (k < 0) return Array.Empty<string>();
            int lb = json.IndexOf('[', k);
            int rb = json.IndexOf(']', lb < 0 ? k : lb);
            if (lb < 0 || rb < 0 || rb < lb) return Array.Empty<string>();
            string inner = json.Substring(lb + 1, rb - lb - 1);
            var result = new System.Collections.Generic.List<string>();
            int i = 0;
            while (i < inner.Length)
            {
                int qs = inner.IndexOf('"', i);
                if (qs < 0) break;
                int qe = inner.IndexOf('"', qs + 1);
                if (qe < 0) break;
                result.Add(inner.Substring(qs + 1, qe - qs - 1));
                i = qe + 1;
            }
            return result.ToArray();
        }
    }
}
