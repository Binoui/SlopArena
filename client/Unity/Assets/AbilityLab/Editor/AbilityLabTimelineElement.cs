using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Shared;

namespace SlopArena.EditorTools;

public enum TimelineDragMode
{
    Move,
    ResizeHitboxEnd,
}

public readonly struct AbilityLabTimelineDrag
{
    public AbilityLabTimelineDrag(int sourceStageIndex, int sourceOperationIndex, TimelineDragMode mode, int tick, int durationTicks)
    {
        SourceStageIndex = sourceStageIndex;
        SourceOperationIndex = sourceOperationIndex;
        Mode = mode;
        Tick = tick;
        DurationTicks = durationTicks;
    }

    public int SourceStageIndex { get; }
    public int SourceOperationIndex { get; }
    public TimelineDragMode Mode { get; }
    public int Tick { get; }
    public int DurationTicks { get; }
}

public sealed class AbilityLabTimelineElement : VisualElement
{
    private const float AxisHeight = 18f;
    private const float RowHeight = 20f;
    private const float LabelColumnWidth = 90f;
    private const float MinimumHeight = 84f;
    private readonly List<(AbilityLabOperationProjection Operation, Rect Rect)> _hitRects = new();
    private readonly VisualElement _rowLabels = new();
    private AbilityLabTimelineProjection _projection = null!;
    private int _currentTick;
    private AbilityLabOperationProjection? _selectedOperation;
    private int _selectedSourceStageIndex = -1;
    private int _selectedSourceOperationIndex = -1;
    private bool _scrubbing;
    private bool _dragging;
    private int _pointerId = -1;
    private TimelineDragMode _dragMode;
    private AbilityLabOperationProjection? _dragOperation;
    private int _dragStartTick;
    private int _dragStartDuration;
    private int _pendingTick;
    private int _pendingDuration;
    public new class UxmlFactory : UxmlFactory<AbilityLabTimelineElement, UxmlTraits> { }
    public AbilityLabTimelineProjection Projection
    {
        get => _projection;
        set
        {
            if (ReferenceEquals(_projection, value)) return;
            _projection = value;
            UpdateGeometry();
            RebuildRowLabels();
            ResolveSelectedOperation();
            MarkDirtyRepaint();
        }
    }

    public int CurrentTick
    {
        get => _currentTick;
        set { _currentTick = Mathf.Clamp(value, 0, Mathf.Max(0, _projection?.DurationTicks ?? 0)); MarkDirtyRepaint(); }
    }

    public AbilityLabOperationProjection? SelectedOperation
    {
        get => _selectedOperation;
        set
        {
            _selectedOperation = value;
            _selectedSourceStageIndex = value?.SourceStageIndex ?? -1;
            _selectedSourceOperationIndex = value?.SourceOperationIndex ?? -1;
            MarkDirtyRepaint();
        }
    }

    public event Action<AbilityLabOperationProjection>? OperationSelected;
    public event Action<int>? TickScrubbed;
    public event Action<AbilityLabTimelineDrag>? DragCompleted;

    public AbilityLabTimelineElement()
    {
        focusable = true;
        _rowLabels.name = "timeline-row-labels";
        _rowLabels.AddToClassList("timeline-row-labels");
        _rowLabels.pickingMode = PickingMode.Ignore;
        Add(_rowLabels);
        generateVisualContent += GenerateVisualContent;
        RegisterCallback<PointerDownEvent>(OnPointerDown);
        RegisterCallback<PointerMoveEvent>(OnPointerMove);
        RegisterCallback<PointerUpEvent>(OnPointerUp);
        RegisterCallback<PointerLeaveEvent>(_ => tooltip = string.Empty);
        RegisterCallback<PointerCancelEvent>(_ => CancelDrag());
    }

    public void CancelDrag()
    {
        if (!_dragging && !_scrubbing) return;
        ReleasePointerCapture();
        _dragging = false;
        _scrubbing = false;
        _dragOperation = null;
        MarkDirtyRepaint();
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0) return;
        Focus();
        var point = evt.localPosition;
        var operation = FindOperation(point);
        if (operation != null)
        {
            SelectedOperation = operation;
            OperationSelected?.Invoke(operation);
            BeginDrag(operation, point, evt.pointerId);
            evt.StopPropagation();
            return;
        }

