using UnityEngine;

[AddComponentMenu("")]
[DisallowMultipleComponent]
public sealed class CellTowerGeneratedRootMarker : MonoBehaviour
{
    [SerializeField, HideInInspector] private CellTowerGenerator owner;

    public CellTowerGenerator Owner => owner;

    public void Initialize(CellTowerGenerator generator)
    {
        owner = generator;
    }
}
