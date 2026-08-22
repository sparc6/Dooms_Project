using System.Collections.Generic;
using UnityEngine;

namespace TheTower.EditorTools.MeshMaskPainter
{
    internal enum MeshPainterHistoryKind
    {
        Paint,
        Sculpt
    }

    internal sealed class MeshPainterCombinedHistory
    {
        internal const long BudgetBytes = 256L * 1024L * 1024L;

        private readonly List<MeshPainterHistoryKind> _commands = new List<MeshPainterHistoryKind>();
        private int _index = -1;

        internal bool CanUndo => _index >= 0;
        internal bool CanRedo => _index < _commands.Count - 1;
        internal int UndoCount => Mathf.Max(0, _index + 1);
        internal int RedoCount => Mathf.Max(0, _commands.Count - _index - 1);

        internal void PrepareNewStroke(MeshLayerMaskPainterSession paint, MeshSculptSession sculpt)
        {
            for (int index = _commands.Count - 1; index > _index; index--)
                _commands.RemoveAt(index);
            paint?.ClearRedoStates();
            sculpt?.ClearRedoStates();
        }

        internal void RegisterCompleted(
            MeshPainterHistoryKind kind,
            MeshLayerMaskPainterSession paint,
            MeshSculptSession sculpt)
        {
            _commands.Add(kind);
            _index = _commands.Count - 1;
            TrimToBudget(paint, sculpt);
        }

        internal bool Undo(MeshLayerMaskPainterSession paint, MeshSculptSession sculpt)
        {
            if (!CanUndo)
                return false;

            MeshPainterHistoryKind kind = _commands[_index];
            bool changed = kind == MeshPainterHistoryKind.Paint
                ? paint?.Undo() == true
                : sculpt?.Undo() == true;
            if (changed)
                _index--;
            return changed;
        }

        internal bool Redo(MeshLayerMaskPainterSession paint, MeshSculptSession sculpt)
        {
            if (!CanRedo)
                return false;

            MeshPainterHistoryKind kind = _commands[_index + 1];
            bool changed = kind == MeshPainterHistoryKind.Paint
                ? paint?.Redo() == true
                : sculpt?.Redo() == true;
            if (changed)
                _index++;
            return changed;
        }

        internal void Clear()
        {
            _commands.Clear();
            _index = -1;
        }

        private void TrimToBudget(MeshLayerMaskPainterSession paint, MeshSculptSession sculpt)
        {
            while (_commands.Count > 0 && TotalBytes(paint, sculpt) > BudgetBytes && _index >= 0)
            {
                MeshPainterHistoryKind oldest = _commands[0];
                if (oldest == MeshPainterHistoryKind.Paint)
                    paint?.DropOldestUndoCommand();
                else
                    sculpt?.DropOldestUndoCommand();
                _commands.RemoveAt(0);
                _index--;
            }
        }

        private static long TotalBytes(MeshLayerMaskPainterSession paint, MeshSculptSession sculpt)
        {
            return (paint?.HistoryBytes ?? 0L) + (sculpt?.HistoryBytes ?? 0L);
        }
    }
}