        _scrubbing = true;
        _pointerId = evt.pointerId;
        PointerCaptureHelper.CapturePointer(this, _pointerId);
        Scrub(point.x);
        evt.StopPropagation();
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (_dragging && evt.pointerId == _pointerId)
        {
            ProposeDrag(evt.localPosition.x);
            return;
        }
        if (_scrubbing && evt.pointerId == _pointerId)
        {
            Scrub(evt.localPosition.x);
            return;
        }

        var operation = FindOperation(evt.localPosition);
        tooltip = operation == null ? string.Empty : $"{operation.Summary} · [{operation.StartTick}, {operation.EndTick})";
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        if (evt.pointerId != _pointerId || evt.button != 0) return;
        if (_dragging)
        {
            var operation = _dragOperation!;
            bool changed = _dragMode == TimelineDragMode.Move
                ? _pendingTick != _dragStartTick
                : _pendingDuration != _dragStartDuration;
            ReleasePointerCapture();
            _dragging = false;
            _dragOperation = null;
            if (changed)
                DragCompleted?.Invoke(new AbilityLabTimelineDrag(operation.SourceStageIndex, operation.SourceOperationIndex,
                    _dragMode, _pendingTick, _pendingDuration));
            MarkDirtyRepaint();
            evt.StopPropagation();
            return;
        }

