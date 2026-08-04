using UnityEngine;
using UnityEngine.AI;

namespace MLA_SIM
{
    public enum PairedAnimationSex
    {
        Unspecified,
        Male,
        Female
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(AnimatorLocomotionDriver))]
    [AddComponentMenu("DOOMS/Animation/Paired Animation Participant")]
    public sealed class PairedAnimationParticipant : MonoBehaviour
    {
        public PairedAnimationSex sex = PairedAnimationSex.Unspecified;

        public AnimatorLocomotionDriver Driver { get; private set; }
        public NavMeshAgent NavAgent { get; private set; }
        public Animator Animator { get; private set; }

        private void Awake()
        {
            CacheComponents();
        }

        public void CacheComponents()
        {
            Driver = GetComponent<AnimatorLocomotionDriver>();
            NavAgent = GetComponent<NavMeshAgent>();
            Animator = GetComponent<Animator>();
        }
    }
}
