using System;
using System.Collections.Generic;
using UnityEngine;
using MLA_SIM.Dooms.Registries;

namespace MLA_SIM
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AnimatorLocomotionDriver))]
    [AddComponentMenu("DOOMS/Animator Prop Driver")]
    public class AnimatorPropDriver : MonoBehaviour
    {
        [System.Serializable]
        public class PropBinding
        {
            [Tooltip("The exact animator state name where this prop should be active.")]
            public string stateName = "";
            public GameObject propPrefab;
            [Tooltip("Name of the bone in the skeletal hierarchy (e.g. 'RightHand', 'LeftHand').")]
            public string boneName = "RightHand";
            public Vector3 localOffset = Vector3.zero;
            public Vector3 localRotationEuler = Vector3.zero;
        }

        public List<PropBinding> propBindings = new List<PropBinding>();

        [Header("Loadout (persistent prop)")]
        [Tooltip("Prop held at all times (e.g. a guard's gun), independent of animator state. Resolved from PropRegistrySO. Empty = no loadout prop.")]
        [RegistryDropdown(RegistryType.Prop)]
        public string loadoutPropId = "";
        [Tooltip("Bone to attach the loadout prop to. Empty = use the prop's defaultBone (or RightHand).")]
        public string loadoutBoneOverride = "";

        [System.Serializable]
        public class LocoStyleProp
        {
            [Tooltip("Locomotion style name (matches AnimatorLocomotionDriver loco style names, e.g. BTGun).")]
            public string styleName = "";
            [Tooltip("Prop carried while in this loco style. Resolved from PropRegistrySO.")]
            [RegistryDropdown(RegistryType.Prop)]
            public string propId = "";
            [Tooltip("Bone override. Empty = prop's defaultBone (or RightHand).")]
            public string boneOverride = "";
        }

        [Header("Loco-Style Props (carried while in a locomotion style)")]
        [Tooltip("Prop carried while the agent's locomotion style matches (e.g. BTGun -> MachineGun). Swapped/cleared automatically when the loco style changes. Independent of state props and the always-on loadout prop.")]
        public List<LocoStyleProp> locoStyleProps = new List<LocoStyleProp>();

        [Header("Debug")]
        [Tooltip("Enable verbose prop driver logging (expensive — disable in play mode)")]
        public bool showPropDriverLogs = false;

        [Header("Lifecycle")]
        [Tooltip("If true, props are destroyed when cleared. If false, they are de-parented and their Rigidbody settings are restored so they can keep simulating in the world.")]
        public bool destroyPropsOnClear = true;

        private AnimatorLocomotionDriver _driver;
        private readonly Dictionary<string, GameObject> _activeProps = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private string _interactionPropOverride = "";
        private GameObject _loadoutProp;
        private string _loadoutPropSpawned = "";
        private GameObject _styleProp;
        private string _stylePropSpawned = "";
        private readonly Dictionary<GameObject, List<RigidbodyState>> _rigidbodyStates = new Dictionary<GameObject, List<RigidbodyState>>();

        private class RigidbodyState
        {
            public Rigidbody rigidbody;
            public bool isKinematic;
            public bool useGravity;
            public bool detectCollisions;
            public Vector3 velocity;
            public Vector3 angularVelocity;
        }

        public void SetInteractionPropOverride(string propId)
        {
            _interactionPropOverride = propId ?? "";
        }

        public void ClearInteractionPropOverride()
        {
            _interactionPropOverride = "";
            ClearAllProps();
        }

        public bool TryGetActiveStateProp(string stateName, out GameObject propInstance)
        {
            propInstance = null;
            return !string.IsNullOrEmpty(stateName)
                && _activeProps.TryGetValue(stateName, out propInstance)
                && propInstance != null;
        }

        public bool SetActiveStatePropVisible(string stateName, bool visible)
        {
            if (!TryGetActiveStateProp(stateName, out GameObject propInstance)) return false;
            propInstance.SetActive(visible);
            return true;
        }

        private class ResolvedProp
        {
            public GameObject prefab;
            public string boneName = "RightHand";
            public Vector3 localOffset = Vector3.zero;
            public Vector3 localRotationEuler = Vector3.zero;
        }

        private void Awake()
        {
            _driver = GetComponent<AnimatorLocomotionDriver>();
            if (showPropDriverLogs)
                Debug.Log($"[DOOMS][AnimatorPropDriver] Awake on '{gameObject.name}'. Driver present={_driver != null}");
        }

        private void OnEnable()
        {
            if (_driver != null)
            {
                _driver.OnStateChanged += HandleStateChanged;
                _driver.OnLocoStyleChanged += HandleLocoStyleChanged;
                if (showPropDriverLogs)
                    Debug.Log($"[DOOMS][AnimatorPropDriver] OnEnable subscribed to state changes on '{gameObject.name}'.");
            }
            else
            {
                Debug.LogWarning($"[DOOMS][AnimatorPropDriver] OnEnable on '{gameObject.name}' but AnimatorLocomotionDriver was missing.");
            }

            SpawnLoadoutProp();
            // Initialise the style prop to match whatever loco style is already set.
            if (_driver != null) HandleLocoStyleChanged(_driver.CurrentLocoStyleName);
        }

        private void OnDisable()
        {
            if (_driver != null)
            {
                _driver.OnStateChanged -= HandleStateChanged;
                _driver.OnLocoStyleChanged -= HandleLocoStyleChanged;
                if (showPropDriverLogs)
                    Debug.Log($"[DOOMS][AnimatorPropDriver] OnDisable unsubscribed from state changes on '{gameObject.name}'.");
            }
            ClearAllProps();
            ClearLoadoutProp();
            ClearStyleProp();
        }

        // ─────────── Loco-style prop (carried while in a locomotion style) ───────────

        private string ResolveStyleProp(string styleName)
        {
            if (string.IsNullOrEmpty(styleName) || locoStyleProps == null) return "";
            for (int i = 0; i < locoStyleProps.Count; i++)
            {
                var e = locoStyleProps[i];
                if (e != null && !string.IsNullOrEmpty(e.styleName)
                    && string.Equals(e.styleName, styleName, StringComparison.OrdinalIgnoreCase))
                    return e.propId ?? "";
            }
            return "";
        }

        private void HandleLocoStyleChanged(string styleName)
        {
            string desired = ResolveStyleProp(styleName);
            if (string.Equals(_stylePropSpawned, desired, StringComparison.OrdinalIgnoreCase) && (_styleProp != null || string.IsNullOrEmpty(desired)))
                return; // already current

            ClearStyleProp();
            if (string.IsNullOrEmpty(desired)) return;

            // Find the matching entry for its bone override.
            string boneOverride = "";
            for (int i = 0; i < locoStyleProps.Count; i++)
            {
                var e = locoStyleProps[i];
                if (e != null && string.Equals(e.styleName, styleName, StringComparison.OrdinalIgnoreCase))
                { boneOverride = e.boneOverride; break; }
            }

            var registry = PropRegistrySO.Instance;
            var entry = registry != null ? registry.FindProp(desired) : null;
            var prefab = entry != null ? entry.ResolvePrefab() : null;
            if (prefab == null)
            {
                if (showPropDriverLogs)
                    Debug.LogWarning($"[DOOMS][AnimatorPropDriver] Loco-style prop '{desired}' (style '{styleName}') could not be resolved on '{gameObject.name}'.");
                return;
            }

            string boneName = !string.IsNullOrEmpty(boneOverride)
                ? boneOverride
                : (string.IsNullOrEmpty(entry.defaultBone) ? "RightHand" : entry.defaultBone);
            Transform bone = FindBoneRecursive(transform, boneName) ?? transform;

            _styleProp = Instantiate(prefab, bone);
            _styleProp.transform.localPosition = entry.localOffset;
            _styleProp.transform.localRotation = Quaternion.Euler(entry.localRotationEuler);
            ConfigurePropForAttachment(_styleProp);
            _stylePropSpawned = desired;

            if (showPropDriverLogs)
                Debug.Log($"[DOOMS][AnimatorPropDriver] Spawned loco-style prop '{prefab.name}' for style '{styleName}' on '{gameObject.name}'.");
        }

        private void ClearStyleProp()
        {
            ReleaseOrDestroyProp(_styleProp);
            _styleProp = null;
            _stylePropSpawned = "";
        }

        /// <summary>Swap the persistent loadout prop at runtime (empty clears it).</summary>
        public void SetLoadoutProp(string propId)
        {
            loadoutPropId = propId ?? "";
            SpawnLoadoutProp();
        }

        public void ClearLoadoutProp()
        {
            ReleaseOrDestroyProp(_loadoutProp);
            _loadoutProp = null;
            _loadoutPropSpawned = "";
        }

        // Spawn (or respawn) the always-held loadout prop. Independent of animator
        // state, so it is never touched by HandleStateChanged / ClearAllProps.
        private void SpawnLoadoutProp()
        {
            if (string.Equals(_loadoutPropSpawned, loadoutPropId ?? "", StringComparison.OrdinalIgnoreCase) && _loadoutProp != null)
                return; // already current

            if (_loadoutProp != null) { Destroy(_loadoutProp); _loadoutProp = null; }
            _loadoutPropSpawned = "";

            if (string.IsNullOrEmpty(loadoutPropId)) return;

            var registry = PropRegistrySO.Instance;
            var entry = registry != null ? registry.FindProp(loadoutPropId) : null;
            var prefab = entry != null ? entry.ResolvePrefab() : null;
            if (prefab == null)
            {
                if (showPropDriverLogs)
                    Debug.LogWarning($"[DOOMS][AnimatorPropDriver] Loadout prop '{loadoutPropId}' could not be resolved on '{gameObject.name}'.");
                return;
            }

            string boneName = !string.IsNullOrEmpty(loadoutBoneOverride)
                ? loadoutBoneOverride
                : (string.IsNullOrEmpty(entry.defaultBone) ? "RightHand" : entry.defaultBone);
            Transform bone = FindBoneRecursive(transform, boneName) ?? transform;

            _loadoutProp = Instantiate(prefab, bone);
            _loadoutProp.transform.localPosition = entry.localOffset;
            _loadoutProp.transform.localRotation = Quaternion.Euler(entry.localRotationEuler);
            ConfigurePropForAttachment(_loadoutProp);
            _loadoutPropSpawned = loadoutPropId;

            if (showPropDriverLogs)
                Debug.Log($"[DOOMS][AnimatorPropDriver] Spawned loadout prop '{prefab.name}' on bone '{boneName}' for '{gameObject.name}'.");
        }

        private void HandleStateChanged(string stateName)
        {
            if (showPropDriverLogs)
                Debug.Log($"[DOOMS][AnimatorPropDriver] State changed on '{gameObject.name}' -> '{stateName}'. Override='{_interactionPropOverride}'.");

            // 1. Check if we need to spawn any props for this state
            var match = ResolvePropForState(stateName);
            
            // Collect any currently active props that are NOT for this state, and destroy them
            List<string> keysToRemove = new List<string>();
            foreach (var kv in _activeProps)
            {
                if (match == null || !string.Equals(kv.Key, stateName, StringComparison.OrdinalIgnoreCase))
                {
                    keysToRemove.Add(kv.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                if (_activeProps.TryGetValue(key, out var propInstance))
                {
                    ReleaseOrDestroyProp(propInstance);
                }
                _activeProps.Remove(key);
            }

            // 2. Spawn matching prop if not already spawned
            if (match != null && match.prefab != null && !_activeProps.ContainsKey(stateName))
            {
                Transform bone = FindBoneRecursive(transform, match.boneName);
                if (bone == null)
                {
                    Debug.LogWarning($"[DOOMS][AnimatorPropDriver] Could not find bone '{match.boneName}' in children of '{gameObject.name}'. Defaulting to root.");
                    bone = transform;
                }

                var instance = Instantiate(match.prefab, bone);
                instance.transform.localPosition = match.localOffset;
                instance.transform.localRotation = Quaternion.Euler(match.localRotationEuler);
                ConfigurePropForAttachment(instance);
                _activeProps[stateName] = instance;
                if (showPropDriverLogs)
                    Debug.Log($"[DOOMS][AnimatorPropDriver] Spawned prop '{match.prefab.name}' on bone '{match.boneName}' for state '{stateName}'.");
            }
            else if (match == null && showPropDriverLogs)
            {
                Debug.Log($"[DOOMS][AnimatorPropDriver] No prop resolved for state '{stateName}' on '{gameObject.name}'.");
            }
        }

        private ResolvedProp ResolvePropForState(string stateName)
        {
            if (string.IsNullOrEmpty(stateName)) return null;

            if (!string.IsNullOrEmpty(_interactionPropOverride))
            {
                if (showPropDriverLogs)
                    Debug.Log($"[DOOMS][AnimatorPropDriver] Trying interaction override '{_interactionPropOverride}' for state '{stateName}' on '{gameObject.name}'.");
                var overrideRegistry = PropRegistrySO.Instance;
                var overrideEntry = overrideRegistry != null ? overrideRegistry.FindProp(_interactionPropOverride) : null;
                if (overrideEntry != null)
                {
                    var overrideResolved = overrideEntry.ResolvePrefab();
                    if (overrideResolved != null)
                    {
                        if (showPropDriverLogs)
                            Debug.Log($"[DOOMS][AnimatorPropDriver] Override '{_interactionPropOverride}' resolved to prefab '{overrideResolved.name}' for state '{stateName}'.");
                        return new ResolvedProp
                        {
                            prefab = overrideResolved,
                            boneName = string.IsNullOrEmpty(overrideEntry.defaultBone) ? "RightHand" : overrideEntry.defaultBone,
                            localOffset = overrideEntry.localOffset,
                            localRotationEuler = overrideEntry.localRotationEuler
                        };
                    }
                    if (showPropDriverLogs)
                        Debug.LogWarning($"[DOOMS][AnimatorPropDriver] Prop override '{_interactionPropOverride}': ResolvePrefab() returned null. Assign PropEntry.prefab or InventoryItemDefinition.dropPrefab.");
                }
                else if (showPropDriverLogs)
                {
                    Debug.LogWarning($"[DOOMS][AnimatorPropDriver] Prop override '{_interactionPropOverride}' was not found in PropRegistrySO.");
                }
            }

            var seqRegistry = AnimationSequenceRegistry.Instance;
            var sequence = seqRegistry != null ? seqRegistry.FindSequenceContainingState(stateName) : null;
            if (sequence != null && sequence.TryGetStepForState(stateName, out var step) && step != null && !string.IsNullOrEmpty(step.propId))
            {
                if (showPropDriverLogs)
                    Debug.Log($"[DOOMS][AnimatorPropDriver] Using sequence-backed propId '{step.propId}' for state '{stateName}'.");
                var propRegistry = PropRegistrySO.Instance;
                var propEntry = propRegistry != null ? propRegistry.FindProp(step.propId) : null;
                if (propEntry != null)
                {
                    var resolved = propEntry.ResolvePrefab();
                    if (resolved != null)
                    {
                        if (showPropDriverLogs)
                            Debug.Log($"[DOOMS][AnimatorPropDriver] Sequence propId '{step.propId}' resolved to prefab '{resolved.name}' for state '{stateName}'.");
                        return new ResolvedProp
                        {
                            prefab = resolved,
                            boneName = string.IsNullOrEmpty(propEntry.defaultBone) ? "RightHand" : propEntry.defaultBone,
                            localOffset = propEntry.localOffset,
                            localRotationEuler = propEntry.localRotationEuler
                        };
                    }
                }
                if (showPropDriverLogs)
                    Debug.LogWarning($"[DOOMS][AnimatorPropDriver] Sequence propId '{step.propId}' could not be resolved for state '{stateName}'.");
            }

            var legacy = propBindings.Find(b => string.Equals(b.stateName, stateName, StringComparison.OrdinalIgnoreCase));
            if (legacy == null || legacy.propPrefab == null) return null;

            if (showPropDriverLogs)
                Debug.Log($"[DOOMS][AnimatorPropDriver] Using legacy prop binding for state '{stateName}' on '{gameObject.name}'.");

            return new ResolvedProp
            {
                prefab = legacy.propPrefab,
                boneName = string.IsNullOrEmpty(legacy.boneName) ? "RightHand" : legacy.boneName,
                localOffset = legacy.localOffset,
                localRotationEuler = legacy.localRotationEuler
            };
        }

        private Transform FindBoneRecursive(Transform parent, string boneName)
        {
            if (string.Equals(parent.name, boneName, StringComparison.OrdinalIgnoreCase))
                return parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform result = FindBoneRecursive(parent.GetChild(i), boneName);
                if (result != null) return result;
            }

            return null;
        }

        private void ClearAllProps()
        {
            foreach (var propInstance in _activeProps.Values)
            {
                ReleaseOrDestroyProp(propInstance);
            }
            _activeProps.Clear();
        }

        private void ConfigurePropForAttachment(GameObject instance)
        {
            if (instance == null) return;

            var bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            if (bodies == null || bodies.Length == 0) return;

            var savedStates = new List<RigidbodyState>(bodies.Length);
            for (int i = 0; i < bodies.Length; i++)
            {
                var rb = bodies[i];
                if (rb == null) continue;

                savedStates.Add(new RigidbodyState
                {
                    rigidbody = rb,
                    isKinematic = rb.isKinematic,
                    useGravity = rb.useGravity,
                    detectCollisions = rb.detectCollisions,
                    velocity = rb.linearVelocity,
                    angularVelocity = rb.angularVelocity
                });

                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }

            if (savedStates.Count > 0)
            {
                _rigidbodyStates[instance] = savedStates;
            }
        }

        private void RestorePropPhysics(GameObject instance)
        {
            if (instance == null) return;

            if (!_rigidbodyStates.TryGetValue(instance, out var savedStates) || savedStates == null)
                return;

            for (int i = 0; i < savedStates.Count; i++)
            {
                var state = savedStates[i];
                if (state == null || state.rigidbody == null) continue;

                state.rigidbody.isKinematic = state.isKinematic;
                state.rigidbody.useGravity = state.useGravity;
                state.rigidbody.detectCollisions = state.detectCollisions;
                state.rigidbody.linearVelocity = state.velocity;
                state.rigidbody.angularVelocity = state.angularVelocity;
            }

            _rigidbodyStates.Remove(instance);
        }

        private void ReleaseOrDestroyProp(GameObject instance)
        {
            if (instance == null) return;

            if (destroyPropsOnClear)
            {
                _rigidbodyStates.Remove(instance);
                Destroy(instance);
                return;
            }

            instance.transform.SetParent(null, true);
            RestorePropPhysics(instance);
        }
    }
}
