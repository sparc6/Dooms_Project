#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using MLA_SIM.Dooms.Registries;

namespace MLA_SIM.Dooms.Editor
{
    public class PopulateAnimationSequencesWindow : EditorWindow
    {
        [MenuItem("DOOMS/Populate Animation Sequences", false, 20)]
        public static void ShowWindow()
        {
            var window = GetWindow<PopulateAnimationSequencesWindow>("Populate Sequences");
            window.minSize = new Vector2(450, 500);
            window.Show();
        }

        private AnimatorController _controller;
        private AnimationSequenceRegistry _registry;
        private AnimationStateRegistrySO _stateRegistry;

        private class ProposedSequence
        {
            public string sequenceId;
            public string startState;
            public List<SequenceStep> holdSteps = new List<SequenceStep>();
            public string endState;
            public bool isSelected = true;
            public bool alreadyExists = false;
        }

        private class ProposedState
        {
            public string stateName;
            public bool isSelected = true;
            public bool alreadyExists = false;
        }

        private class SequenceCandidate
        {
            public string sequenceId;
            public string startState;
            public string legacyLoopState;
            public string endState;
            public Dictionary<int, string> holdStates = new Dictionary<int, string>();
        }

        private List<ProposedSequence> _proposedSequences = new List<ProposedSequence>();
        private List<ProposedState> _proposedStates = new List<ProposedState>();
        private Vector2 _scrollPos;

        private void OnEnable()
        {
            // Auto-detect registry if possible
            if (_registry == null)
            {
                _registry = AnimationSequenceRegistry.Instance;
            }
            if (_stateRegistry == null)
            {
                _stateRegistry = AnimationStateRegistrySO.Instance;
            }
        }

        private void OnGUI()
        {
            GUILayout.Label("Populate Animation Sequences", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _controller = (AnimatorController)EditorGUILayout.ObjectField(
                "Animator Controller", _controller, typeof(AnimatorController), false);

            _registry = (AnimationSequenceRegistry)EditorGUILayout.ObjectField(
                "Sequence Registry", _registry, typeof(AnimationSequenceRegistry), false);

            _stateRegistry = (AnimationStateRegistrySO)EditorGUILayout.ObjectField(
                "State Registry", _stateRegistry, typeof(AnimationStateRegistrySO), false);

            EditorGUILayout.Space();

            if (GUILayout.Button("Scan Animator Controller", GUILayout.Height(30)))
            {
                ScanController();
            }

            EditorGUILayout.Space();

            if (_proposedSequences.Count > 0 || _proposedStates.Count > 0)
            {
                GUILayout.Label($"Proposed Sequences ({_proposedSequences.Count}) / States ({_proposedStates.Count}):", EditorStyles.boldLabel);
                
                // Selection buttons
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Select All", GUILayout.Width(100)))
                {
                    foreach (var s in _proposedSequences) if (!s.alreadyExists) s.isSelected = true;
                    foreach (var s in _proposedStates) if (!s.alreadyExists) s.isSelected = true;
                }
                if (GUILayout.Button("Deselect All", GUILayout.Width(100)))
                {
                    foreach (var s in _proposedSequences) s.isSelected = false;
                    foreach (var s in _proposedStates) s.isSelected = false;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space();

                _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));
                
                foreach (var seq in _proposedSequences)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();

                    GUI.enabled = !seq.alreadyExists;
                    seq.isSelected = EditorGUILayout.Toggle(seq.isSelected, GUILayout.Width(20));
                    
                    EditorGUILayout.LabelField($"Sequence: <b>{seq.sequenceId}</b>", new GUIStyle(EditorStyles.label) { richText = true });
                    
                    if (seq.alreadyExists)
                    {
                        GUI.enabled = true;
                        GUILayout.Label("(Already in Registry)", EditorStyles.miniLabel);
                        GUI.enabled = false;
                    }

                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.LabelField($"  Start: {seq.startState}");
                    if (seq.holdSteps != null && seq.holdSteps.Count > 0)
                    {
                        for (int i = 0; i < seq.holdSteps.Count; i++)
                        {
                            var step = seq.holdSteps[i];
                            EditorGUILayout.LabelField($"  Hold {i + 1}: {step.stateName} {(string.IsNullOrEmpty(step.propId) ? "" : $"[Prop: {step.propId}]")}");
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField($"  Loop:  {seq.endState}");
                    }
                    EditorGUILayout.LabelField($"  End:   {seq.endState}");

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(2);
                }
                
                if (_proposedStates.Count > 0)
                {
                    EditorGUILayout.Space(8);
                    GUILayout.Label("Proposed Bare States:", EditorStyles.boldLabel);
                    foreach (var state in _proposedStates)
                    {
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        EditorGUILayout.BeginHorizontal();
                        GUI.enabled = !state.alreadyExists;
                        state.isSelected = EditorGUILayout.Toggle(state.isSelected, GUILayout.Width(20));
                        EditorGUILayout.LabelField($"State: <b>{state.stateName}</b>", new GUIStyle(EditorStyles.label) { richText = true });
                        if (state.alreadyExists)
                        {
                            GUI.enabled = true;
                            GUILayout.Label("(Already in Registry)", EditorStyles.miniLabel);
                            GUI.enabled = false;
                        }
                        EditorGUILayout.EndHorizontal();
                        GUI.enabled = true;
                        EditorGUILayout.EndVertical();
                    }
                }

                GUI.enabled = true;
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space();

                if (GUILayout.Button("Add Selected to Registries", GUILayout.Height(40)))
                {
                    AddSelectedEntries();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Select an Animator Controller and click 'Scan' to find candidate sequences.\n" +
                                        "Candidate sequences must have states ending in suffix patterns like:\n" +
                                        "- Start: _Enter, _Start\n" +
                                        "- Hold:  _h1, _h2, ...\n" +
                                        "- Loop:  _Loop\n" +
                                        "- End:   _Exit, _End", MessageType.Info);
            }
        }

