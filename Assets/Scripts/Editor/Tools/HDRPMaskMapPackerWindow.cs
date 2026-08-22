using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// HDRP Mask Map Packer for Unity 6 (HDRP).
/// Packs Metallic (R), Ambient Occlusion (G), Detail Mask (B) и Smoothness/Roughness (A)
/// в одну Mask‑карту, совместимую с HDRP/Lit.
/// </summary>
public class HDRPMaskMapPackerWindow : EditorWindow
{
    private enum Tab { SinglePack, BatchOrmToMads, BatchRmaToMads, BatchUrpToHdrp, TerrainPack }
    private Tab _tab;
    
    // ───────────────────────────── INPUT MAPS ─────────────────────────────
    private Texture2D _metallic;
    private Texture2D _ambientOcclusion;
    private Texture2D _detailMask;
    private Texture2D _smoothness;

    // Fallback colours
    private Color _metallicColor   = Color.black;
    private Color _aoColor         = Color.white;
    private Color _detailMaskColor = Color.white;
    private Color _smoothnessColor = Color.white;

    // Invert toggles
    private bool _invertMetallic;
    private bool _invertAO;
    private bool _invertDetail;
    private bool _invertSmoothness;

    // Read Smoothness from alpha instead of red
    private bool _smoothnessFromAlpha;
    
    private readonly List<Texture2D> _ormeList = new();
    private readonly List<Texture2D> _rmaList = new();
    private readonly List<Texture2D> _urpMapList = new();
    private Vector2 _scroll;
    private Vector2 _urpScroll;
    private string _madsTag      = "_MADS";
    private OutputFormat _madsFmt = OutputFormat.PNG;
    private Color _detailMaskFallback = Color.white;
    private bool _trimSuffixBatch = true;
    private string _rmaMadsTag = "_MADS";
    private OutputFormat _rmaMadsFmt = OutputFormat.PNG;
    private Color _rmaDetailMaskFallback = Color.white;
    private bool _trimSuffixRmaBatch = true;
    private string _urpMadsTag = "_MADS";
    private OutputFormat _urpMadsFmt = OutputFormat.PNG;
    private Color _urpDetailMaskFallback = Color.white;
    private bool _trimSuffixUrpBatch = true;

    // ───────────────────────────── OUTPUT ─────────────────────────────
    private string _fileTag = "_MaskMap";
    private enum OutputFormat { PNG, JPG }
    private OutputFormat _format = OutputFormat.PNG;
    private bool _trimLastSuffix = true;

    // ───────────────────────────── TERRAIN INPUT ─────────────────────────────
    private Texture2D _terrainColor;
    private Texture2D _terrainSmoothness;
    private Texture2D _terrainNormal;
    private Texture2D _terrainAO;
    private Texture2D _terrainHeight;

    private Color _terrainColorFallback = Color.white;
    private float _terrainSmoothnessFallback = 0.5f;
    private float _terrainAoFallback = 1f;
    private float _terrainHeightFallback = 0f;
    private float _terrainNormalXFallback = 0.5f;
    private float _terrainNormalYFallback = 0.5f;

    private bool _terrainSmoothnessInvert;
    private bool _terrainAoInvert;
    private bool _terrainHeightInvert;
    private Channel _terrainSmoothnessChannel = Channel.R;
    private Channel _terrainAoChannel = Channel.R;
    private Channel _terrainHeightChannel = Channel.R;

    private string _terrainCsTag = "_CS";
    private string _terrainNohTag = "_NOH";
    private OutputFormat _terrainFormat = OutputFormat.PNG;
    private bool _terrainTrimSuffix = true;

    // ───────────────────────────── MENU ─────────────────────────────
    [MenuItem("Tools/HDRP Mask Map Packer")]
    private static void ShowWindow()
    {
        var w = GetWindow<HDRPMaskMapPackerWindow>(true, "HDRP Mask Map Packer");
        w.minSize = new Vector2(520, 680);
    }

