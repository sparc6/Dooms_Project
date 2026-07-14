using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms.Scenes
{
    [CreateAssetMenu(fileName = "NewScene", menuName = "DOOMS/Scene Definition")]
    public class SceneSO : ScriptableObject
    {
        [RegistryDropdown(RegistryType.Scene)]
        public string sceneId = "NewScene";
        public string displayName = "New Scene";
        [TextArea(3, 5)]
        public string description = "";

        [Tooltip("The factions required for this scene to start.")]
        public List<string> requiredFactions = new List<string>();

        public int minAgentsPerFaction = 2;
        public float baseDurationSec = 120f;

        [Tooltip("The NodeCanvas SceneGraph defining the phases and roles for this scene.")]
        public SceneGraph graph;

        [Tooltip("Classification tags for this scene (e.g. 'calm', 'tense', 'violent') used by narrator selection logic.")]
        public List<string> tags = new List<string>();
    }
}
