using System;
using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms.Scenes
{
    public class SceneRuntimeContext
    {
        public SceneSO scene;
        public float intensity = 0.5f;
        public float elapsedInPhase = 0f;

        // Role ID -> List of Agent IDs assigned to it
        public Dictionary<string, List<string>> roleAssignments = new Dictionary<string, List<string>>();

        // Agent ID -> Reserved InteractionPoint
        public Dictionary<string, InteractionPoint> reservedPoints = new Dictionary<string, InteractionPoint>();

        // Agent ID -> Reserved AreaAnchor
        public Dictionary<string, AreaAnchor> reservedAreas = new Dictionary<string, AreaAnchor>();

        // Agent ID -> Reserved TimelineAnchor
        public Dictionary<string, TimelineAnchor> reservedTimelines = new Dictionary<string, TimelineAnchor>();

        // Callback to trigger phase advancement in the SceneDirector
        public Action<string> onAdvancePhase;

        public float TimeRemaining(float maxDuration)
        {
            return Mathf.Max(0f, maxDuration - elapsedInPhase);
        }

        public void AdvancePhase(string nextPhaseId = null)
        {
            onAdvancePhase?.Invoke(nextPhaseId);
        }
    }
}
