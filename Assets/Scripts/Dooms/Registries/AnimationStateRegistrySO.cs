using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms.Registries
{
    [CreateAssetMenu(fileName = "AnimationStateRegistry", menuName = "DOOMS/Animation State Registry")]
    public class AnimationStateRegistrySO : ScriptableObject
    {
        private static AnimationStateRegistrySO _instance;
        public static AnimationStateRegistrySO Instance
        {
            get
            {
#if UNITY_EDITOR
                // Editor: always return the live in-memory asset so inspector edits
                // are reflected immediately without domain reload.
                const string kAuth = "Assets/Resources/Dooms/DoomsAnimationStateRegistry.asset";
                var reg = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationStateRegistrySO>(kAuth);
                if (reg != null) return reg;
                var guids = UnityEditor.AssetDatabase.FindAssets("t:AnimationStateRegistrySO");
                foreach (var guid in guids)
                {
                    reg = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationStateRegistrySO>(
                              UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (reg != null) return reg;
                }
                return null;
#else
                if (_instance == null)
                {
                    _instance = Resources.Load<AnimationStateRegistrySO>("Dooms/DoomsAnimationStateRegistry");
                    if (_instance == null)
                    {
                        var assets = Resources.FindObjectsOfTypeAll<AnimationStateRegistrySO>();
                        if (assets.Length > 0) _instance = assets[0];
                    }
                }
                return _instance;
#endif
            }
        }

        [Tooltip("All valid animation/coroutine state names used by AgentActionSystem.")]
        public List<string> states = new List<string>
        {
            "Idle",
            "Patrolling",
            "Guarding",
            "Working",
            "Hiding",
            "Chanting",
            "Preaching",
            "Farming",
            "Resting",
            "Lurking",
            "Marching",
            "Dispersing",
            "TendingMachines",
            "Worshipping",
            "Demonstrating",
            "Negotiating",
            "Fleeing",
            "Surrendering"
        };
    }
}
