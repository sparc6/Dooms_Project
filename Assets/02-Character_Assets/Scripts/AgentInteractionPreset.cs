using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewAgentInteractionPreset", menuName = "Character Assets/Agent Interaction Preset")]
public class AgentInteractionPreset : ScriptableObject
{
    [Header("Identification")]
    [SerializeField] private string presetId;
    [SerializeField] private List<string> tags = new List<string>();

    [Header("Agent Pose")]
    [SerializeField] private bool snapAgentToPose = true;

    [Header("Prop Attachment")]
    [SerializeField] private string attachmentBoneName = "CC_Base_R_Hand";
    [SerializeField] private Vector3 attachedLocalPosition;
    [SerializeField] private Vector3 attachedLocalEulerAngles;
    [SerializeField] private Vector3 attachedLocalScale = Vector3.one;
    [SerializeField] private bool returnPropWhenDone = true;

    [Header("Prop Return Offset")]
    [SerializeField] private Vector3 returnedLocalPosition;
    [SerializeField] private Vector3 returnedLocalEulerAngles;

    [Header("Animator")]
    [SerializeField] private string animatorBoolName = "nail";
    [SerializeField] private InteractionCompletionMode completionMode = InteractionCompletionMode.Timed;
    [SerializeField] private float actionDuration = 2f;
    [SerializeField] private bool resetBoolWhenDone = true;

    public string PresetId => presetId;
    public bool SnapAgentToPose => snapAgentToPose;
    public string AttachmentBoneName => attachmentBoneName;
    public Vector3 AttachedLocalPosition => attachedLocalPosition;
    public Vector3 AttachedLocalEulerAngles => attachedLocalEulerAngles;
    public Vector3 AttachedLocalScale => attachedLocalScale;
    public bool ReturnPropWhenDone => returnPropWhenDone;
    public Vector3 ReturnedLocalPosition => returnedLocalPosition;
    public Vector3 ReturnedLocalEulerAngles => returnedLocalEulerAngles;
    public string AnimatorBoolName => animatorBoolName;
    public InteractionCompletionMode CompletionMode => completionMode;
    public float ActionDuration => actionDuration;
    public bool ResetBoolWhenDone => resetBoolWhenDone;

    public bool HasTag(string tag)
    {
        return HasTag(tags, tag);
    }

    private void OnValidate()
    {
        actionDuration = Mathf.Max(0f, actionDuration);
        if (attachedLocalScale == Vector3.zero)
        {
            attachedLocalScale = Vector3.one;
        }
    }

    internal static bool HasTag(List<string> sourceTags, string tag)
    {
        if (sourceTags == null || string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        for (int i = 0; i < sourceTags.Count; i++)
        {
            if (string.Equals(sourceTags[i], tag, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
