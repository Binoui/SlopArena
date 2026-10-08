using System;
using System.Linq;
using SlopArena.Shared;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools;

public sealed partial class AbilityLabWindow
{
    private void AddChargedDashInspector(Foldout group, AbilityLabOperationProjection operation,
        StartCapabilityOperationSource source, ChargedDirectionalDashCapabilityParameters value)
    {
        AddDelayedInteger(group, "Start tick", source.Tick, tick => CommitCapabilityStart(operation, tick));
        AddDelayedInteger(group, "Maximum charge ticks", value.MaxChargeTicks,
            tick => CommitChargedDash(operation, p => p with { MaxChargeTicks = UShort(tick) }));
        AddDelayedInteger(group, "Tier 2 threshold ticks", value.Tier2Ticks,
            tick => CommitChargedDash(operation, p => p with { Tier2Ticks = UShort(tick) }));
        AddDelayedInteger(group, "Tier 3 threshold ticks", value.Tier3Ticks,
            tick => CommitChargedDash(operation, p => p with { Tier3Ticks = UShort(tick) }));
        AddDelayedFloat(group, "Minimum dash distance (m)", value.MinDistance,
            distance => CommitChargedDash(operation, p => p with { MinDistance = distance }));
        AddDelayedFloat(group, "Maximum dash distance (m)", value.MaxDistance,
            distance => CommitChargedDash(operation, p => p with { MaxDistance = distance }));
        AddDelayedFloat(group, "Dash speed (m/s)", value.DashSpeed,
            speed => CommitChargedDash(operation, p => p with { DashSpeed = speed }));
        AddDelayedInteger(group, "Finisher lead ticks", value.FinisherLeadTicks,
            ticks => CommitChargedDash(operation, p => p with { FinisherLeadTicks = UShort(ticks) }));
        AddDelayedInteger(group, "Finisher pose seek tick", value.FinisherSeekTick,
            tick => CommitChargedDash(operation, p => p with { FinisherSeekTick = UShort(tick) }));
        AddDelayedInteger(group, "Recovery ticks", value.RecoveryTicks,
            ticks => CommitChargedDash(operation, p => p with { RecoveryTicks = UShort(ticks) }));
        AddDelayedFloat(group, "Tier 2 finisher damage", value.Tier2Damage,
            damage => CommitChargedDash(operation, p => p with { Tier2Damage = damage }));
        AddDelayedFloat(group, "Tier 3 finisher damage", value.Tier3Damage,
            damage => CommitChargedDash(operation, p => p with { Tier3Damage = damage }));
        AddChargedHitbox(group, operation, "Traversal hitbox", false, value.TraversalHitbox);
        AddChargedHitbox(group, operation, "Finisher hitbox", true, value.FinisherHitbox);
    }

    private static ushort UShort(int value) => (ushort)Mathf.Clamp(value, 0, ushort.MaxValue);

