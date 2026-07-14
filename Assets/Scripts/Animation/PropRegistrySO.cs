using System;
using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms.Registries
{
    [Serializable]
    public class PropEntry
    {
        public string propId = "";

        [Tooltip("Optional link to an InventoryItemDefinition in InteractableCatalog. " +
                 "When set and prefab is null, the item's dropPrefab is used instead — " +
                 "keeping props and items in a single place.")]
        [MLA_SIM.RegistryDropdown(MLA_SIM.RegistryType.Item)]
        public string itemId = "";

        [Tooltip("Direct prefab override. If null and itemId is set, falls back to " +
                 "the matching InventoryItemDefinition.dropPrefab from InteractableCatalog.")]
        public GameObject prefab;
        public string defaultBone = "RightHand";
        public Vector3 localOffset = Vector3.zero;
        public Vector3 localRotationEuler = Vector3.zero;
        [TextArea(2, 4)]
        public string description = "";

        /// <summary>
        /// Resolves the effective prefab: direct prefab first, then catalog item dropPrefab.
        /// </summary>
        public GameObject ResolvePrefab()
        {
            if (prefab != null) return prefab;
            if (string.IsNullOrEmpty(itemId)) return null;
            var catalog = MLA_SIM.InteractableCatalog.Instance;
            if (catalog == null) return null;
            var def = catalog.GetItemDefinition(itemId);
            return def != null ? def.dropPrefab : null;
        }
    }

    [CreateAssetMenu(fileName = "PropRegistry", menuName = "DOOMS/Prop Registry")]
    public class PropRegistrySO : ScriptableObject
    {
        private static PropRegistrySO _instance;
        public static PropRegistrySO Instance
        {
            get
            {
#if UNITY_EDITOR
                const string kCanonical = "Assets/Resources/Dooms/PropRegistry.asset";
                var reg = UnityEditor.AssetDatabase.LoadAssetAtPath<PropRegistrySO>(kCanonical);
                if (reg != null) return reg;
                var guids = UnityEditor.AssetDatabase.FindAssets("t:PropRegistrySO");
                foreach (var guid in guids)
                {
                    reg = UnityEditor.AssetDatabase.LoadAssetAtPath<PropRegistrySO>(
                        UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (reg != null) return reg;
                }
                return null;
#else
                if (_instance == null)
                {
                    _instance = Resources.Load<PropRegistrySO>("Dooms/PropRegistry");
                    if (_instance == null)
                    {
                        var assets = Resources.FindObjectsOfTypeAll<PropRegistrySO>();
                        if (assets != null && assets.Length > 0) _instance = assets[0];
                    }
                }
                return _instance;
#endif
            }
        }

        [Tooltip("List of named props that can be selected from registries, sequences, and animators.")]
        public List<PropEntry> props = new List<PropEntry>();

        public PropEntry FindProp(string propId)
        {
            if (string.IsNullOrEmpty(propId) || props == null) return null;
            return props.Find(p => p != null && string.Equals(p.propId, propId, StringComparison.OrdinalIgnoreCase));
        }

        public string[] GetPropIds()
        {
            if (props == null) return Array.Empty<string>();
            var ids = new List<string>();
            for (int i = 0; i < props.Count; i++)
            {
                if (props[i] != null && !string.IsNullOrWhiteSpace(props[i].propId))
                {
                    ids.Add(props[i].propId);
                }
            }
            return ids.ToArray();
        }
    }
}
