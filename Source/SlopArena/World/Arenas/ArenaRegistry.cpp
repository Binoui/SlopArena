// Copyright SlopArena Contributors. MIT License.

#include "ArenaRegistry.h"

// Helper lambdas
namespace {
	FPlatformData MakeFloor(float X, float Y, float Z, float SizeX, float SizeY, bool Cover = false)
	{
		FPlatformData P;
		P.Center = FVector(X, Y, Z);
		P.Size = FVector2D(SizeX, SizeY);
		P.bIsMainFloor = true;
		P.bHasCover = Cover;
		return P;
	}

	FPlatformData MakePlatform(float X, float Y, float Z, float SizeX, float SizeY)
	{
		FPlatformData P;
		P.Center = FVector(X, Y, Z);
		P.Size = FVector2D(SizeX, SizeY);
		P.bIsMainFloor = false;
		return P;
	}

	FCoverData MakePillar(float X, float Y, float W, float D, float H)
	{
		FCoverData C;
		C.Center = FVector(X, Y, 0);
		C.Size = FVector2D(W, D);
		C.Height = H;
		return C;
	}

	FSpawnPointData MakeSpawn(float X, float Y, float Yaw)
	{
		FSpawnPointData S;
		S.Location = FVector(X, Y, 0);
		S.Yaw = Yaw;
		return S;
	}

	void AddRingOutSpawns(TArray<FSpawnPointData>& Spawns, float Radius)
	{
		// 4 spawns at compass points
		Spawns.Add(MakeSpawn(-Radius * 0.5f, 0.0f, 0.0f));       // West facing East
		Spawns.Add(MakeSpawn( Radius * 0.5f, 0.0f, 180.0f));      // East facing West
		Spawns.Add(MakeSpawn(0.0f, -Radius * 0.5f, 90.0f));      // South facing North (UE Y = North)
		Spawns.Add(MakeSpawn(0.0f,  Radius * 0.5f, -90.0f));     // North facing South
	}
}

// ===================================================================
// SANCTUARY — Classic DKO arena
// A circular main platform with two side platforms.
// ===================================================================
FArenaDefinition FArenaRegistry::CreateSanctuary()
{
	FArenaDefinition A;
	A.ArenaName = NSLOCTEXT("SlopArena", "Sanctuary", "Sanctuary");
	A.Description = NSLOCTEXT("SlopArena", "SanctuaryDesc", "A classic floating colosseum. Simple, balanced, and deadly for ring-outs.");
	A.VoidPlaneZ = -5000.0f;
	A.CeilingHeight = 4000.0f;

	// Main floor — circular (approximated as large square with rounded feel)
	A.Platforms.Add(MakeFloor(0.0f, 0.0f, 0.0f, 3500.0f, 3500.0f));

	// Two side platforms — perfect for escaping pressure or edge-guarding
	A.Platforms.Add(MakePlatform(-2000.0f, 0.0f, 500.0f, 1200.0f, 800.0f));  // Left high
	A.Platforms.Add(MakePlatform( 2000.0f, 0.0f, 500.0f, 1200.0f, 800.0f));  // Right high

	// Two low side platforms for recovery
	A.Platforms.Add(MakePlatform(-1500.0f, 0.0f, -300.0f, 800.0f, 600.0f));  // Left low
	A.Platforms.Add(MakePlatform( 1500.0f, 0.0f, -300.0f, 800.0f, 600.0f));  // Right low

	// Spawn points
	AddRingOutSpawns(A.SpawnPoints, 1200.0f);

	return A;
}

// ===================================================================
// SKY TEMPLE — Multi-tier platforms
// Inspired by DKO's temple stages: staggered heights, strategic positioning.
// ===================================================================
FArenaDefinition FArenaRegistry::CreateSkyTemple()
{
	FArenaDefinition A;
	A.ArenaName = NSLOCTEXT("SlopArena", "SkyTemple", "Sky Temple");
	A.Description = NSLOCTEXT("SlopArena", "SkyTempleDesc", "Staggered platforms at multiple heights. Vertical play and juggling reign here.");
	A.VoidPlaneZ = -6000.0f;
	A.CeilingHeight = 5000.0f;

	// Main floor
	A.Platforms.Add(MakeFloor(0.0f, 0.0f, 0.0f, 3000.0f, 3000.0f));

	// Center high platform — for the brave
	A.Platforms.Add(MakePlatform(0.0f, 0.0f, 700.0f, 1000.0f, 1000.0f));

	// Four corner platforms at medium height
	float R = 1800.0f;
	float H = 400.0f;
	A.Platforms.Add(MakePlatform(-R, -R, H, 800.0f, 800.0f));
	A.Platforms.Add(MakePlatform( R, -R, H, 800.0f, 800.0f));
	A.Platforms.Add(MakePlatform(-R,  R, H, 800.0f, 800.0f));
	A.Platforms.Add(MakePlatform( R,  R, H, 800.0f, 800.0f));

	// Low outer platforms — recovery options
	float RL = 2400.0f;
	A.Platforms.Add(MakePlatform(-RL, 0.0f, -400.0f, 600.0f, 600.0f));
	A.Platforms.Add(MakePlatform( RL, 0.0f, -400.0f, 600.0f, 600.0f));
	A.Platforms.Add(MakePlatform(0.0f, -RL, -400.0f, 600.0f, 600.0f));
	A.Platforms.Add(MakePlatform(0.0f,  RL, -400.0f, 600.0f, 600.0f));

	AddRingOutSpawns(A.SpawnPoints, 1000.0f);

	return A;
}

