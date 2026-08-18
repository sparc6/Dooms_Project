using System.Collections;
using UnityEngine;

namespace MLA_SIM
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AnimatorPropDriver))]
    [AddComponentMenu("DOOMS/Animation/Animator Thrown Prop Driver")]
    public sealed class AnimatorThrownPropDriver : MonoBehaviour
    {
        [Header("Held Prop")]
        [Tooltip("Animator state whose attached prop is hidden and restored by the animation events.")]
        public string stateName = "Throwing_Molotov";
        [Tooltip("Fallback launch transform when the attached state prop is unavailable.")]
        public string launchBoneName = "CC_Base_R_Hand";
        public Vector3 fallbackLocalPositionOffset = Vector3.zero;

        [Header("Thrown Prop")]
        [Tooltip("Prefab cloned at the held prop pose when ReleaseProp is called.")]
        public GameObject projectilePrefab;
        [Min(0f)] public float forwardSpeed = 7f;
        [Min(0f)] public float upwardSpeed = 3f;
        public float sidewaysSpeed = 0f;
        [Tooltip("World-space spin applied to the released prop in radians per second.")]
        public Vector3 angularVelocity = new Vector3(8f, 4f, 6f);
        [Min(0.01f)] public float mass = 0.35f;
        [Min(0f)] public float linearDamping = 0.05f;
        [Min(0f)] public float angularDamping = 0.05f;
        [Min(0.1f)] public float projectileLifetime = 4f;
        public bool addConvexCollider = true;

        [Header("Optional Release Effect")]
        public ParticleSystem releaseEffectPrefab;
        [Min(0.1f)] public float releaseEffectLifetime = 3f;

        private AnimatorPropDriver _propDriver;

        private void Awake()
        {
            _propDriver = GetComponent<AnimatorPropDriver>();
        }

        // Animation Event: hide the held prop and launch one physical copy.
        public void ReleaseProp()
        {
            if (_propDriver == null) _propDriver = GetComponent<AnimatorPropDriver>();

            GameObject heldProp = null;
            bool hasHeldProp = _propDriver != null
                && _propDriver.TryGetActiveStateProp(stateName, out heldProp);

            Transform launchTransform = hasHeldProp && heldProp != null
                ? heldProp.transform
                : FindBoneRecursive(transform, launchBoneName) ?? transform;
            Vector3 launchPosition = launchTransform.position;
            Quaternion launchRotation = launchTransform.rotation;
            Vector3 launchScale = launchTransform.lossyScale;

            if (!hasHeldProp)
            {
                launchPosition += launchTransform.TransformVector(fallbackLocalPositionOffset);
            }
            else
            {
                _propDriver.SetActiveStatePropVisible(stateName, false);
            }

            GameObject source = projectilePrefab != null ? projectilePrefab : heldProp;
            if (source == null)
            {
                Debug.LogWarning($"[AnimatorThrownPropDriver] No projectile prefab or active prop was available on '{name}'.", this);
                return;
            }

            GameObject projectile = Instantiate(source, launchPosition, launchRotation);
            projectile.name = source.name + "_Thrown";
            projectile.transform.localScale = launchScale;
            projectile.SetActive(true);

            Rigidbody body = projectile.GetComponent<Rigidbody>();
            if (body == null) body = projectile.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;
            body.detectCollisions = true;
            body.mass = Mathf.Max(0.01f, mass);
            body.linearDamping = Mathf.Max(0f, linearDamping);
            body.angularDamping = Mathf.Max(0f, angularDamping);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = transform.forward * forwardSpeed
                + Vector3.up * upwardSpeed
                + transform.right * sidewaysSpeed;
            body.angularVelocity = angularVelocity;

            if (addConvexCollider) EnsureCollider(projectile);
            PlayReleaseEffect(launchPosition, launchRotation);
            StartCoroutine(DestroyProjectileAfter(projectile, projectileLifetime));
        }

        // Animation Event: reveal the state prop for the next throw cycle.
        public void RestoreProp()
        {
            if (_propDriver == null) _propDriver = GetComponent<AnimatorPropDriver>();
            if (_propDriver != null) _propDriver.SetActiveStatePropVisible(stateName, true);
        }

        private void PlayReleaseEffect(Vector3 position, Quaternion rotation)
        {
            if (releaseEffectPrefab == null) return;

            ParticleSystem effect = Instantiate(releaseEffectPrefab, position, rotation);
            effect.Play(true);
            Destroy(effect.gameObject, Mathf.Max(0.1f, releaseEffectLifetime));
        }

        private static void EnsureCollider(GameObject projectile)
        {
            if (projectile.GetComponentInChildren<Collider>(true) != null) return;

            MeshFilter[] meshFilters = projectile.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter meshFilter = meshFilters[i];
                if (meshFilter == null || meshFilter.sharedMesh == null) continue;

                MeshCollider collider = meshFilter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = meshFilter.sharedMesh;
                collider.convex = true;
                return;
            }
        }

        private static Transform FindBoneRecursive(Transform parent, string boneName)
        {
            if (parent == null || string.IsNullOrEmpty(boneName)) return null;
            if (string.Equals(parent.name, boneName, System.StringComparison.OrdinalIgnoreCase)) return parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform result = FindBoneRecursive(parent.GetChild(i), boneName);
                if (result != null) return result;
            }
            return null;
        }

        private static IEnumerator DestroyProjectileAfter(GameObject projectile, float delay)
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, delay));
            if (projectile != null) Destroy(projectile);
        }

        private void OnDisable()
        {
            RestoreProp();
        }
    }
}
