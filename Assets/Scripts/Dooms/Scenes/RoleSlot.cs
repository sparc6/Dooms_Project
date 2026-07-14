using System;
using UnityEngine;

namespace MLA_SIM.Dooms.Scenes
{
    public enum RoleKind
    {
        Point,
        Area,
        Timeline
    }

    [Serializable]
    public class RoleSlot
    {
        public string roleId = "NewRole";

        [Tooltip("Type of orchestration slot.")]
        public RoleKind roleKind = RoleKind.Point;

        [RegistryDropdown(RegistryType.Faction)]
        [RegistryDropdownNC(RegistryType.Faction)]
        public string factionId = "";

        [Header("Point Role specific")]
        [RegistryDropdown(RegistryType.InteractionPoint)]
        [RegistryDropdownNC(RegistryType.InteractionPoint)]
        public string pointTag = "";

        [Tooltip("The animation or routine name to play from AgentActionSystem.")]
        [RegistryDropdown(RegistryType.AnimationState)]
        [RegistryDropdownNC(RegistryType.AnimationState)]
        public string animationState = "Idle";

        [Header("Area Role specific")]
        [Tooltip("The area tag or volume this agent should roam within.")]
        public string areaTag = "";
        [Tooltip("The roaming behavior: Patrol, Loiter, Mingle, Protest, Brawl.")]
        public string behavior = "Loiter";
        [Tooltip("The preferred locomotion blend tree or style (e.g. BT_March).")]
        public string preferredBlendTree = "";
        [Tooltip("Optional faction to pair up with for Talk/Fight social actions.")]
        public string pairWithFactionId = "";

        [Header("Timeline Role specific")]
        [Tooltip("Unique ID of the TimelineAnchor or choreography to occupy.")]
        public string timelineAnchorId = "";
        [Tooltip("The designated role slot ID inside that timeline.")]
        public string timelineSlotId = "";

        public int count = 1;
        public float arrivalTolerance = 2.0f;
        public bool optional = false;
    }
}
