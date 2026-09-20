using System;
using System.Collections.Generic;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Tools;

/// <summary>Runs one isolated package move through the Shared authoritative simulation.</summary>
public sealed class AbilityLabSimulationController
{
    private const ulong PreviewEntityId = 1;
    private ServerSimulation _simulation;
    private readonly List<TimelinePresentationEvent> _events = new();

    public IReadOnlyList<TimelinePresentationEvent> PresentationEvents => _events;
    public bool IsReady => _simulation != null;

    public void Simulate(
        CharacterDefinition definition,
        BakedAnimationData? baked,
        byte wireSlot,
        bool airborne,
        Vector3 origin,
        float facingYaw,
        int targetTick)
    {
        _events.Clear();
        _simulation = null;
        if (definition == null || targetTick < 0) return;

        float floorY = origin.y - definition.CapsuleHeight * 0.5f;
        var simulation = new ServerSimulation(CreateArena(floorY))
        {
            NoCooldownsEntityId = PreviewEntityId,
        };
        simulation.SetTick(0);
        var state = new CharacterState
        {
            EntityId = PreviewEntityId,
            PX = origin.x,
            PY = origin.y,
            PZ = origin.z,
            FacingYaw = facingYaw,
            AimYaw = facingYaw,
            IsGrounded = !airborne,
            State = ActionState.Idle,
        };
        simulation.RegisterEntity(PreviewEntityId, definition, state, baked);
        var inputs = new Dictionary<ulong, InputState>(1);
        for (int tick = 0; tick <= targetTick; tick++)
        {
            inputs[PreviewEntityId] = tick == 0
                ? new InputState
                {
                    ActiveSlot = wireSlot,
                    FacingYaw = ToDegreesHundredths(facingYaw),
                    AimYaw = ToDegreesHundredths(facingYaw),
                }
                : default;
            simulation.Tick(inputs);
            _events.AddRange(simulation.GetPresentationEvents(clear: true));
        }
        _simulation = simulation;
    }

    public void Clear()
    {
        _events.Clear();
        _simulation = null;
    }

    private static short ToDegreesHundredths(float radians)
    {
        int degrees = Mathf.RoundToInt(radians * Mathf.Rad2Deg * 100f);
        return (short)Mathf.Clamp(degrees, short.MinValue, short.MaxValue);
    }

    private static ArenaDefinition CreateArena(float floorY)
    {
        const int width = 100;
        const int height = 100;
        var heightmap = new float[width * height];
        for (int i = 0; i < heightmap.Length; i++) heightmap[i] = floorY;
        return new ArenaDefinition
        {
            Name = "ability-lab",
            DisplayName = "Ability Lab",
            KillHeight = floorY - 20f,
            SpawnPoints = new[] { new SpawnPoint { X = 0f, Y = floorY, Z = 0f, Yaw = 0f } },
            Heightmap = new ArenaHeightmap
            {
                Data = heightmap,
                Width = width,
                Height = height,
                CellSize = 1f,
                OriginX = 0f,
                OriginZ = 0f,
            },
        };
    }
}
