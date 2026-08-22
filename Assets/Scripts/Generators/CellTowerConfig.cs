using System;
using UnityEngine;

[CreateAssetMenu(fileName = "CellTowerConfig", menuName = "The Tower/Cell Tower Config")]
public sealed class CellTowerConfig : ScriptableObject
{
    public enum LengthAxis
    {
        X,
        Y,
        Z
    }

    [Serializable]
    public sealed class Module
    {
        [SerializeField] private GameObject prefab;
        [SerializeField] private LengthAxis lengthAxis = LengthAxis.X;
        [SerializeField, Min(0f)] private float referenceLength;
        [SerializeField] private Vector3 positionOffset;
        [SerializeField] private Vector3 rotationOffset;
        [SerializeField] private Vector3 scaleMultiplier = Vector3.one;
        [SerializeField] private bool centerOnPlacement = true;

        public GameObject Prefab => prefab;
        public LengthAxis Axis => lengthAxis;
        public float ReferenceLength => referenceLength;
        public Vector3 PositionOffset => positionOffset;
        public Vector3 RotationOffset => rotationOffset;
        public Vector3 ScaleMultiplier => scaleMultiplier;
        public bool CenterOnPlacement => centerOnPlacement;
        public bool IsAssigned => prefab != null;
    }

    [Serializable]
    public sealed class WeightedModule
    {
        [SerializeField] private Module module = new Module();
        [SerializeField, Min(0f)] private float weight = 1f;

        public Module Module => module;
        public float Weight => weight;
        public bool IsUsable => module != null && module.IsAssigned && weight > 0f;
    }

    [Serializable]
    public sealed class AntennaPair
    {
        [SerializeField] private string id;
        [SerializeField] private Module antenna = new Module();
        [SerializeField] private Module frame = new Module();
        [SerializeField, Min(0f)] private float weight = 1f;

        public string Id => id;
        public Module Antenna => antenna;
        public Module Frame => frame;
        public float Weight => weight;
        public bool IsUsable => antenna != null
            && frame != null
            && antenna.IsAssigned
            && frame.IsAssigned
            && weight > 0f;
    }

    [Header("Generation")]
    [SerializeField] private int generatedLayer;

    [Header("Structure")]
    [SerializeField] private Module centralPole = new Module();
    [SerializeField] private Module radialSupport = new Module();
    [SerializeField] private Module verticalHorizontalSupport = new Module();
    [SerializeField] private Module diagonalSupport = new Module();
    [SerializeField] private Module ladder = new Module();

    [Header("Platforms")]
    [SerializeField] private WeightedModule[] threeSidedPlatforms = Array.Empty<WeightedModule>();
    [SerializeField] private WeightedModule[] fourSidedPlatforms = Array.Empty<WeightedModule>();
    [SerializeField] private WeightedModule[] fiveSidedPlatforms = Array.Empty<WeightedModule>();

    [Header("Antennas")]
    [SerializeField] private AntennaPair[] antennaPairs = Array.Empty<AntennaPair>();

    [Header("Randomize Ranges")]
    [SerializeField, Min(3)] private int minimumLevels = 15;
    [SerializeField, Min(3)] private int maximumLevels = 20;
    [SerializeField, Min(1)] private int startingLevelMinimumOffset = 10;
    [SerializeField, Min(1)] private int startingLevelMaximumOffset = 5;

    public int GeneratedLayer => generatedLayer;
    public Module CentralPole => centralPole;
    public Module RadialSupport => radialSupport;
    public Module VerticalHorizontalSupport => verticalHorizontalSupport;
    public Module DiagonalSupport => diagonalSupport;
    public Module Ladder => ladder;
    public AntennaPair[] AntennaPairs => antennaPairs;
    public int MinimumLevels => minimumLevels;
    public int MaximumLevels => maximumLevels;
    public int StartingLevelMinimumOffset => startingLevelMinimumOffset;
    public int StartingLevelMaximumOffset => startingLevelMaximumOffset;

    public WeightedModule[] GetPlatforms(int sideCount)
    {
        return sideCount switch
        {
            3 => threeSidedPlatforms,
            4 => fourSidedPlatforms,
            5 => fiveSidedPlatforms,
            _ => Array.Empty<WeightedModule>()
        };
    }