    private void AddChargedHitbox(Foldout parent, AbilityLabOperationProjection operation,
        string label, bool finisher, HitboxSource value)
    {
        var group = new Foldout { text = label, value = false };
        var timing = new Foldout { text = "Timing & behavior", value = false };
        if (finisher)
            AddDelayedInteger(timing, "Active duration ticks", value.DurationTicks,
                v => CommitChargedHitbox(operation, finisher, h => h with { DurationTicks = UShort(v) }));
        else
            timing.Add(new Label("Active for the computed dash travel duration."));
        AddDelayedInteger(timing, "Hitstun gate (0 = off)", value.StunTicks,
            v => CommitChargedHitbox(operation, finisher, h => h with { StunTicks = UShort(v) }));
        AddDelayedInteger(timing, "Fixed hitstun ticks (0 = automatic)", value.FixedHitstunTicks,
            v => CommitChargedHitbox(operation, finisher, h => h with { FixedHitstunTicks = UShort(v) }));
        AddToggle(timing, "Interruptible", value.Interruptible,
            v => CommitChargedHitbox(operation, finisher, h => h with { Interruptible = v }));
        timing.Add(new Label("Traversal and finisher keep independent per-opponent hit histories."));
        group.Add(timing);

        var combat = new Foldout { text = "Combat", value = false };
        AddDelayedFloat(combat, "Damage", value.Damage, v => CommitChargedHitbox(operation, finisher, h => h with { Damage = v }));
        AddDelayedFloat(combat, "Angle", value.Angle, v => CommitChargedHitbox(operation, finisher, h => h with { Angle = v }));
        AddDelayedFloat(combat, "Base knockback", value.BaseKnockback, v => CommitChargedHitbox(operation, finisher, h => h with { BaseKnockback = v }));
        AddDelayedFloat(combat, "Knockback growth", value.KnockbackGrowth, v => CommitChargedHitbox(operation, finisher, h => h with { KnockbackGrowth = v }));
        var direction = new EnumField("Knockback direction", value.KnockbackDirection);
        direction.RegisterValueChangedCallback(evt => CommitChargedHitbox(operation, finisher,
            h => h with { KnockbackDirection = (AuthoringKnockbackDirection)evt.newValue }));
        combat.Add(direction);
        group.Add(combat);

        var shape = new Foldout { text = "Geometry", value = false };
        var shapeField = new EnumField("Shape", value.Shape);
        shapeField.RegisterValueChangedCallback(evt => CommitChargedHitbox(operation, finisher,
            h => h with { Shape = (AuthoringHitboxShape)evt.newValue }));
        shape.Add(shapeField);
        AddDelayedFloat(shape, "Radius", value.Radius, v => CommitChargedHitbox(operation, finisher, h => h with { Radius = v }));
        AddDelayedFloat(shape, "Offset X", value.OffsetX, v => CommitChargedHitbox(operation, finisher, h => h with { OffsetX = v }));
        AddDelayedFloat(shape, "Offset Y", value.OffsetY, v => CommitChargedHitbox(operation, finisher, h => h with { OffsetY = v }));
        AddDelayedFloat(shape, "Offset Z", value.OffsetZ, v => CommitChargedHitbox(operation, finisher, h => h with { OffsetZ = v }));
        AddDelayedFloat(shape, "End offset X", value.EndOffsetX, v => CommitChargedHitbox(operation, finisher, h => h with { EndOffsetX = v }));
        AddDelayedFloat(shape, "End offset Y", value.EndOffsetY, v => CommitChargedHitbox(operation, finisher, h => h with { EndOffsetY = v }));
        AddDelayedFloat(shape, "End offset Z", value.EndOffsetZ, v => CommitChargedHitbox(operation, finisher, h => h with { EndOffsetZ = v }));
        group.Add(shape);
        AddBonePopup(group, "Start bone", value.StartBoneId,
            id => CommitChargedHitbox(operation, finisher, h => h with { StartBoneId = id }));
        AddBonePopup(group, "End bone", value.EndBoneId,
            id => CommitChargedHitbox(operation, finisher, h => h with { EndBoneId = id }));
        parent.Add(group);
    }

    private void CommitChargedDash(AbilityLabOperationProjection selected,
        Func<ChargedDirectionalDashCapabilityParameters, ChargedDirectionalDashCapabilityParameters> edit)
    {
        if (_updatingControls || _lab == null || selected.Source is not StartCapabilityOperationSource original ||
            original.Parameters is not ChargedDirectionalDashCapabilityParameters parameters ||
            !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out int slotIndex, out _)) return;
        CommitChargedDashCore(selected, slotIndex, original, edit(parameters));
    }

    private void CommitChargedDashCore(AbilityLabOperationProjection selected, int slotIndex,
        StartCapabilityOperationSource original, ChargedDirectionalDashCapabilityParameters parameters)
    {
        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplaceOperation(slotIndex, selected.SourceStageIndex, selected.SourceOperationIndex,
                original with { Parameters = parameters });
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(selected.SourceStageIndex, selected.SourceOperationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private void CommitChargedHitbox(AbilityLabOperationProjection selected, bool finisher,
        Func<HitboxSource, HitboxSource> edit)
    {
        if (_updatingControls || _lab == null || selected.Source is not StartCapabilityOperationSource original ||
            original.Parameters is not ChargedDirectionalDashCapabilityParameters parameters ||
            !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out int slotIndex, out _)) return;
        var updated = finisher
            ? parameters with { FinisherHitbox = edit(parameters.FinisherHitbox) }
            : parameters with { TraversalHitbox = edit(parameters.TraversalHitbox) };
        CommitChargedDashCore(selected, slotIndex, original, updated);
    }

    private void AddChargedDirectionalDashButton(VisualElement parent, int stageIndex,
        CharacterSlotSource slot)
    {
        var add = new Button(() =>
        {
            if (_lab == null || !_workspace.AddChargedDirectionalDash(_lab.SelectedSlotId, stageIndex)) return;
            UpdateTimelineControls();
            _selectedOperation = _timelineProjection?.Stages[stageIndex].Operations.LastOrDefault();
            CacheSelectedHitbox();
            SyncSelectedHitbox();
            _timelineTrack.SelectedOperation = _selectedOperation;
            RefreshInspector();
            SceneView.RepaintAll();
        }) { name = "add-charged-directional-dash", text = "Add charged directional dash" };
        add.tooltip = "Hold to charge, then dash in the locked aim direction with tiered finisher damage.";
        add.SetEnabled(!slot.Timeline.Stages.SelectMany(phase => phase.Operations)
            .OfType<StartCapabilityOperationSource>()
            .Any(op => op.CapabilityId == CharacterPackageCompiler.ChargedDirectionalDashCapabilityId));
        parent.Add(add);
    }
}