        private void ScanController()
        {
            _proposedSequences.Clear();
            _proposedStates.Clear();

            if (_controller == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select an Animator Controller first.", "OK");
                return;
            }

            if (_controller.layers.Length == 0)
            {
                EditorUtility.DisplayDialog("Error", "Selected Animator Controller has no layers.", "OK");
                return;
            }

            // Get all states from the Base Layer (layer 0)
            var stateMachine = _controller.layers[0].stateMachine;
            var states = new List<string>();
            GatherStates(stateMachine, states);

            var sequenceCandidates = new Dictionary<string, SequenceCandidate>(StringComparer.OrdinalIgnoreCase);
            var bareStates = new List<string>();
            var claimedStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Suffix lists (case-insensitive checks)
            var startSuffixes = new[] { "_enter", "_start", "_Enter", "_Start" };
            var loopSuffixes  = new[] { "_loop", "_Loop" };
            var endSuffixes   = new[] { "_exit", "_end", "_Exit", "_End" };

            foreach (var state in states)
            {
                if (string.IsNullOrEmpty(state)) continue;

                string prefix = "";
                int type = -1; // 0=start, 1=loop, 2=end, 3=hold-step
                int holdIndex = -1;

                // 1. Check Loop suffix
                foreach (var sfx in loopSuffixes)
                {
                    if (state.EndsWith(sfx, StringComparison.OrdinalIgnoreCase))
                    {
                        prefix = state.Substring(0, state.Length - sfx.Length);
                        type = 1;
                        break;
                    }
                }

                // 2. Check Start suffix
                if (type == -1)
                {
                    foreach (var sfx in startSuffixes)
                    {
                        if (state.EndsWith(sfx, StringComparison.OrdinalIgnoreCase))
                        {
                            prefix = state.Substring(0, state.Length - sfx.Length);
                            type = 0;
                            break;
                        }
                    }
                }

                // 3. Check End suffix
                if (type == -1)
                {
                    foreach (var sfx in endSuffixes)
                    {
                        if (state.EndsWith(sfx, StringComparison.OrdinalIgnoreCase))
                        {
                            prefix = state.Substring(0, state.Length - sfx.Length);
                            type = 2;
                            break;
                        }
                    }
                }

                // 2. Check numbered hold suffix (_h1, _h2, ...)
                if (type == -1)
                {
                    int holdMarker = state.LastIndexOf("_h", StringComparison.OrdinalIgnoreCase);
                    if (holdMarker > 0 && holdMarker + 2 < state.Length)
                    {
                        string numericPart = state.Substring(holdMarker + 2);
                        if (int.TryParse(numericPart, out holdIndex))
                        {
                            prefix = state.Substring(0, holdMarker);
                            type = 3;
                        }
                    }
                }

                if (type != -1 && !string.IsNullOrEmpty(prefix))
                {
                    claimedStates.Add(state);

                    if (!sequenceCandidates.TryGetValue(prefix, out var candidate))
                    {
                        candidate = new SequenceCandidate { sequenceId = prefix };
                    }

                    if (type == 0) candidate.startState = state;
                    else if (type == 1) candidate.legacyLoopState = state;
                    else if (type == 2) candidate.endState = state;
                    else if (type == 3 && holdIndex > 0)
                    {
                        candidate.holdStates[holdIndex] = state;
                    }

                    sequenceCandidates[prefix] = candidate;
                }
                else
                {
                    bareStates.Add(state);
                }
            }

            // Construct Proposed Sequences
            foreach (var kvp in sequenceCandidates)
            {
                var prefix = kvp.Key;
                var candidate = kvp.Value;

                if (candidate == null) continue;

                var orderedHoldSteps = new List<SequenceStep>();
                if (candidate.holdStates != null && candidate.holdStates.Count > 0)
                {
                    var keys = new List<int>(candidate.holdStates.Keys);
                    keys.Sort();
                    for (int i = 0; i < keys.Count; i++)
                    {
                        orderedHoldSteps.Add(new SequenceStep { stateName = candidate.holdStates[keys[i]] });
                    }
                }
                else if (!string.IsNullOrEmpty(candidate.legacyLoopState))
                {
                    orderedHoldSteps.Add(new SequenceStep { stateName = candidate.legacyLoopState });
                }

                if (orderedHoldSteps.Count == 0) continue;

                // Create a proposed sequence
                var seq = new ProposedSequence
                {
                    sequenceId = prefix,
                    startState = candidate.startState ?? "",
                    holdSteps = orderedHoldSteps,
                    endState = candidate.endState ?? ""
                };

                // Check if already exists in registry
                if (_registry != null)
                {
                    var existing = _registry.FindSequence(prefix);
                    if (existing != null)
                    {
                        seq.alreadyExists = true;
                        seq.isSelected = false;
                        
                        // Copy existing to show current setup
                        seq.startState = existing.startState;
                        seq.endState = existing.endState;
                        seq.holdSteps = existing.GetHoldSteps();
                    }
                }

                _proposedSequences.Add(seq);
            }

            foreach (var state in bareStates)
            {
                if (string.IsNullOrWhiteSpace(state) || claimedStates.Contains(state)) continue;

                var proposedState = new ProposedState
                {
                    stateName = state
                };

                if (_stateRegistry != null && _stateRegistry.states != null)
                {
                    foreach (var existing in _stateRegistry.states)
                    {
                        if (string.Equals(existing, state, StringComparison.OrdinalIgnoreCase))
                        {
                            proposedState.alreadyExists = true;
                            proposedState.isSelected = false;
                            break;
                        }
                    }
                }

                _proposedStates.Add(proposedState);
            }

            // Sort alphabetically by sequence ID
            _proposedSequences.Sort((a, b) => string.Compare(a.sequenceId, b.sequenceId, StringComparison.OrdinalIgnoreCase));
            _proposedStates.Sort((a, b) => string.Compare(a.stateName, b.stateName, StringComparison.OrdinalIgnoreCase));

            if (_proposedSequences.Count == 0 && _proposedStates.Count == 0)
            {
                EditorUtility.DisplayDialog("Scan Complete", "No candidate sequences or bare states found.\n\nMake sure states ending in suffixes like '_Enter', '_h1', '_Loop', or '_Exit' exist in your Base Layer.", "OK");
            }
        }

