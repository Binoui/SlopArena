using System.Runtime.InteropServices;
using SlopArena.Shared;

namespace SlopArena.Server
{
    static class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== SlopArena Game Server ===");

            // Load configuration
            string configPath = args.Length > 0 ? args[0] : "server.json";
            var config = ServerConfig.Load(configPath);
            Console.WriteLine($"Server: {config.ServerName}");
            Console.WriteLine($"Region: {config.Region}");
            Console.WriteLine($"Port range: {config.Port}-{config.Port + config.MaxConcurrentMatches - 1}");
            Console.WriteLine($"Max concurrent matches: {config.MaxConcurrentMatches}");
            Console.WriteLine($"Master server: {config.MasterServerUrl}");
            Console.WriteLine($"Arena data: {config.ArenaDataDir}");
            Console.WriteLine();

            // Gameplay arenas are loaded from the published .arena files.
            ArenaRegistry.LoadFromDirectory(config.ArenaDataDir);

            var orchestrator = new MultiMatchOrchestrator(config);
            bool contentReady = orchestrator.ContentReady && ArenaRegistry.Get("slop_court").HasValue;
            if (!contentReady)
            {
                Console.WriteLine("[Content] Required cooked roster or default arena unavailable; readiness disabled.");
                orchestrator.StopAcceptingMatches();
            }
            var registration = new GameServerRegistration(config, orchestrator);
            using var cts = new CancellationTokenSource();

            // Report finished-match results (winner steam id) to the master server (issue #40).
            // Fire-and-forget: ReportMatchResultAsync swallows errors; shared victory reports 0.
            orchestrator.ReportMatchResult = (matchId, winner) =>
                _ = registration.ReportMatchResultAsync(matchId, winner);

            using var control = new MatchControlServer(orchestrator, config.Port,
                defaultArena: "slop_court", controlKey: config.IsVps ? config.MatchControlKey : null,
                isReady: () => contentReady && registration.HasFreshRegistration);
            control.Start();

            void BeginShutdown()
            {
                orchestrator.StopAcceptingMatches();
                control.Stop();
                cts.Cancel();
            }

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                BeginShutdown();
            };
            using var sigterm = OperatingSystem.IsWindows() ? null :
                PosixSignalRegistration.Create(PosixSignal.SIGTERM, e =>
                {
                    e.Cancel = true;
                    BeginShutdown();
                });

            Console.WriteLine("Registering with master server (recovery enabled).");
            try
            {
                await registration.RunAsync(cts.Token);
            }
            finally
            {
                BeginShutdown();
                orchestrator.Shutdown();
                Console.WriteLine("Server stopped.");
            }

        }
    }
}
