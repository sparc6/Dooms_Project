using System.Collections.Generic;
using UnityEngine;

public enum InteractionCompletionMode
{
    Timed,
    UntilNextClickOrInteraction,
    AnimationEvent
}

[DisallowMultipleComponent]
public class AgentInteractionZone : MonoBehaviour
{
    [Header("Preset")]
    [SerializeField] private AgentInteractionPreset preset;
    [SerializeField] private bool usePresetSettings = true;

    [Header("Tags")]
    [SerializeField] private List<string> tags = new List<string>();

    [Header("Agent Pose")]
    [SerializeField] private Transform poseReference;
    [SerializeField] private bool snapAgentToPose = true;

    [Header("Prop")]
    [SerializeField] private Transform prop;
    [SerializeField] private Transform propHome;
    [SerializeField] private Transform attachmentTargetOverride;
    [SerializeField] private string attachmentBoneName = "CC_Base_R_Hand";
    [SerializeField] private Vector3 attachedLocalPosition;
    [SerializeField] private Vector3 attachedLocalEulerAngles;
    [SerializeField] private Vector3 attachedLocalScale = Vector3.one;
    [SerializeField] private float propAttachmentTransitionDuration = 0.25f;
    [SerializeField] private AnimationCurve propAttachmentTransitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private bool returnPropWhenDone = true;

    [Header("Prop Return Offset")]
    [SerializeField] private Vector3 returnedLocalPosition;
    [SerializeField] private Vector3 returnedLocalEulerAngles;

    [Header("Animator")]
    [SerializeField] private string animatorBoolName = "nail";
    [SerializeField] private InteractionCompletionMode completionMode = InteractionCompletionMode.Timed;
    [SerializeField] private float actionDuration = 2f;
    [SerializeField] private bool resetBoolWhenDone = true;

    public AgentInteractionPreset Preset => preset;
    public Transform PoseReference => poseReference;
    public bool SnapAgentToPose => UsesPreset ? preset.SnapAgentToPose : snapAgentToPose;
    public Transform Prop => prop;
    public Transform PropHome => propHome;
    public Transform AttachmentTargetOverride => attachmentTargetOverride;
    public string AttachmentBoneName => UsesPreset ? preset.AttachmentBoneName : attachmentBoneName;
    public Vector3 AttachedLocalPosition => UsesPreset ? preset.AttachedLocalPosition : attachedLocalPosition;
    public Vector3 AttachedLocalEulerAngles => UsesPreset ? preset.AttachedLocalEulerAngles : attachedLocalEulerAngles;
    public Vector3 AttachedLocalScale => UsesPreset ? preset.AttachedLocalScale : attachedLocalScale;
    public float PropAttachmentTransitionDuration => UsesPreset ? preset.PropAttachmentTransitionDuration : propAttachmentTransitionDuration;
    public AnimationCurve PropAttachmentTransitionCurve => UsesPreset ? preset.PropAttachmentTransitionCurve : propAttachmentTransitionCurve;
    public bool ReturnPropWhenDone => UsesPreset ? preset.ReturnPropWhenDone : returnPropWhenDone;
    public Vector3 ReturnedLocalPosition => UsesPreset ? preset.ReturnedLocalPosition : returnedLocalPosition;
    public Vector3 ReturnedLocalEulerAngles => UsesPreset ? preset.ReturnedLocalEulerAngles : returnedLocalEulerAngles;
    public string AnimatorBoolName => UsesPreset ? preset.AnimatorBoolName : animatorBoolName;
    public InteractionCompletionMode CompletionMode => UsesPreset ? preset.CompletionMode : completionMode;
    public float ActionDuration => UsesPreset ? preset.ActionDuration : actionDuration;
    public bool ResetBoolWhenDone => UsesPreset ? preset.ResetBoolWhenDone : resetBoolWhenDone;

    private bool UsesPreset => usePresetSettings && preset != null;

    public bool HasTag(string tag)
    {
        return AgentInteractionPreset.HasTag(tags, tag) || (preset != null && preset.HasTag(tag));
    }

    private void OnValidate()
    {
        actionDuration = Mathf.Max(0f, actionDuration);
        propAttachmentTransitionDuration = Mathf.Max(0f, propAttachmentTransitionDuration);
        if (attachedLocalScale == Vector3.zero)
        {
            attachedLocalScale = Vector3.one;
        }

        if (propAttachmentTransitionCurve == null)
        {
            propAttachmentTransitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }
    }
}
