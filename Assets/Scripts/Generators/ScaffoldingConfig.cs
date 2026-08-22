using System;
using UnityEngine;

[CreateAssetMenu(fileName = "ScaffoldingConfig", menuName = "The Tower/Scaffolding Config")]
public sealed class ScaffoldingConfig : ScriptableObject
{
    public enum SectionForwardAxis
    {
        X,
        Z
    }

    [SerializeField] private GameObject sectionPrefab;
    [SerializeField, Min(0.001f)] private float sectionLength = 2.5f;
    [SerializeField, Min(0.001f)] private float floorHeight = 2f;
    [SerializeField] private SectionForwardAxis sectionForwardAxis = SectionForwardAxis.X;

    public GameObject SectionPrefab => sectionPrefab;
    public float SectionLength => sectionLength;
    public float FloorHeight => floorHeight;
    public SectionForwardAxis ForwardAxis => sectionForwardAxis;

    public static event Action<ScaffoldingConfig> Changed;

    private void OnValidate()
    {
        Changed?.Invoke(this);
    }
}
