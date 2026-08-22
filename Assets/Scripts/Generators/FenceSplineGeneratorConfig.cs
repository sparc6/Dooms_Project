using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Конфиг генератора заборов вдоль сплайна.
/// </summary>
[CreateAssetMenu(fileName = "FenceSplineGeneratorConfig", menuName = "Configs/Graphics/Road/FenceSplineGeneratorConfig")]
public sealed class FenceSplineGeneratorConfig : ScriptableObject
{
    [Header("State")]
    [Tooltip("Включить генерацию заборов.")]
    [SerializeField] private bool _enabled = true;

    [Header("Tiles")]
    [Tooltip("Список тайлов забора. Длина секции рассчитывается автоматически по геометрии префаба.")]
    [SerializeField] private List<GameObject> _tiles = new List<GameObject>();

    [Tooltip("Продольная ось тайлов, вдоль которой автоматически считается длина префабов.")]
    [SerializeField] private FenceTileLengthAxis _tileLengthAxis = FenceTileLengthAxis.X;

    [Tooltip("Ось ориентации тайлов вдоль хорды сплайна. Отрицательные оси разворачивают направление секции.")]
    [SerializeField] private FenceTileOrientationAxis _tileOrientationAxis = FenceTileOrientationAxis.PositiveX;

    [Header("Spline Range")]
    [Tooltip("Процент длины сплайна, с которого начинается генерация (0..100).")]
    [Range(0f, 100f)]
    [SerializeField] private float _trimStartPercent = 0f;

    [Tooltip("Процент длины сплайна, на котором заканчивается генерация (0..100).")]
    [Range(0f, 100f)]
    [SerializeField] private float _trimEndPercent = 100f;

    [Header("Curvature")]
    [Tooltip("Длина шага (м) для оценки кривизны по касательным вокруг текущей точки.")]
    [Min(0.05f)]
    [SerializeField] private float _curvatureProbeStep = 1f;

    [Tooltip("Нижний порог кривизны (град/м): ниже выбираются более длинные тайлы.")]
    [Min(0f)]
    [SerializeField] private float _lowCurvatureDegPerMeter = 1f;

    [Tooltip("Верхний порог кривизны (град/м): выше выбираются более короткие тайлы.")]
    [Min(0.01f)]
    [SerializeField] private float _highCurvatureDegPerMeter = 12f;

    [Header("Alignment")]
    [Tooltip("Вертикальный оффсет всей линии забора относительно сплайна.")]
    [SerializeField] private float _verticalOffset = 0f;

    [Tooltip("Перпендикулярное смещение всей линии забора относительно сплайна. Положительное значение смещает вправо по направлению генерации, отрицательное — влево.")]
    [SerializeField] private float _perpendicularOffset = 0f;

    [Tooltip("Зазор между соседними тайлами в метрах. Положительное значение увеличивает промежуток, отрицательное — даёт нахлёст.")]
    [SerializeField] private float _tileGap = 0f;

    [Tooltip("Минимальная остаточная длина в конце диапазона. Если остаток меньше, последний тайл не ставится.")]
    [Min(0f)]
    [SerializeField] private float _tailSkipLength = 0.15f;

    [Header("Linear Mode")]
    [Tooltip("В линейном режиме ставить такие же столбы на точках излома сплайна.")]
    [SerializeField] private bool _spawnPostsAtLinearPoints = false;

    [Tooltip("Минимальный нахлёст секций в линейном режиме. Используется как страховка от щелей между тайлами.")]
    [Min(0f)]
    [SerializeField] private float _linearTileOverlap = 0.02f;

    [Header("Final Post")]
    [Tooltip("Ставить финальный столб в конце диапазона генерации.")]
    [SerializeField] private bool _spawnFinalPost = true;

    [Tooltip("Префаб финального столба.")]
    [SerializeField] private GameObject _finalPostPrefab;

    [Tooltip("Поворот финального столба по касательной сплайна.")]
    [SerializeField] private bool _alignFinalPostToSpline = true;

    public bool Enabled => _enabled;
    public IReadOnlyList<GameObject> Tiles => _tiles;
    public FenceTileLengthAxis TileLengthAxis => _tileLengthAxis;
    public FenceTileOrientationAxis TileOrientationAxis => _tileOrientationAxis;
    public float TrimStartPercent => _trimStartPercent;
    public float TrimEndPercent => _trimEndPercent;
    public float CurvatureProbeStep => _curvatureProbeStep;
    public float LowCurvatureDegPerMeter => _lowCurvatureDegPerMeter;
    public float HighCurvatureDegPerMeter => _highCurvatureDegPerMeter;
    public float VerticalOffset => _verticalOffset;
    public float PerpendicularOffset => _perpendicularOffset;
    public float TileGap => _tileGap;
    public float TailSkipLength => _tailSkipLength;
    public bool SpawnPostsAtLinearPoints => _spawnPostsAtLinearPoints;
    public float LinearTileOverlap => _linearTileOverlap;
    public bool SpawnFinalPost => _spawnFinalPost;
    public GameObject FinalPostPrefab => _finalPostPrefab;
    public bool AlignFinalPostToSpline => _alignFinalPostToSpline;
}

public enum FenceTileLengthAxis
{
    X = 0,
    Y = 1,
    Z = 2
}

public enum FenceTileOrientationAxis
{
    PositiveX = 0,
    NegativeX = 1,
    PositiveY = 2,
    NegativeY = 3,
    PositiveZ = 4,
    NegativeZ = 5
}
