using System;

namespace SlopArena.Shared
{
	[Serializable]
	public struct SpawnPoint
	{
		public float X, Y, Z;
		public float Yaw; // facing direction in radians
	}

	/// <summary>
	/// Pure data definition of an arena. No Godot types.
	/// Used by both client (ArenaManager) and server (for bounds/spawning).
	/// </summary>
	[Serializable]
	public struct ArenaDefinition
	{
		public string Name;           // machine-readable key
		public string DisplayName;    // human-readable
		public string ScenePath;      // res://assets/arenas/xxx.tscn
		public float VoidHeight;      // Y below this = out of bounds (death/respawn)
		public float KillHeight;      // Y below this = instant kill (no save)
		public float MinX, MaxX;      // arena bounds X
		public float MinZ, MaxZ;      // arena bounds Z
		public SpawnPoint[] SpawnPoints;
	}

	public static class ArenaRegistry
	{
		public static ArenaDefinition[] All { get; } = BuildAll();

		public static ArenaDefinition Get(string name)
		{
			foreach (var a in All)
				if (a.Name == name) return a;
			return All[0];
		}

		private static ArenaDefinition[] BuildAll()
		{
			return new[]
			{
				new ArenaDefinition
				{
					Name = "pit",
					DisplayName = "The Pit",
					ScenePath = "res://assets/arenas/arena_pit.tscn",
					VoidHeight = -2f,
					KillHeight = -10f,
					MinX = 0f, MaxX = 80f,
					MinZ = 0f, MaxZ = 80f,
					SpawnPoints = new[]
					{
						new SpawnPoint { X = 10f, Y = 0.5f, Z = 10f, Yaw = 0f },
						new SpawnPoint { X = 70f, Y = 0.5f, Z = 10f, Yaw = MathF.PI },
						new SpawnPoint { X = 10f, Y = 0.5f, Z = 70f, Yaw = 0f },
						new SpawnPoint { X = 70f, Y = 0.5f, Z = 70f, Yaw = MathF.PI },
						new SpawnPoint { X = 40f, Y = 0.5f, Z = 15f, Yaw = 0f },
						new SpawnPoint { X = 40f, Y = 0.5f, Z = 65f, Yaw = MathF.PI },
					}
				},
				new ArenaDefinition
				{
					Name = "cross",
					DisplayName = "Crossroads",
					ScenePath = "res://assets/arenas/arena_cross.tscn",
					VoidHeight = -2f,
					KillHeight = -10f,
					MinX = 0f, MaxX = 60f,
					MinZ = 0f, MaxZ = 60f,
					SpawnPoints = new[]
					{
						new SpawnPoint { X = 30f, Y = 0.5f, Z = 5f, Yaw = MathF.PI },
						new SpawnPoint { X = 30f, Y = 0.5f, Z = 55f, Yaw = 0f },
						new SpawnPoint { X = 5f, Y = 0.5f, Z = 30f, Yaw = MathF.PI / 2f },
						new SpawnPoint { X = 55f, Y = 0.5f, Z = 30f, Yaw = -MathF.PI / 2f },
						new SpawnPoint { X = 30f, Y = 0.5f, Z = 30f, Yaw = 0f },
						new SpawnPoint { X = 22f, Y = 0.5f, Z = 22f, Yaw = 0f },
					}
				},
				new ArenaDefinition
				{
					Name = "split",
					DisplayName = "The Split",
					ScenePath = "res://assets/arenas/arena_split.tscn",
					VoidHeight = -3f,
					KillHeight = -10f,
					MinX = 0f, MaxX = 60f,
					MinZ = 0f, MaxZ = 60f,
					SpawnPoints = new[]
					{
						new SpawnPoint { X = 15f, Y = 0.5f, Z = 15f, Yaw = 0f },
						new SpawnPoint { X = 45f, Y = 0.5f, Z = 15f, Yaw = MathF.PI },
						new SpawnPoint { X = 15f, Y = 0.5f, Z = 45f, Yaw = 0f },
						new SpawnPoint { X = 45f, Y = 0.5f, Z = 45f, Yaw = MathF.PI },
						new SpawnPoint { X = 30f, Y = 2.5f, Z = 30f, Yaw = 0f },
						new SpawnPoint { X = 22f, Y = 0.5f, Z = 30f, Yaw = 0f },
					}
				},
			};
		}
	}
}
