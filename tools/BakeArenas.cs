using SlopArena.Shared;

namespace SlopArena.Tools;

static class BakeArenas
{
    /// <summary>
    /// Usage: dotnet run [output-directory]
    /// Default output: data/arenas/ (relative to project root)
    /// </summary>
    static void Main(string[] args)
    {
        // Default: ../../data/arenas/ relative to tools/BakeArenas/bin/Debug/net8.0/
        // Or pass as arg. But we use the project-relative path via AppContext
        string baseDir = args.Length > 0 ? args[0] : "data/arenas";

        // If running via dotnet run --project tools/BakeArenas.csproj from project root,
        // the working directory is the project root. So "data/arenas" is correct.
        string outputDir = Path.GetFullPath(baseDir);
        System.IO.Directory.CreateDirectory(outputDir);

        int count = 0;
        foreach (var arena in ArenaRegistry.All)
        {
            string path = System.IO.Path.Combine(outputDir, arena.Name + ".arena");
            ArenaBinaryFormat.SaveToFile(path, arena);
            Console.WriteLine($"Baked: {arena.Name} -> {path}");
            Console.WriteLine($"  Platforms: {arena.Platforms?.Length ?? 0}  Spawns: {arena.SpawnPoints?.Length ?? 0}");
            count++;
        }

        Console.WriteLine($"\nDone! {count} arenas baked to {outputDir}/");
    }
}
