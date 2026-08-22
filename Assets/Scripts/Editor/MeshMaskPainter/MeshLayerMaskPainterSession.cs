using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheTower.EditorTools.MeshMaskPainter
{
    internal sealed class MeshLayerMaskPainterSession : IDisposable
    {
        private static readonly int SourceTexId = Shader.PropertyToID("_SourceTex");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int CoverageTexId = Shader.PropertyToID("_CoverageTex");
        private static readonly int BrushPositionId = Shader.PropertyToID("_BrushPosition");
        private static readonly int BrushNormalId = Shader.PropertyToID("_BrushNormal");
        private static readonly int BrushRadiusId = Shader.PropertyToID("_BrushRadius");
        private static readonly int BrushDepthId = Shader.PropertyToID("_BrushDepth");
        private static readonly int BrushNormalThresholdId = Shader.PropertyToID("_BrushNormalThreshold");
        private static readonly int BrushStrengthId = Shader.PropertyToID("_BrushStrength");
        private static readonly int BrushHardnessId = Shader.PropertyToID("_BrushHardness");
        private static readonly int TargetLayerId = Shader.PropertyToID("_TargetLayer");
        private static readonly int LayerCountId = Shader.PropertyToID("_LayerCount");
        private static readonly int TexelSizeId = Shader.PropertyToID("_PainterTexelSize");
        private static readonly int UvTileOffsetId = Shader.PropertyToID("_PainterUvTileOffset");

        private readonly MeshLayerMaskTarget _target;
        private readonly Material _paintMaterial;
        private readonly MaterialPropertyBlock _originalPropertyBlock = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _previewPropertyBlock = new MaterialPropertyBlock();
        private readonly List<HistoryState> _history = new List<HistoryState>();
        private readonly HashSet<Vector2Int> _usedUvTiles = new HashSet<Vector2Int>();

        private RenderTexture _current;
        private RenderTexture _scratch;
        private int _historyIndex = -1;
        private long _nextHistoryId = 1;
        private long _savedHistoryId = -1;
        private bool _strokeActive;
        private bool _strokeChanged;
        private string _assetPath;

        internal MeshLayerMaskPainterSession(MeshLayerMaskTarget target, Shader paintShader)
        {
            _target = target;
            _paintMaterial = new Material(paintShader) { hideFlags = HideFlags.HideAndDontSave };
            target.Renderer.GetPropertyBlock(_originalPropertyBlock, target.MaterialSlot);
            target.Renderer.GetPropertyBlock(_previewPropertyBlock, target.MaterialSlot);
        }

        internal bool HasTexture => _current;
        internal RenderTexture CurrentTexture => _current;
        internal string AssetPath => _assetPath;
        internal int Width => _current ? _current.width : 0;
        internal int Height => _current ? _current.height : 0;
        internal bool CanUndo => _historyIndex > 0;
        internal bool CanRedo => _historyIndex >= 0 && _historyIndex < _history.Count - 1;
        internal int UndoCount => Mathf.Max(0, _historyIndex);
        internal int RedoCount => Mathf.Max(0, _history.Count - _historyIndex - 1);
        internal bool IsDirty => HasTexture && (_savedHistoryId < 0 || CurrentHistoryId != _savedHistoryId);
        internal long HistoryBytes => HasTexture ? (long)_history.Count * _current.width * _current.height * 4L : 0L;

        internal void InitializeNew(int resolution)
        {
            AllocateWorkingTextures(resolution, resolution);
            Clear(_current, MeshLayerMaskUtility.InitialLayerZeroColor);
            Clear(_scratch, MeshLayerMaskUtility.InitialLayerZeroColor);
            _usedUvTiles.Clear();
            _assetPath = null;
            _savedHistoryId = -1;
            ResetHistory(markSaved: false);
            ApplyPreview();
        }

        internal void Load(Texture2D source)
        {
            if (!source)
                throw new ArgumentNullException(nameof(source));

            AllocateWorkingTextures(source.width, source.height);
            Graphics.Blit(source, _current);
            Graphics.Blit(source, _scratch);
            _usedUvTiles.Clear();
            _assetPath = AssetDatabase.GetAssetPath(source);
            if (!string.Equals(Path.GetExtension(_assetPath), ".png", StringComparison.OrdinalIgnoreCase) ||
                !_assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                _assetPath = null;
            }

            ResetHistory(markSaved: true);
            ApplyPreview();
        }

        internal void BeginStroke()
        {
            _strokeActive = HasTexture;
            _strokeChanged = false;
        }

        internal void Stamp(
            Vector3 position,
            Vector3 normal,
            Vector2 uv,
            int targetLayer,
            float radius,
            float hardness,
            float strength,
            float maximumAngle,
            float depthScale)
        {
            if (!_strokeActive || !HasTexture)
                return;

            Graphics.Blit(_current, _scratch);

            _paintMaterial.SetTexture(SourceTexId, _current);
            _paintMaterial.SetVector(BrushPositionId, position);
            _paintMaterial.SetVector(BrushNormalId, normal.normalized);
            _paintMaterial.SetFloat(BrushRadiusId, Mathf.Max(0.0001f, radius));
            _paintMaterial.SetFloat(BrushDepthId, Mathf.Max(0.0001f, radius * Mathf.Clamp(depthScale, 0.05f, 2f)));
            _paintMaterial.SetFloat(
                BrushNormalThresholdId,
                Mathf.Cos(Mathf.Clamp(maximumAngle, 0f, 90f) * Mathf.Deg2Rad));
            _paintMaterial.SetFloat(BrushStrengthId, Mathf.Clamp01(strength));
            _paintMaterial.SetFloat(BrushHardnessId, Mathf.Clamp01(hardness));
            _paintMaterial.SetInt(TargetLayerId, Mathf.Clamp(targetLayer, 0, 3));
            _paintMaterial.SetInt(LayerCountId, MeshLayerMaskUtility.GetLayerCount(_target.Material));

            DrawTargetSubmeshAroundUv(_scratch, 0, uv);
            SwapWorkingTextures();
            _strokeChanged = true;
            ApplyPreview();
        }

        internal bool EndStroke()
        {
            if (!_strokeActive)
                return false;

            _strokeActive = false;
            if (!_strokeChanged)
                return false;

            RemoveRedoStates();
            _history.Add(CreateHistoryState());
            _historyIndex = _history.Count - 1;
            return true;
        }

        internal void ClearRedoStates()
        {
            RemoveRedoStates();
        }

        internal void DropOldestUndoCommand()
        {
            if (_history.Count <= 1 || _historyIndex <= 0)
                return;

            long removedId = _history[0].Id;
            ReleaseRenderTexture(_history[0].Texture);
            _history.RemoveAt(0);
            _historyIndex--;
            if (_savedHistoryId == removedId)
                _savedHistoryId = -1;
        }

        internal bool Undo()
        {
            if (!CanUndo)
                return false;

            _historyIndex--;
            Graphics.Blit(_history[_historyIndex].Texture, _current);
            ApplyPreview();
            return true;
        }

        internal bool Redo()
        {
            if (!CanRedo)
                return false;

            _historyIndex++;
            Graphics.Blit(_history[_historyIndex].Texture, _current);
            ApplyPreview();
            return true;
        }

        internal Texture2D SavePngAndAssign(string assetPath)
        {
            if (!HasTexture)
                throw new InvalidOperationException("Нет рабочей текстуры для сохранения.");
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Текстура должна быть сохранена внутри Assets.", nameof(assetPath));

            assetPath = Path.ChangeExtension(assetPath.Replace('\\', '/'), ".png");
            Texture2D readable = CreateReadableSaveTexture();

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                throw new InvalidOperationException("Не удалось определить корень Unity-проекта.");

            string fullPath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? projectRoot);
            File.WriteAllBytes(fullPath, readable.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(readable);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ConfigureImporter(assetPath);

            Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (!imported)
                throw new InvalidOperationException($"Unity не смог импортировать маску '{assetPath}'.");

            UnityEditor.Undo.RecordObject(_target.Material, "Assign Layer Mask");
            _target.Material.SetTexture(MeshLayerMaskUtility.LayerMaskProperty, imported);
            EditorUtility.SetDirty(_target.Material);
            AssetDatabase.SaveAssets();

            _assetPath = assetPath;
            _savedHistoryId = CurrentHistoryId;
            return imported;
        }

        internal Texture2D CreateReadableSaveTexture()
        {
            if (!HasTexture)
                throw new InvalidOperationException("Нет рабочей текстуры для сохранения.");

            RenderTexture padded = CreatePaddedTexture();
            try
            {
                return ReadBack(padded);
            }
            finally
            {
                ReleaseRenderTexture(padded);
            }
        }

        public void Dispose()
        {
            if (_target.Renderer)
                _target.Renderer.SetPropertyBlock(_originalPropertyBlock, _target.MaterialSlot);

            ReleaseRenderTexture(_current);
            ReleaseRenderTexture(_scratch);
            _current = null;
            _scratch = null;
            ClearHistory();

            if (_paintMaterial)
                UnityEngine.Object.DestroyImmediate(_paintMaterial);
        }

        private long CurrentHistoryId => _historyIndex >= 0 && _historyIndex < _history.Count
            ? _history[_historyIndex].Id
            : -1;

        private void AllocateWorkingTextures(int width, int height)
        {
            ReleaseRenderTexture(_current);
            ReleaseRenderTexture(_scratch);
            ClearHistory();

            _current = CreateRenderTexture(width, height, "Layer Mask Working");
            _scratch = CreateRenderTexture(width, height, "Layer Mask Scratch");
        }

        private void ResetHistory(bool markSaved)
        {
            ClearHistory();
            HistoryState initial = CreateHistoryState();
            _history.Add(initial);
            _historyIndex = 0;
            if (markSaved)
                _savedHistoryId = initial.Id;
        }

        private HistoryState CreateHistoryState()
        {
            RenderTexture snapshot = CreateRenderTexture(_current.width, _current.height, "Layer Mask History");
            Graphics.Blit(_current, snapshot);
            return new HistoryState(_nextHistoryId++, snapshot);
        }

        private void RemoveRedoStates()
        {
            for (int index = _history.Count - 1; index > _historyIndex; index--)
            {
                ReleaseRenderTexture(_history[index].Texture);
                _history.RemoveAt(index);
            }
        }

        private void ClearHistory()
        {
            foreach (HistoryState state in _history)
                ReleaseRenderTexture(state.Texture);
            _history.Clear();
            _historyIndex = -1;
        }

        private void ApplyPreview()
        {
            if (!_target.Renderer || !_current)
                return;

            _previewPropertyBlock.SetTexture(MeshLayerMaskUtility.LayerMaskProperty, _current);
            _target.Renderer.SetPropertyBlock(_previewPropertyBlock, _target.MaterialSlot);
            SceneView.RepaintAll();
        }

        private void SwapWorkingTextures()
        {
            RenderTexture temporary = _current;
            _current = _scratch;
            _scratch = temporary;
        }

        private void DrawTargetSubmeshAroundUv(RenderTexture destination, int pass, Vector2 uv)
        {
            Vector2Int centerTile = new Vector2Int(Mathf.FloorToInt(uv.x), Mathf.FloorToInt(uv.y));
            RenderTexture previous = RenderTexture.active;
            Graphics.SetRenderTarget(destination);
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                var tile = new Vector2Int(centerTile.x + x, centerTile.y + y);
                _usedUvTiles.Add(tile);
                DrawTargetSubmeshTile(pass, tile);
            }
            RenderTexture.active = previous;
        }

        private void DrawCoverage(RenderTexture destination)
        {
            RenderTexture previous = RenderTexture.active;
            Graphics.SetRenderTarget(destination);
            if (_usedUvTiles.Count == 0)
            {
                DrawTargetSubmeshTile(1, Vector2Int.zero);
            }
            else
            {
                foreach (Vector2Int tile in _usedUvTiles)
                    DrawTargetSubmeshTile(1, tile);
            }
            RenderTexture.active = previous;
        }

        private void DrawTargetSubmeshTile(int pass, Vector2Int tile)
        {
            _paintMaterial.SetVector(UvTileOffsetId, new Vector4(tile.x, tile.y, 0f, 0f));
            _paintMaterial.SetPass(pass);
            Mesh mesh = _target.Filter ? _target.Filter.sharedMesh : _target.Mesh;
            if (mesh)
                Graphics.DrawMeshNow(mesh, _target.Renderer.localToWorldMatrix, _target.MaterialSlot);
        }

        private RenderTexture CreatePaddedTexture()
        {
            RenderTexture previouslyActive = RenderTexture.active;
            int width = _current.width;
            int height = _current.height;
            int paddingIterations = Mathf.Max(2, Mathf.Max(width, height) / 256);

            RenderTexture colorA = CreateRenderTexture(width, height, "Layer Mask Save Color A");
            RenderTexture colorB = CreateRenderTexture(width, height, "Layer Mask Save Color B");
            RenderTexture coverageA = CreateRenderTexture(width, height, "Layer Mask Coverage A", FilterMode.Point);
            RenderTexture coverageB = CreateRenderTexture(width, height, "Layer Mask Coverage B", FilterMode.Point);

            Graphics.Blit(_current, colorA);
            Clear(coverageA, Color.black);
            DrawCoverage(coverageA);

            _paintMaterial.SetVector(TexelSizeId, new Vector4(1f / width, 1f / height, width, height));
            for (int iteration = 0; iteration < paddingIterations; iteration++)
            {
                _paintMaterial.SetTexture(CoverageTexId, coverageA);
                _paintMaterial.SetTexture(MainTexId, colorA);
                Graphics.Blit(null, colorB, _paintMaterial, 2);
                _paintMaterial.SetTexture(MainTexId, coverageA);
                Graphics.Blit(null, coverageB, _paintMaterial, 3);

                Swap(ref colorA, ref colorB);
                Swap(ref coverageA, ref coverageB);
            }

            RenderTexture.active = previouslyActive;
            ReleaseRenderTexture(colorB);
            ReleaseRenderTexture(coverageA);
            ReleaseRenderTexture(coverageB);
            return colorA;
        }

        private static Texture2D ReadBack(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            texture.Apply(false, false);
            RenderTexture.active = previous;
            return texture;
        }

        private static void ConfigureImporter(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
                return;

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.crunchedCompression = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }

        private static RenderTexture CreateRenderTexture(int width, int height, string name, FilterMode filterMode = FilterMode.Bilinear)
        {
            var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = filterMode,
                wrapMode = TextureWrapMode.Repeat,
                useMipMap = false,
                autoGenerateMips = false
            };
            texture.Create();
            return texture;
        }

        private static void Clear(RenderTexture texture, Color color)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(false, true, color);
            RenderTexture.active = previous;
        }

        private static void ReleaseRenderTexture(RenderTexture texture)
        {
            if (!texture)
                return;

            if (RenderTexture.active == texture)
                RenderTexture.active = null;
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static void Swap(ref RenderTexture left, ref RenderTexture right)
        {
            RenderTexture temporary = left;
            left = right;
            right = temporary;
        }

        private sealed class HistoryState
        {
            internal HistoryState(long id, RenderTexture texture)
            {
                Id = id;
                Texture = texture;
            }

            internal long Id { get; }
            internal RenderTexture Texture { get; }
        }
    }
}
