using System;
using System.Buffers.Binary;
using System.Text;

namespace SlopArena.Shared
{
    /// <summary>
    /// Binary serialization for ArenaDefinition.
    /// Pure C# — no Godot dependency. Used by both client and server.
    ///
    /// Format:
    ///   Magic:  "AREN" (0x4E455241)
    ///   Version: uint32 (currently 1)
    ///   Name:        uint32 len + UTF8 bytes
    ///   DisplayName: uint32 len + UTF8 bytes
    ///   ScenePath:   uint32 len + UTF8 bytes
    ///   KillHeight:  float32
    ///   FloorHeight: float32
    ///   PlatformCount: uint32
    ///     For each: CenterX, CenterZ, HalfSizeX, HalfSizeZ, SurfaceY (5 × float32)
    ///   MinX, MaxX, MinZ, MaxZ: 4 × float32
    ///   SpawnCount: uint32
    ///     For each: X, Y, Z, Yaw (4 × float32)
    /// </summary>
    public static class ArenaBinaryFormat
    {
        private const uint Magic = 0x4E455241; // "AREN"
        private const uint Version = 1;

        /// <summary>
        /// Serialize an ArenaDefinition to a byte array.
        /// </summary>
        public static byte[] Serialize(ArenaDefinition arena)
        {
            // Calculate size upfront
            int size = 4 + 4; // magic + version
            size += 4 + Encoding.UTF8.GetByteCount(arena.Name ?? "");
            size += 4 + Encoding.UTF8.GetByteCount(arena.DisplayName ?? "");
            size += 4 + Encoding.UTF8.GetByteCount(arena.ScenePath ?? "");

            size += 4; // KillHeight (float)
            size += 4; // FloorHeight (float)

            // Platforms
            int platCount = arena.Platforms?.Length ?? 0;
            size += 4; // count
            size += platCount * 5 * 4; // 5 floats each

            // Bounds
            size += 4 * 4; // MinX, MaxX, MinZ, MaxZ

            // Spawns
            int spawnCount = arena.SpawnPoints?.Length ?? 0;
            size += 4; // count
            size += spawnCount * 4 * 4; // 4 floats each

            var buffer = new byte[size];
            int pos = 0;

            // Magic + Version
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(pos), Magic); pos += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(pos), Version); pos += 4;

            // Name
            WriteString(buffer, ref pos, arena.Name ?? "");

            // DisplayName
            WriteString(buffer, ref pos, arena.DisplayName ?? "");

            // ScenePath
            WriteString(buffer, ref pos, arena.ScenePath ?? "");

            // KillHeight
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(pos), arena.KillHeight); pos += 4;

            // FloorHeight
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(pos), arena.FloorHeight); pos += 4;

            // Platforms
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), platCount); pos += 4;
            for (int i = 0; i < platCount; i++)
            {
                var p = arena.Platforms![i];
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(pos), p.CenterX); pos += 4;
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(pos), p.CenterZ); pos += 4;
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(pos), p.HalfSizeX); pos += 4;
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(pos), p.HalfSizeZ); pos += 4;
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(pos), p.SurfaceY); pos += 4;
            }

            // Bounds
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(pos), arena.MinX); pos += 4;
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(pos), arena.MaxX); pos += 4;
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(pos), arena.MinZ); pos += 4;
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(pos), arena.MaxZ); pos += 4;

            // Spawns
            int spawnCount32 = arena.SpawnPoints?.Length ?? 0;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), spawnCount32); pos += 4;
            for (int i = 0; i < spawnCount32; i++)
            {
                var s = arena.SpawnPoints![i];
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(pos), s.X); pos += 4;
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(pos), s.Y); pos += 4;
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(pos), s.Z); pos += 4;
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(pos), s.Yaw); pos += 4;
            }

            return buffer;
        }

        /// <summary>
        /// Deserialize an ArenaDefinition from a byte array.
        /// Returns null if the data is invalid or version mismatch.
        /// </summary>
        public static ArenaDefinition? Deserialize(byte[] data)
        {
            int pos = 0;

            // Magic
            if (data.Length < 8) return null;
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos)); pos += 4;
            if (magic != Magic) return null;

            uint version = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos)); pos += 4;
            if (version != Version) return null;

            var arena = new ArenaDefinition();

            // Name
            arena.Name = ReadString(data, ref pos)!;
            if (arena.Name == null) return null;

            // DisplayName
            arena.DisplayName = ReadString(data, ref pos)!;
            if (arena.DisplayName == null) return null;

            // ScenePath
            arena.ScenePath = ReadString(data, ref pos)!;
            if (arena.ScenePath == null) return null;

            // KillHeight
            if (pos + 4 > data.Length) return null;
            arena.KillHeight = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos)); pos += 4;

            // FloorHeight
            if (pos + 4 > data.Length) return null;
            arena.FloorHeight = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos)); pos += 4;

            // Platforms
            if (pos + 4 > data.Length) return null;
            int platCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos)); pos += 4;
            arena.Platforms = new PlatformDef[platCount];
            for (int i = 0; i < platCount; i++)
            {
                if (pos + 20 > data.Length) return null;
                float cx = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos)); pos += 4;
                float cz = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos)); pos += 4;
                float hx = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos)); pos += 4;
                float hz = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos)); pos += 4;
                float sy = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos)); pos += 4;
                arena.Platforms[i] = new PlatformDef { CenterX = cx, CenterZ = cz, HalfSizeX = hx, HalfSizeZ = hz, SurfaceY = sy };
            }

            // Bounds
            if (pos + 16 > data.Length) return null;
            arena.MinX = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos)); pos += 4;
            arena.MaxX = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos)); pos += 4;
            arena.MinZ = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos)); pos += 4;
            arena.MaxZ = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos)); pos += 4;

            // Spawns
            if (pos + 4 > data.Length) return null;
            int spawnCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos)); pos += 4;
            arena.SpawnPoints = new SpawnPoint[spawnCount];
            for (int i = 0; i < spawnCount; i++)
            {
                if (pos + 16 > data.Length) return null;
                float sx = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos)); pos += 4;
                float sy = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos)); pos += 4;
                float sz = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos)); pos += 4;
                float syaw = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos)); pos += 4;
                arena.SpawnPoints[i] = new SpawnPoint { X = sx, Y = sy, Z = sz, Yaw = syaw };
            }

            return arena;
        }

        /// <summary>Load from file path. Returns null on failure.</summary>
        public static ArenaDefinition? LoadFromFile(string path)
        {
            try
            {
                byte[] data = System.IO.File.ReadAllBytes(path);
                return Deserialize(data);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Save to file path.</summary>
        public static void SaveToFile(string path, ArenaDefinition arena)
        {
            byte[] data = Serialize(arena);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");
            System.IO.File.WriteAllBytes(path, data);
        }

        // ── String helpers ──

        private static void WriteString(byte[] buffer, ref int pos, string value)
        {
            int byteLen = Encoding.UTF8.GetByteCount(value);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), byteLen); pos += 4;
            if (byteLen > 0)
            {
                Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, pos);
                pos += byteLen;
            }
        }

        private static string? ReadString(byte[] data, ref int pos)
        {
            if (pos + 4 > data.Length) return null;
            int len = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos)); pos += 4;
            if (pos + len > data.Length) return null;
            if (len == 0) return "";
            string result = Encoding.UTF8.GetString(data, pos, len);
            pos += len;
            return result;
        }
    }
}