        private void GatherStates(AnimatorStateMachine sm, List<string> states)
        {
            if (sm == null) return;

            foreach (var state in sm.states)
            {
                if (state.state != null && !string.IsNullOrEmpty(state.state.name))
                {
                    states.Add(state.state.name);
                }
            }

            foreach (var subSm in sm.stateMachines)
            {
                GatherStates(subSm.stateMachine, states);
            }
        }

        private void AddSelectedEntries()
        {
            if (_registry == null || _stateRegistry == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select both a Sequence Registry and a State Registry first.", "OK");
                return;
            }

            int addedSequenceCount = 0;
            int addedStateCount = 0;
            foreach (var seq in _proposedSequences)
            {
                if (seq.isSelected && !seq.alreadyExists)
                {
                    var holdSteps = new List<SequenceStep>();
                    if (seq.holdSteps != null)
                    {
                        for (int i = 0; i < seq.holdSteps.Count; i++)
                        {
                            var step = seq.holdSteps[i];
                            if (step == null) continue;
                            holdSteps.Add(new SequenceStep
                            {
                                stateName = step.stateName,
                                propId = step.propId
                            });
                        }
                    }

                    _registry.sequences.Add(new ActionAnimSequence
                    {
                        sequenceId = seq.sequenceId,
                        startState = seq.startState,
                        loopState = holdSteps.Count > 0 ? holdSteps[0].stateName : "",
                        endState = seq.endState,
                        holdSteps = holdSteps,
                        startCrossfade = 0.15f,
                        endCrossfade = 0.15f
                    });
                    addedSequenceCount++;
                }
            }

            if (_stateRegistry.states == null)
            {
                _stateRegistry.states = new List<string>();
            }

            foreach (var state in _proposedStates)
            {
                if (state.isSelected && !state.alreadyExists && !string.IsNullOrWhiteSpace(state.stateName))
                {
                    bool exists = false;
                    for (int i = 0; i < _stateRegistry.states.Count; i++)
                    {
                        if (string.Equals(_stateRegistry.states[i], state.stateName, StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                    {
                        _stateRegistry.states.Add(state.stateName);
                        addedStateCount++;
                    }
                }
            }

            if (addedSequenceCount > 0)
            {
                EditorUtility.SetDirty(_registry);
            }

            if (addedStateCount > 0)
            {
                EditorUtility.SetDirty(_stateRegistry);
            }

            if (addedSequenceCount > 0 || addedStateCount > 0)
            {
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog("Success", $"Successfully added {addedSequenceCount} sequence(s) and {addedStateCount} state(s) to the registries.", "OK");
                ScanController(); // Re-scan to update state list
            }
            else
            {
                EditorUtility.DisplayDialog("Info", "No new sequences or states were selected to be added.", "OK");
            }
        }
    }
}
#endif
