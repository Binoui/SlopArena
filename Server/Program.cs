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

            // Load arena definitions from .arena files (fallback to hardcoded)
            ArenaRegistry.LoadFromDirectory(config.ArenaDataDir);

            var orchestrator = new MultiMatchOrchestrator(config);
            var registration = new GameServerRegistration(config, orchestrator);
            var cts = new CancellationTokenSource();

            // Handle Ctrl+C for graceful shutdown
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\nCtrl+C received. Shutting down...");
                cts.Cancel();
            };

            // Register with master server
            Console.WriteLine("Registering with master server...");
            bool registered = await registration.RegisterAsync(cts.Token);

            if (!registered)
            {
                Console.WriteLine("WARNING: Failed to register with master server.");
                Console.WriteLine("The server will run without master server integration.");
                Console.WriteLine("Matches can still be assigned programmatically via AssignMatch().");
            }
            else
            {
                Console.WriteLine($"Registered successfully (Server ID: {registration.ServerId}).");
            }

            Console.WriteLine();
            Console.WriteLine("Orchestrator running. Press Ctrl+C to stop.");
            Console.WriteLine();

            // Start heartbeat loop (blocks until Ctrl+C)
            if (registered)
            {
                await registration.RunHeartbeatLoopAsync(cts.Token);
            }
            else
            {
                // Keep alive without heartbeat
                try { await Task.Delay(-1, cts.Token); } catch (TaskCanceledException) { }
            }

            orchestrator.Shutdown();
            Console.WriteLine("Server stopped.");
        }
    }
}
