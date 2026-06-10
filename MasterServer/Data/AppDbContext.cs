// MasterServer/Data/AppDbContext.cs
using Microsoft.EntityFrameworkCore;
using MasterServer.Data.Models;

namespace MasterServer.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Match> Matches { get; set; } = null!;
    public DbSet<GameServer> GameServers { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.SteamId);
            entity.Property(e => e.Username).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Mmr).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
        });

        // Match entity
        modelBuilder.Entity<Match>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Player1SteamId);
            entity.HasIndex(e => e.Player2SteamId);
            entity.Property(e => e.ServerRegion).HasMaxLength(16).IsRequired();

            // Foreign keys use Restrict on removal
            entity.HasOne(e => e.Player1)
                .WithMany()
                .HasForeignKey(e => e.Player1SteamId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Player2)
                .WithMany()
                .HasForeignKey(e => e.Player2SteamId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Winner)
                .WithMany()
                .HasForeignKey(e => e.WinnerSteamId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // GameServer entity
        modelBuilder.Entity<GameServer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Region);
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();
            entity.Property(e => e.IpAddress).HasMaxLength(45).IsRequired();
            entity.Property(e => e.Region).HasMaxLength(16).IsRequired();
            entity.Property(e => e.ApiToken).HasMaxLength(128).IsRequired();
        });
    }
}
