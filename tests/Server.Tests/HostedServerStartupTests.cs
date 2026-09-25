using SlopArena.Server;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Server.Tests;

public class HostedServerStartupTests
{
    [Fact]
    public void EmbeddedHostConfig_StartsExplicitDevelopmentServer()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, new HostedServerConfig
            {
                ServerName = "Hosted match",
                MasterServerUrl = "http://127.0.0.1:19100",
                PublicIp = "game.example.test",
                Port = 19200,
                ArenaDataDir = "/example/arenas"
            }.ToJson());

            var server = ServerConfig.Load(path);
            Assert.Equal("development", server.DeploymentProfile);
            Assert.False(server.IsVps);
            Assert.Equal("game.example.test", server.PublicIp);
            Assert.Equal(19200, server.Port);
            Assert.Equal("/example/arenas", server.ArenaDataDir);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