    // ───────────────────────────── GUI ─────────────────────────────
    private void OnGUI()
    {
        _tab = (Tab)GUILayout.Toolbar((int)_tab,
            new[] { "Single pack", "Batch ORME → MADS", "Batch RMA → MADS", "URP to HDRP", "Terrain CS/NOH" }, GUILayout.Height(24));

        GUILayout.Space(4);

        switch (_tab)
        {
            case Tab.SinglePack:
                DrawSinglePackGUI();   // ваш прежний OnGUI разбит на метод
                break;
            case Tab.BatchOrmToMads:
                DrawBatchGui();
                break;
            case Tab.BatchRmaToMads:
                DrawBatchRmaGui();
                break;
            case Tab.BatchUrpToHdrp:
                DrawBatchUrpToHdrpGui();
                break;
            case Tab.TerrainPack:
                DrawTerrainPackGui();
                break;
        }
    }
    private void DrawSinglePackGUI()
    {
        using (new GUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Source Textures", EditorStyles.boldLabel);
            DrawTextureField(ref _metallic,         ref _invertMetallic,   ref _metallicColor,   "Metallic (R)");
            DrawTextureField(ref _ambientOcclusion, ref _invertAO,         ref _aoColor,         "Ambient Occlusion (G)");
            DrawTextureField(ref _detailMask,       ref _invertDetail,     ref _detailMaskColor, "Detail Mask (B)");

            using (new GUILayout.HorizontalScope())
            {
                _smoothness = (Texture2D)EditorGUILayout.ObjectField("Smoothness (A)", _smoothness, typeof(Texture2D), false);
                _invertSmoothness = EditorGUILayout.ToggleLeft("Invert", _invertSmoothness, GUILayout.Width(60));
            }
            if (!_smoothness)
                _smoothnessColor = EditorGUILayout.ColorField("Fallback", _smoothnessColor);
            _smoothnessFromAlpha = EditorGUILayout.ToggleLeft("Read Alpha channel (grayscale in A)", _smoothnessFromAlpha);
        }

        GUILayout.Space(4);
        using (new GUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Output Settings", EditorStyles.boldLabel);
            _fileTag        = EditorGUILayout.TextField("File Tag", _fileTag);
            _format         = (OutputFormat)EditorGUILayout.EnumPopup("File Format", _format);
            _trimLastSuffix = EditorGUILayout.ToggleLeft("Remove last suffix in base name", _trimLastSuffix);
        }

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Pack & Save", GUILayout.Height(40)))
            PackAndSave();
    }
    
    private void DrawBatchGui()
    {
        EditorGUILayout.HelpBox(
            "Перетащите сюда UE-маски (ORME) или добавьте через Object Field. " +
            "Нажмите «Convert All», чтобы получить HDRP Mask Map (MADS) в тех же папках.",
            MessageType.Info);

        // Drag-&-Drop зона
        Rect dropRect = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, "Drag ORME textures here", EditorStyles.helpBox);

        HandleDragAndDrop(dropRect, _ormeList);