// ===================================================================
// PILLAR PIT — Central pillar for cover
// DKO had stages with large central pillars for line-of-sight breaking.
// ===================================================================
FArenaDefinition FArenaRegistry::CreatePillarPit()
{
	FArenaDefinition A;
	A.ArenaName = NSLOCTEXT("SlopArena", "PillarPit", "Pillar Pit");
	A.Description = NSLOCTEXT("SlopArena", "PillarPitDesc", "A massive central pillar splits the arena. Use it for cover, wall-jumps, and surprise attacks.");
	A.VoidPlaneZ = -5000.0f;
	A.CeilingHeight = 4500.0f;

	// Main floor with space around pillar
	A.Platforms.Add(MakeFloor(0.0f, 0.0f, 0.0f, 4000.0f, 4000.0f, true));

	// Central pillar
	A.CoverObjects.Add(MakePillar(0.0f, 0.0f, 400.0f, 400.0f, 3000.0f));

	// Two side platforms at the edges
	A.Platforms.Add(MakePlatform(-2400.0f, 0.0f, 300.0f, 1000.0f, 800.0f));
	A.Platforms.Add(MakePlatform( 2400.0f, 0.0f, 300.0f, 1000.0f, 800.0f));

	// Low platforms for recovery below the main floor
	A.Platforms.Add(MakePlatform(-1200.0f, -1200.0f, -500.0f, 600.0f, 600.0f));
	A.Platforms.Add(MakePlatform( 1200.0f,  1200.0f, -500.0f, 600.0f, 600.0f));

	AddRingOutSpawns(A.SpawnPoints, 1400.0f);

	return A;
}

// ===================================================================
// LAVA FOUNDRY — Hazard edges
// Smaller arena with no safe corners. Forces engagement.
// ===================================================================
FArenaDefinition FArenaRegistry::CreateLavaFoundry()
{
	FArenaDefinition A;
	A.ArenaName = NSLOCTEXT("SlopArena", "LavaFoundry", "Lava Foundry");
	A.Description = NSLOCTEXT("SlopArena", "LavaFoundryDesc", "A compact arena with a small safe zone. The edges are lava — every hit is a potential ring-out.");
	A.VoidPlaneZ = -3000.0f;
	A.CeilingHeight = 3000.0f;

	// Small main floor — tight, action is constant
	A.Platforms.Add(MakeFloor(0.0f, 0.0f, 0.0f, 2500.0f, 2500.0f));

	// Center pillar — limited cover
	A.CoverObjects.Add(MakePillar(0.0f, 0.0f, 300.0f, 300.0f, 2000.0f));

	// Four tiny platforms near the edges — high risk, high reward
	float R = 1600.0f;
	A.Platforms.Add(MakePlatform(-R, -R, -200.0f, 400.0f, 400.0f));
	A.Platforms.Add(MakePlatform( R, -R, -200.0f, 400.0f, 400.0f));
	A.Platforms.Add(MakePlatform(-R,  R, -200.0f, 400.0f, 400.0f));
	A.Platforms.Add(MakePlatform( R,  R, -200.0f, 400.0f, 400.0f));

	// Spawn in the center-ish
	A.SpawnPoints.Add(MakeSpawn(-600.0f, 0.0f, 0.0f));
	A.SpawnPoints.Add(MakeSpawn( 600.0f, 0.0f, 180.0f));

	return A;
}

// ===================================================================
// Factory
// ===================================================================

TArray<FArenaDefinition> FArenaRegistry::GetAll()
{
	TArray<FArenaDefinition> Arenas;
	Arenas.Add(CreateSanctuary());
	Arenas.Add(CreateSkyTemple());
	Arenas.Add(CreatePillarPit());
	Arenas.Add(CreateLavaFoundry());
	return Arenas;
}