    public WeightedModule PickPlatform(int sideCount, float randomValue)
    {
        return PickWeighted(GetPlatforms(sideCount), randomValue);
    }

    public AntennaPair PickAntenna(float randomValue)
    {
        float totalWeight = 0f;
        for (int index = 0; index < antennaPairs.Length; index++)
        {
            AntennaPair pair = antennaPairs[index];
            if (pair != null && pair.IsUsable)
                totalWeight += pair.Weight;
        }

        if (totalWeight <= 0f)
            return null;

        float target = Mathf.Clamp01(randomValue) * totalWeight;
        AntennaPair fallback = null;
        for (int index = 0; index < antennaPairs.Length; index++)
        {
            AntennaPair pair = antennaPairs[index];
            if (pair == null || !pair.IsUsable)
                continue;

            fallback = pair;
            target -= pair.Weight;
            if (target <= 0f)
                return pair;
        }

        return fallback;
    }

    public bool TryValidate(out string message)
    {
        if (generatedLayer < 0
            || generatedLayer > 31
            || string.IsNullOrEmpty(LayerMask.LayerToName(generatedLayer)))
        {
            return Fail("Select a valid named Unity layer for generated cell-tower objects.", out message);
        }

        if (!IsAssigned(centralPole))
            return Fail("Assign the central pole model in CellTowerConfig.", out message);
        if (!IsAssigned(radialSupport))
            return Fail("Assign the radial support model in CellTowerConfig.", out message);
        if (!IsAssigned(verticalHorizontalSupport))
            return Fail("Assign the vertical/horizontal support model in CellTowerConfig.", out message);
        if (!IsAssigned(diagonalSupport))
            return Fail("Assign the diagonal support model in CellTowerConfig.", out message);
        if (!IsAssigned(ladder))
            return Fail("Assign the ladder model in CellTowerConfig.", out message);

        for (int sideCount = 3; sideCount <= 5; sideCount++)
        {
            WeightedModule[] platforms = GetPlatforms(sideCount);
            bool hasPlatform = false;
            for (int index = 0; index < platforms.Length; index++)
            {
                if (platforms[index] != null && platforms[index].IsUsable)
                {
                    hasPlatform = true;
                    break;
                }
            }

            if (!hasPlatform)
                return Fail($"Assign at least one usable {sideCount}-sided platform.", out message);
        }

        bool hasAntenna = false;
        for (int index = 0; index < antennaPairs.Length; index++)
        {
            if (antennaPairs[index] != null && antennaPairs[index].IsUsable)
            {
                hasAntenna = true;
                break;
            }
        }

        if (!hasAntenna)
            return Fail("Assign at least one complete antenna and frame pair.", out message);
        if (minimumLevels < 3 || maximumLevels < minimumLevels)
            return Fail("Randomized level range is invalid.", out message);
        if (startingLevelMinimumOffset < startingLevelMaximumOffset)
            return Fail("Starting-level minimum offset must be greater than or equal to its maximum offset.", out message);

        message = string.Empty;
        return true;
    }

    private static WeightedModule PickWeighted(WeightedModule[] modules, float randomValue)
    {
        float totalWeight = 0f;
        for (int index = 0; index < modules.Length; index++)
        {
            WeightedModule module = modules[index];
            if (module != null && module.IsUsable)
                totalWeight += module.Weight;
        }

        if (totalWeight <= 0f)
            return null;

        float target = Mathf.Clamp01(randomValue) * totalWeight;
        WeightedModule fallback = null;
        for (int index = 0; index < modules.Length; index++)
        {
            WeightedModule module = modules[index];
            if (module == null || !module.IsUsable)
                continue;

            fallback = module;
            target -= module.Weight;
            if (target <= 0f)
                return module;
        }

        return fallback;
    }

    private static bool IsAssigned(Module module)
    {
        return module != null && module.IsAssigned;
    }

    private static bool Fail(string failureMessage, out string message)
    {
        message = failureMessage;
        return false;
    }

    private void OnValidate()
    {
        minimumLevels = Mathf.Max(3, minimumLevels);
        maximumLevels = Mathf.Max(minimumLevels, maximumLevels);
        startingLevelMinimumOffset = Mathf.Max(1, startingLevelMinimumOffset);
        startingLevelMaximumOffset = Mathf.Clamp(
            startingLevelMaximumOffset,
            1,
            startingLevelMinimumOffset);
    }
}