        // Список
        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(300));
        for (int i = 0; i < _ormeList.Count; ++i)
        {
            GUILayout.BeginHorizontal();
            _ormeList[i] = (Texture2D)EditorGUILayout.ObjectField(_ormeList[i],
                              typeof(Texture2D), false);
            if (GUILayout.Button("✕", GUILayout.Width(20)))
            {
                _ormeList.RemoveAt(i);
                --i;
            }
            GUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        // Fallback для Detail Mask и output
        using (new GUILayout.VerticalScope("box"))
        {
            _madsTag        = EditorGUILayout.TextField("File Tag",      _madsTag);
            _madsFmt        = (OutputFormat)EditorGUILayout.EnumPopup("File Format", _madsFmt);
            _trimSuffixBatch= EditorGUILayout.ToggleLeft("Remove last suffix in base name",
                                                         _trimSuffixBatch);
            _detailMaskFallback = EditorGUILayout.ColorField("Detail Mask Fallback (B)",
                                                             _detailMaskFallback);
        }

        GUILayout.FlexibleSpace();
        GUI.enabled = _ormeList.Count > 0;
        if (GUILayout.Button($"Convert All ({_ormeList.Count})", GUILayout.Height(38)))
            ConvertAllOrme();
        GUI.enabled = true;
    }

    private void DrawBatchRmaGui()
    {
        EditorGUILayout.HelpBox(
            "Перетащите сюда RMA-маски или добавьте через Object Field. " +
            "Нажмите «Convert All», чтобы получить HDRP Mask Map (MADS) в тех же папках.",
            MessageType.Info);

        Rect dropRect = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, "Drag RMA textures here", EditorStyles.helpBox);

        HandleDragAndDrop(dropRect, _rmaList);

        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(300));
        for (int i = 0; i < _rmaList.Count; ++i)
        {
            GUILayout.BeginHorizontal();
            _rmaList[i] = (Texture2D)EditorGUILayout.ObjectField(_rmaList[i],
                              typeof(Texture2D), false);
            if (GUILayout.Button("✕", GUILayout.Width(20)))
            {
                _rmaList.RemoveAt(i);
                --i;
            }
            GUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        using (new GUILayout.VerticalScope("box"))
        {
            _rmaMadsTag = EditorGUILayout.TextField("File Tag", _rmaMadsTag);
            _rmaMadsFmt = (OutputFormat)EditorGUILayout.EnumPopup("File Format", _rmaMadsFmt);
            _trimSuffixRmaBatch = EditorGUILayout.ToggleLeft("Remove last suffix in base name",
                                                             _trimSuffixRmaBatch);
            _rmaDetailMaskFallback = EditorGUILayout.ColorField("Detail Mask Fallback (B)",
                                                                 _rmaDetailMaskFallback);
        }

        GUILayout.FlexibleSpace();
        GUI.enabled = _rmaList.Count > 0;
        if (GUILayout.Button($"Convert All ({_rmaList.Count})", GUILayout.Height(38)))
            ConvertAllRma();
        GUI.enabled = true;
    }

    private void DrawBatchUrpToHdrpGui()
    {
        EditorGUILayout.HelpBox(
            "Перетащите сюда карты URP: *_AO (AO в R) и *_MS (Metallic в R, Smoothness в A). " +
            "Нажмите «Convert All», чтобы собрать HDRP Mask Map (MADS) в тех же папках.",
            MessageType.Info);

        Rect dropRect = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, "Drag URP AO/MS textures here", EditorStyles.helpBox);

        HandleDragAndDrop(dropRect, _urpMapList);

        _urpScroll = EditorGUILayout.BeginScrollView(_urpScroll, GUILayout.Height(300));
        for (int i = 0; i < _urpMapList.Count; ++i)
        {
            GUILayout.BeginHorizontal();
            _urpMapList[i] = (Texture2D)EditorGUILayout.ObjectField(_urpMapList[i],
                              typeof(Texture2D), false);
            if (GUILayout.Button("✕", GUILayout.Width(20)))
            {
                _urpMapList.RemoveAt(i);
                --i;
            }
            GUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        using (new GUILayout.VerticalScope("box"))
        {
            _urpMadsTag = EditorGUILayout.TextField("File Tag", _urpMadsTag);
            _urpMadsFmt = (OutputFormat)EditorGUILayout.EnumPopup("File Format", _urpMadsFmt);
            _trimSuffixUrpBatch = EditorGUILayout.ToggleLeft("Remove last suffix in base name",
                                                             _trimSuffixUrpBatch);
            _urpDetailMaskFallback = EditorGUILayout.ColorField("Detail Mask Fallback (B)",
                                                                 _urpDetailMaskFallback);
        }

        GUILayout.FlexibleSpace();
        GUI.enabled = _urpMapList.Count > 0;
        if (GUILayout.Button($"Convert All ({_urpMapList.Count})", GUILayout.Height(38)))
            ConvertAllUrpToHdrp();
        GUI.enabled = true;
    }

    private void DrawTerrainPackGui()
    {
        using (new GUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Источник (Terrain)", EditorStyles.boldLabel);
            _terrainColor = (Texture2D)EditorGUILayout.ObjectField(
                new GUIContent("Color (RGB)", "Цветовая карта террейна (RGB)."),
                _terrainColor, typeof(Texture2D), false);
            if (!_terrainColor)
                _terrainColorFallback = EditorGUILayout.ColorField(
                    new GUIContent("Fallback Color", "Цвет по умолчанию, если карта не задана."),
                    _terrainColorFallback);

            using (new GUILayout.HorizontalScope())
            {
                _terrainSmoothness = (Texture2D)EditorGUILayout.ObjectField(
                    new GUIContent("Smoothness (A)", "Карта гладкости, канал выбирается в списке."),
                    _terrainSmoothness, typeof(Texture2D), false);
                _terrainSmoothnessInvert = EditorGUILayout.ToggleLeft("Invert", _terrainSmoothnessInvert, GUILayout.Width(60));
            }
            using (new EditorGUI.DisabledScope(!_terrainSmoothness))
            {
                _terrainSmoothnessChannel = (Channel)EditorGUILayout.EnumPopup(
                    new GUIContent("Канал Smoothness", "Из какого канала брать гладкость."),
                    _terrainSmoothnessChannel);
            }
            if (!_terrainSmoothness)
                _terrainSmoothnessFallback = EditorGUILayout.Slider(
                    new GUIContent("Fallback Smoothness", "Значение гладкости по умолчанию."),
                    _terrainSmoothnessFallback, 0f, 1f);

            _terrainNormal = (Texture2D)EditorGUILayout.ObjectField(
                new GUIContent("Normal (RG)", "Нормаль террейна: R и G переносятся в RG."),
                _terrainNormal, typeof(Texture2D), false);
            if (!_terrainNormal)
            {
                _terrainNormalXFallback = EditorGUILayout.Slider(
                    new GUIContent("Fallback Normal X (R)", "X компонента нормали по умолчанию."),
                    _terrainNormalXFallback, 0f, 1f);
                _terrainNormalYFallback = EditorGUILayout.Slider(
                    new GUIContent("Fallback Normal Y (G)", "Y компонента нормали по умолчанию."),
                    _terrainNormalYFallback, 0f, 1f);
            }

            using (new GUILayout.HorizontalScope())
            {
                _terrainAO = (Texture2D)EditorGUILayout.ObjectField(
                    new GUIContent("AO (B)", "Карта окружающего затенения, канал выбирается в списке."),
                    _terrainAO, typeof(Texture2D), false);
                _terrainAoInvert = EditorGUILayout.ToggleLeft("Invert", _terrainAoInvert, GUILayout.Width(60));
            }
            using (new EditorGUI.DisabledScope(!_terrainAO))
            {
                _terrainAoChannel = (Channel)EditorGUILayout.EnumPopup(
                    new GUIContent("Канал AO", "Из какого канала брать AO."),
                    _terrainAoChannel);
            }
            if (!_terrainAO)
                _terrainAoFallback = EditorGUILayout.Slider(
                    new GUIContent("Fallback AO", "AO по умолчанию."),
                    _terrainAoFallback, 0f, 1f);

            using (new GUILayout.HorizontalScope())
            {
                _terrainHeight = (Texture2D)EditorGUILayout.ObjectField(
                    new GUIContent("Height (A)", "Карта высоты, канал выбирается в списке."),
                    _terrainHeight, typeof(Texture2D), false);
                _terrainHeightInvert = EditorGUILayout.ToggleLeft("Invert", _terrainHeightInvert, GUILayout.Width(60));
            }
            using (new EditorGUI.DisabledScope(!_terrainHeight))
            {
                _terrainHeightChannel = (Channel)EditorGUILayout.EnumPopup(
                    new GUIContent("Канал Height", "Из какого канала брать высоту."),
                    _terrainHeightChannel);
            }
            if (!_terrainHeight)
                _terrainHeightFallback = EditorGUILayout.Slider(
                    new GUIContent("Fallback Height", "Высота по умолчанию."),
                    _terrainHeightFallback, 0f, 1f);
        }

        GUILayout.Space(4);
        using (new GUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Output Settings", EditorStyles.boldLabel);
            _terrainCsTag = EditorGUILayout.TextField(
                new GUIContent("CS Tag", "Суффикс для текстуры Color+Smoothness."),
                _terrainCsTag);
            _terrainNohTag = EditorGUILayout.TextField(
                new GUIContent("NOH Tag", "Суффикс для текстуры Normal+AO+Height."),
                _terrainNohTag);
            _terrainFormat = (OutputFormat)EditorGUILayout.EnumPopup(
                new GUIContent("File Format", "Формат выходных текстур."),
                _terrainFormat);
            _terrainTrimSuffix = EditorGUILayout.ToggleLeft(
                "Remove last suffix in base name", _terrainTrimSuffix);
        }

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Pack CS & NOH", GUILayout.Height(40)))
            PackTerrainMaps();
    }

    private static void HandleDragAndDrop(Rect dropRect, List<Texture2D> targetList)
    {
        Event evt = Event.current;
        if (!dropRect.Contains(evt.mousePosition)) return;

        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
                {
                    if (obj is Texture2D tex && !targetList.Contains(tex))
                        targetList.Add(tex);
                }
            }
            evt.Use();
        }
    }

    private void DrawTextureField(ref Texture2D tex, ref bool invert, ref Color fallback, string label)
    {
        using (new GUILayout.HorizontalScope())
        {
            tex = (Texture2D)EditorGUILayout.ObjectField(label, tex, typeof(Texture2D), false);
            invert = EditorGUILayout.ToggleLeft("Invert", invert, GUILayout.Width(60));
        }
        if (!tex)
            fallback = EditorGUILayout.ColorField("Fallback", fallback);
    }

    // ───────────────────────────── MAIN ─────────────────────────────
    private void PackAndSave()
    {
        Texture2D reference = _metallic ?? _ambientOcclusion ?? _detailMask ?? _smoothness;
        if (!reference)
        {
            EditorUtility.DisplayDialog("No Textures", "Assign at least one source texture.", "OK");
            return;
        }

        string refPath  = AssetDatabase.GetAssetPath(reference);
        string directory = Path.GetDirectoryName(refPath);
        string outputDirectory = directory;
        int width  = reference.width;
        int height = reference.height;

        // Ensure textures are readable and remember which ones we toggled
        var m  = EnsureReadable(_metallic,         out bool mRevert,  out TextureImporter mImp);
        var ao = EnsureReadable(_ambientOcclusion, out bool aoRevert, out TextureImporter aoImp);
        var dm = EnsureReadable(_detailMask,       out bool dmRevert, out TextureImporter dmImp);
        var sm = EnsureReadable(_smoothness,       out bool smRevert, out TextureImporter smImp);

        Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                pixels[i] = new Color(
                    Sample(m,  x, y, _metallicColor.r,   _invertMetallic,   Channel.R),
                    Sample(ao, x, y, _aoColor.r,         _invertAO,         Channel.R),
                    Sample(dm, x, y, _detailMaskColor.r, _invertDetail,     Channel.R),
                    Sample(sm, x, y, _smoothnessColor.r, _invertSmoothness, _smoothnessFromAlpha ? Channel.A : Channel.R));
            }
        }
        result.SetPixels(pixels);
        result.Apply();

        // Restore readability flags
        RestoreReadable(mRevert,  mImp);
        RestoreReadable(aoRevert, aoImp);
        RestoreReadable(dmRevert, dmImp);
        RestoreReadable(smRevert, smImp);

        // Build filename
        string baseName = reference.name;
        if (_trimLastSuffix)
            baseName = Regex.Replace(baseName, "_[^_]+$", "");
        string fileName = baseName + _fileTag + (_format == OutputFormat.PNG ? ".png" : ".jpg");
        string fullPath = Path.Combine(outputDirectory, fileName);

        File.WriteAllBytes(fullPath, _format == OutputFormat.PNG ? result.EncodeToPNG() : result.EncodeToJPG(95));
        AssetDatabase.Refresh();

        var outImp = (TextureImporter)AssetImporter.GetAtPath(fullPath);
        outImp.textureType          = TextureImporterType.Default;
        outImp.sRGBTexture          = false;
        outImp.alphaSource          = TextureImporterAlphaSource.FromInput;
        outImp.alphaIsTransparency  = false;
        outImp.SaveAndReimport();
    }

    // ───────────────────────────── HELPERS ─────────────────────────────
    private enum Channel { R, G, B, A }

    private static float Sample(Texture2D tex, int x, int y, float fallback, bool invert, Channel ch)
    {
        float v;
        if (tex)
        {
            Color c = tex.GetPixel(x, y);
            v = ch switch
            {
                Channel.R => c.r,
                Channel.G => c.g,
                Channel.B => c.b,
                Channel.A => c.a,
                _ => c.r
            };
        }
        else v = fallback;
        if (invert) v = 1f - v;
        return v;
    }

    private void PackTerrainMaps()
    {
        Texture2D reference = _terrainColor ?? _terrainSmoothness ?? _terrainNormal ?? _terrainAO ?? _terrainHeight;
        if (!reference)
        {
            EditorUtility.DisplayDialog("No Textures", "Назначьте хотя бы одну карту для террейна.", "OK");
            return;
        }

        string refPath = AssetDatabase.GetAssetPath(reference);
        string directory = Path.GetDirectoryName(refPath);
        string outputDirectory = EnsurePackedDirectory(directory);
        int width = reference.width;
        int height = reference.height;

        var color = EnsureReadable(_terrainColor, out bool colorRevert, out TextureImporter colorImp);
        var smooth = EnsureReadable(_terrainSmoothness, out bool smoothRevert, out TextureImporter smoothImp);
        var normal = EnsureReadable(_terrainNormal, out bool normalRevert, out TextureImporter normalImp);
        var ao = EnsureReadable(_terrainAO, out bool aoRevert, out TextureImporter aoImp);
        var heightMap = EnsureReadable(_terrainHeight, out bool heightRevert, out TextureImporter heightImp);

        Texture2D cs = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        Texture2D noh = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        Color[] csPixels = new Color[width * height];
        Color[] nohPixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            float u = (x + 0.5f) / width;
            float v = (y + 0.5f) / height;

            Color colorSample = color ? color.GetPixelBilinear(u, v) : _terrainColorFallback;
            float smoothValue = SampleBilinear(
                smooth, u, v, _terrainSmoothnessFallback, _terrainSmoothnessInvert, _terrainSmoothnessChannel);

            SampleNormalBilinear(normal, normalImp, u, v, _terrainNormalXFallback, _terrainNormalYFallback,
                out float normalX, out float normalY);
            float aoValue = SampleBilinear(
                ao, u, v, _terrainAoFallback, _terrainAoInvert, _terrainAoChannel);
            float heightValue = SampleBilinear(
                heightMap, u, v, _terrainHeightFallback, _terrainHeightInvert, _terrainHeightChannel);

            csPixels[i] = new Color(colorSample.r, colorSample.g, colorSample.b, smoothValue);
            nohPixels[i] = new Color(normalX, normalY, aoValue, heightValue);
        }

        cs.SetPixels(csPixels);
        cs.Apply();
        noh.SetPixels(nohPixels);
        noh.Apply();

        RestoreReadable(colorRevert, colorImp);
        RestoreReadable(smoothRevert, smoothImp);
        RestoreReadable(normalRevert, normalImp);
        RestoreReadable(aoRevert, aoImp);
        RestoreReadable(heightRevert, heightImp);

        string baseName = reference.name;
        if (_terrainTrimSuffix)
            baseName = Regex.Replace(baseName, "_[^_]+$", "");

        SaveTexture(cs, Path.Combine(outputDirectory, baseName + _terrainCsTag), _terrainFormat, true);
        SaveTexture(noh, Path.Combine(outputDirectory, baseName + _terrainNohTag), _terrainFormat, false);

        AssetDatabase.Refresh();
    }

    private void ConvertAllOrme()
    {
        int done = 0;
        foreach (Texture2D src in _ormeList)
        {
            if (!src) continue;

            EnsureReadable(src, out bool revert, out TextureImporter imp);

            int w = src.width;
            int h = src.height;
            Texture2D dst = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            Color[] pix  = new Color[w * h];
            Color   fall = _detailMaskFallback;

            for (int y = 0; y < h; ++y)
            for (int x = 0; x < w; ++x)
            {
                int i = y * w + x;
                Color c = src.GetPixel(x, y);
                // R: Metallic ← B
                // G: AO       ← R
                // B: Detail   ← fallback (или A)
                // A: Smooth   ← 1-Roughness(G)
                float smooth = 1f - c.g;
                pix[i] = new Color(c.b, c.r, c.a, smooth);
            }
            dst.SetPixels(pix);
            dst.Apply();

            SaveMask(dst, src, _madsTag, _madsFmt, _trimSuffixBatch);

            RestoreReadable(revert, imp);
            ++done;
        }

        AssetDatabase.Refresh();
    }

    private void ConvertAllRma()
    {
        int done = 0;
        foreach (Texture2D src in _rmaList)
        {
            if (!src) continue;

            EnsureReadable(src, out bool revert, out TextureImporter imp);

            int w = src.width;
            int h = src.height;
            Texture2D dst = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            Color[] pix = new Color[w * h];
            Color fall = _rmaDetailMaskFallback;

            for (int y = 0; y < h; ++y)
            for (int x = 0; x < w; ++x)
            {
                int i = y * w + x;
                Color c = src.GetPixel(x, y);
                // R: Metallic ← G
                // G: AO       ← B
                // B: Detail   ← fallback
                // A: Smooth   ← 1-Roughness(R)
                float smooth = 1f - c.r;
                pix[i] = new Color(c.g, c.b, fall.r, smooth);
            }
            dst.SetPixels(pix);
            dst.Apply();

            SaveMask(dst, src, _rmaMadsTag, _rmaMadsFmt, _trimSuffixRmaBatch);

            RestoreReadable(revert, imp);
            ++done;
        }

        AssetDatabase.Refresh();
    }

    private enum UrpMapKind
    {
        AO,
        MS
    }

    private static bool TryExtractUrpBaseName(string textureName, out string baseName, out UrpMapKind mapKind)
    {
        if (textureName.EndsWith("_AO", System.StringComparison.OrdinalIgnoreCase))
        {
            baseName = textureName.Substring(0, textureName.Length - 3);
            mapKind = UrpMapKind.AO;
            return true;
        }

        if (textureName.EndsWith("_MS", System.StringComparison.OrdinalIgnoreCase))
        {
            baseName = textureName.Substring(0, textureName.Length - 3);
            mapKind = UrpMapKind.MS;
            return true;
        }

        baseName = string.Empty;
        mapKind = UrpMapKind.AO;
        return false;
    }

    private void ConvertAllUrpToHdrp()
    {
        var aoByBase = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);
        var msByBase = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);

        foreach (Texture2D tex in _urpMapList)
        {
            if (!tex) continue;
            if (!TryExtractUrpBaseName(tex.name, out string baseName, out UrpMapKind mapKind))
                continue;

            if (mapKind == UrpMapKind.AO)
                aoByBase[baseName] = tex;
            else
                msByBase[baseName] = tex;
        }

        int done = 0;
        foreach (var pair in msByBase)
        {
            string baseName = pair.Key;
            Texture2D ms = pair.Value;

            if (!aoByBase.TryGetValue(baseName, out Texture2D ao))
            {
                Debug.LogWarning($"[HDRPMaskMapPacker] Пропущена пара '{baseName}': не найдена карта *_AO.");
                continue;
            }

            EnsureReadable(ms, out bool msRevert, out TextureImporter msImp);
            EnsureReadable(ao, out bool aoRevert, out TextureImporter aoImp);

            if (ms.width != ao.width || ms.height != ao.height)
            {
                RestoreReadable(msRevert, msImp);
                RestoreReadable(aoRevert, aoImp);
                Debug.LogWarning(
                    $"[HDRPMaskMapPacker] Пропущена пара '{baseName}': разные размеры AO({ao.width}x{ao.height}) и MS({ms.width}x{ms.height}).");
                continue;
            }

            int w = ms.width;
            int h = ms.height;
            Texture2D dst = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            Color[] pix = new Color[w * h];
            float detailFallback = _urpDetailMaskFallback.r;

            for (int y = 0; y < h; ++y)
            for (int x = 0; x < w; ++x)
            {
                int i = y * w + x;
                Color msPixel = ms.GetPixel(x, y);
                Color aoPixel = ao.GetPixel(x, y);
                pix[i] = new Color(msPixel.r, aoPixel.r, detailFallback, msPixel.a);
            }

            dst.SetPixels(pix);
            dst.Apply();

            SaveMask(dst, ms, _urpMadsTag, _urpMadsFmt, _trimSuffixUrpBatch);

            RestoreReadable(msRevert, msImp);
            RestoreReadable(aoRevert, aoImp);
            ++done;
        }

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("URP to HDRP", $"Готово: {done} mask-карт создано.", "OK");
    }

    private static void SaveMask(Texture2D texOut, Texture2D reference,
        string tag, OutputFormat fmt, bool trimSuffix)
    {
        string refPath  = AssetDatabase.GetAssetPath(reference);
        string dir      = Path.GetDirectoryName(refPath);
        string outputDir = EnsurePackedDirectory(dir);
        string baseName = reference.name;
        if (trimSuffix) baseName = Regex.Replace(baseName, "_[^_]+$", "");
        string fileName = baseName + tag + (fmt == OutputFormat.PNG ? ".png" : ".jpg");
        string path     = Path.Combine(outputDir, fileName);

        // 1. Записываем файл
        File.WriteAllBytes(path,
            fmt == OutputFormat.PNG ? texOut.EncodeToPNG() : texOut.EncodeToJPG(95));

        // 2. Сообщаем Unity, что появился новый ассет
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        // 3. Теперь импортёр точно не null
        if (AssetImporter.GetAtPath(path) is TextureImporter imp)
        {
            imp.textureType         = TextureImporterType.Default;
            imp.sRGBTexture         = false;
            imp.alphaSource         = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = false;
            imp.SaveAndReimport();
        }
    }

    private static void SaveTexture(Texture2D texOut, string pathBase, OutputFormat fmt, bool srgb)
    {
        string fileName = pathBase + (fmt == OutputFormat.PNG ? ".png" : ".jpg");
        string path = fileName;

        File.WriteAllBytes(path, fmt == OutputFormat.PNG ? texOut.EncodeToPNG() : texOut.EncodeToJPG(95));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        if (AssetImporter.GetAtPath(path) is TextureImporter imp)
        {
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = srgb;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = false;
            imp.SaveAndReimport();
        }
    }

    private static float SampleBilinear(Texture2D tex, float u, float v, float fallback, bool invert, Channel channel)
    {
        float value;
        if (tex)
        {
            Color color = tex.GetPixelBilinear(u, v);
            value = channel switch
            {
                Channel.R => color.r,
                Channel.G => color.g,
                Channel.B => color.b,
                Channel.A => color.a,
                _ => color.r
            };
        }
        else
        {
            value = fallback;
        }

        return invert ? 1f - value : value;
    }

    private static void SampleNormalBilinear(Texture2D tex, TextureImporter importer, float u, float v,
        float fallbackX, float fallbackY, out float normalX, out float normalY)
    {
        if (!tex)
        {
            normalX = fallbackX;
            normalY = fallbackY;
            return;
        }

        Color color = tex.GetPixelBilinear(u, v);
        if (importer != null && importer.textureType == TextureImporterType.NormalMap)
        {
            normalX = color.a;
            normalY = color.g;
            return;
        }

        normalX = color.r;
        normalY = color.g;
    }

    private static string EnsurePackedDirectory(string baseDirectory)
    {
        string outputDirectory = Path.Combine(baseDirectory, "Packed");
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            AssetDatabase.Refresh();
        }
        return outputDirectory;
    }

    
    /// <summary>
    /// Ensure the texture is readable. Returns the same texture (not a copy).
    /// </summary>
    private static Texture2D EnsureReadable(Texture2D tex, out bool reverted, out TextureImporter importer)
    {
        reverted = false;
        importer = null;
        if (!tex) return null;

        string path = AssetDatabase.GetAssetPath(tex);
        importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (!importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
            reverted = true;
        }
        return tex;
    }

    /// <summary>
    /// Revert readability if we enabled it temporarily.
    /// </summary>
    private static void RestoreReadable(bool revert, TextureImporter importer)
    {
        if (revert && importer)
        {
            importer.isReadable = false;
            importer.SaveAndReimport();
        }
    }
}