        if (_scrubbing)
        {
            ReleasePointerCapture();
            _scrubbing = false;
            evt.StopPropagation();
        }
    }

    private void BeginDrag(AbilityLabOperationProjection operation, Vector2 point, int pointerId)
    {
        _dragOperation = operation;
        _dragStartTick = operation.Source.Tick;
        _dragStartDuration = operation.Source is SpawnHitboxOperationSource hitbox ? hitbox.Hitbox.DurationTicks : 0;
        _pendingTick = _dragStartTick;
        _pendingDuration = _dragStartDuration;
        _dragMode = IsHitboxEndHandle(operation, point.x) ? TimelineDragMode.ResizeHitboxEnd : TimelineDragMode.Move;
        _dragging = true;
        _pointerId = pointerId;
        PointerCaptureHelper.CapturePointer(this, _pointerId);
    }

    private void ProposeDrag(float x)
    {
        float plotWidth = PlotWidth;
        if (!_dragging || _dragOperation == null || _projection == null || _projection.DurationTicks <= 0 || plotWidth <= 0) return;
        if (_dragOperation.SourceStageIndex < 0 || _dragOperation.SourceStageIndex >= _projection.Stages.Count) return;
        var stage = _projection.Stages[_dragOperation.SourceStageIndex];
        int cumulativeTick = AbilityLabTimelineProjection.SnapTick(Mathf.Clamp01((x - LabelColumnWidth) / plotWidth), _projection.DurationTicks);
        int localTick = cumulativeTick - stage.StartTick;
        if (_dragMode == TimelineDragMode.ResizeHitboxEnd)
        {
            int endTick = Mathf.Clamp(localTick, 0, stage.DurationTicks);
            _pendingDuration = AbilityLabTimelineProjection.ClampHitboxDuration(_dragStartTick, endTick - _dragStartTick, stage.DurationTicks);
        }
        else
        {
            int maxTick = stage.DurationTicks - 1;
            if (_dragOperation.Source is SpawnHitboxOperationSource)
                maxTick = stage.DurationTicks - _dragStartDuration;
            _pendingTick = Mathf.Clamp(localTick, 0, Mathf.Max(0, maxTick));
        }
        MarkDirtyRepaint();
    }

    private void Scrub(float x)
    {
        float plotWidth = PlotWidth;
        if (_projection == null || _projection.DurationTicks <= 0 || plotWidth <= 0) return;
        int tick = AbilityLabTimelineProjection.SnapTick(Mathf.Clamp01((x - LabelColumnWidth) / plotWidth), _projection.DurationTicks);
        CurrentTick = tick;
        TickScrubbed?.Invoke(tick);
    }

    private AbilityLabOperationProjection? FindOperation(Vector2 point)
    {
        for (int i = 0; i < _hitRects.Count; i++)
            if (_hitRects[i].Rect.Contains(point)) return _hitRects[i].Operation;
        return null;
    }
    private float PlotWidth => Mathf.Max(0f, contentRect.width - LabelColumnWidth);

    private bool IsHitboxEndHandle(AbilityLabOperationProjection operation, float x)
    {
        if (operation.Source is not SpawnHitboxOperationSource) return false;
        float endX = LabelColumnWidth + Mathf.Clamp01(operation.EndTick / (float)_projection.DurationTicks) * PlotWidth;
        return Mathf.Abs(x - endX) <= 7f;
    }
    private void RebuildRowLabels()
    {
        _rowLabels.Clear();
        if (_projection?.Stages == null) return;
        var counts = new Dictionary<CookedOperationKind, int>();
        foreach (var stage in _projection.Stages)
        foreach (var operation in stage.Operations)
            counts[operation.Kind] = counts.TryGetValue(operation.Kind, out int count) ? count + 1 : 1;

        var occurrences = new Dictionary<CookedOperationKind, int>();
        int row = 0;
        foreach (var stage in _projection.Stages)
        foreach (var operation in stage.Operations)
        {
            int occurrence = occurrences.TryGetValue(operation.Kind, out int count) ? count + 1 : 1;
            occurrences[operation.Kind] = occurrence;
            string name = operation.Kind switch
            {
                CookedOperationKind.SpawnHitbox => "Hitbox",
                CookedOperationKind.SpawnProjectile => "Projectile",
                CookedOperationKind.EmitPresentation => "Presentation",
                CookedOperationKind.StartCapability => "Capability",
                CookedOperationKind.SetVelocity => "Velocity",
                CookedOperationKind.SetAimState => "Aim",
                CookedOperationKind.CompleteTimeline => "Complete",
                _ => operation.Summary,
            };
            if ((operation.Kind == CookedOperationKind.SpawnHitbox || operation.Kind == CookedOperationKind.SpawnProjectile) &&
                counts[operation.Kind] > 1)
                name += $" {occurrence}";
            var label = new Label(name);
            label.AddToClassList("timeline-row-label");
            label.pickingMode = PickingMode.Ignore;
            label.style.top = AxisHeight + row * RowHeight;
            _rowLabels.Add(label);
            row++;
        }
    }


    private void ResolveSelectedOperation()
    {
        _selectedOperation = null;
        if (_selectedSourceStageIndex < 0 || _selectedSourceOperationIndex < 0 || _projection == null) return;
        if (_selectedSourceStageIndex >= _projection.Stages.Count) return;
        foreach (var operation in _projection.Stages[_selectedSourceStageIndex].Operations)
            if (operation.SourceOperationIndex == _selectedSourceOperationIndex)
            {
                _selectedOperation = operation;
                return;
            }
    }

    private void ReleasePointerCapture()
    {
        if (_pointerId >= 0 && PointerCaptureHelper.HasPointerCapture(this, _pointerId)) PointerCaptureHelper.ReleasePointer(this, _pointerId);
        _pointerId = -1;
    }
    private void UpdateGeometry()
    {
        int operationCount = 0;
        if (_projection?.Stages != null)
            foreach (var stage in _projection.Stages)
                operationCount += stage.Operations?.Count ?? 0;
        style.height = Mathf.Max(MinimumHeight, AxisHeight + operationCount * RowHeight);
    }

    private void GenerateVisualContent(MeshGenerationContext context)
    {
        _hitRects.Clear();
        float width = PlotWidth;
        if (_projection == null || _projection.DurationTicks <= 0 || width <= 0) return;
        var painter = context.painter2D;
        float axisHeight = AxisHeight;
        float rowHeight = RowHeight;
        painter.strokeColor = new Color(0.45f, 0.45f, 0.5f);
        painter.lineWidth = 1f;
        for (int i = 0; i < _projection.Stages.Count; i++)
        {
            var stage = _projection.Stages[i];
            float x = LabelColumnWidth + stage.StartTick / (float)_projection.DurationTicks * width;
            DrawLine(painter, new Vector2(x, 0), new Vector2(x, contentRect.height), new Color(0.5f, 0.5f, 0.55f));
            if (i == _projection.Stages.Count - 1)
            {
                float endX = LabelColumnWidth + stage.EndTick / (float)_projection.DurationTicks * width;
                DrawLine(painter, new Vector2(endX, 0), new Vector2(endX, contentRect.height), new Color(0.5f, 0.5f, 0.55f));
            }
        }
        float currentX = LabelColumnWidth + _currentTick / (float)_projection.DurationTicks * width;
        DrawLine(painter, new Vector2(currentX, 0), new Vector2(currentX, contentRect.height), new Color(1f, 1f, 0.35f));

        int row = 0;
        foreach (var stage in _projection.Stages)
        foreach (var operation in stage.Operations)
        {
            int tick = operation.Source.Tick;
            int duration = operation.Source is SpawnHitboxOperationSource hitbox ? hitbox.Hitbox.DurationTicks : 0;
            if (_dragging && _dragOperation != null && operation.SourceStageIndex == _dragOperation.SourceStageIndex && operation.SourceOperationIndex == _dragOperation.SourceOperationIndex)
            {
                tick = _pendingTick;
                duration = _pendingDuration;
            }
            float start = LabelColumnWidth + Mathf.Clamp01((stage.StartTick + tick) / (float)_projection.DurationTicks) * width;
            float end = operation.Source is SpawnHitboxOperationSource
                ? LabelColumnWidth + Mathf.Clamp01((stage.StartTick + tick + duration) / (float)_projection.DurationTicks) * width
                : start;
            float y = axisHeight + row * rowHeight;
            var renderedRect = operation.Kind == CookedOperationKind.SpawnHitbox
                ? new Rect(start, y + 4f, Mathf.Max(2f, end - start), 10f)
                : new Rect(start - 4f, y + 5f, 8f, 8f);
            _hitRects.Add((operation, operation.Kind == CookedOperationKind.SpawnHitbox
                ? new Rect(renderedRect.xMin - 2f, renderedRect.yMin - 2f, renderedRect.width + 4f, renderedRect.height + 4f)
                : renderedRect));
            var color = operation.Kind == CookedOperationKind.SpawnHitbox
                ? new Color(1f, 0.45f, 0.12f)
                : new Color(0.35f, 0.7f, 1f);
            if (_selectedOperation is { } selected && selected.SourceStageIndex == operation.SourceStageIndex && selected.SourceOperationIndex == operation.SourceOperationIndex)
                color = Color.yellow;
            if (operation.Kind == CookedOperationKind.SpawnHitbox)
                DrawRect(painter, new Rect(start, y + 4f, Mathf.Max(2f, end - start), 10f), color);
            else
                DrawMarker(painter, new Vector2(start, y + 9f), color);
            row++;
        }
    }

    private static void DrawLine(Painter2D painter, Vector2 from, Vector2 to, Color color)
    {
        painter.strokeColor = color;
        painter.BeginPath(); painter.MoveTo(from); painter.LineTo(to); painter.Stroke();
    }

    private static void DrawRect(Painter2D painter, Rect rect, Color color)
    {
        painter.fillColor = color;
        painter.BeginPath();
        painter.MoveTo(rect.min); painter.LineTo(new Vector2(rect.xMax, rect.yMin));
        painter.LineTo(rect.max); painter.LineTo(new Vector2(rect.xMin, rect.yMax));
        painter.ClosePath(); painter.Fill();
    }

    private static void DrawMarker(Painter2D painter, Vector2 center, Color color)
    {
        const float radius = 4f;
        painter.fillColor = color;
        painter.BeginPath();
        for (int i = 0; i <= 8; i++)
        {
            float angle = i / 8f * Mathf.PI * 2f;
            var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            if (i == 0) painter.MoveTo(point); else painter.LineTo(point);
        }
        painter.ClosePath(); painter.Fill();
    }
}
