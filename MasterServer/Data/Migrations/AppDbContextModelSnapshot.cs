using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MasterServer.Data;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MasterServer.Data.Migrations
{
    [DbContext(typeof(AppDbContext))]
    partial class AppDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("MasterServer.Data.Models.GameServer", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("ApiToken")
                        .IsRequired()
                        .HasMaxLength(128)
                        .HasColumnType("character varying(128)");

                    b.Property<int>("CurrentMatches")
                        .HasColumnType("integer");

                    b.Property<string>("CustomRulesJson")
                        .HasColumnType("text");

                    b.Property<string>("IpAddress")
                        .IsRequired()
                        .HasMaxLength(45)
                        .HasColumnType("character varying(45)");

                    b.Property<bool>("IsOfficial")
                        .HasColumnType("boolean");

                    b.Property<DateTime>("LastHeartbeat")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("MaxConcurrentMatches")
                        .HasColumnType("integer");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(128)
                        .HasColumnType("character varying(128)");

                    b.Property<int>("Port")
                        .HasColumnType("integer");

                    b.Property<string>("Region")
                        .IsRequired()
                        .HasMaxLength(16)
                        .HasColumnType("character varying(16)");

                    b.HasKey("Id");

                    b.HasIndex("Region");

                    b.ToTable("GameServers");
                });

            modelBuilder.Entity("MasterServer.Data.Models.Match", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<DateTime?>("EndedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<long>("Player1SteamId")
                        .HasColumnType("bigint");

                    b.Property<long>("Player2SteamId")
                        .HasColumnType("bigint");

                    b.Property<string>("ServerRegion")
                        .IsRequired()
                        .HasMaxLength(16)
                        .HasColumnType("character varying(16)");

                    b.Property<DateTime>("StartedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<long?>("WinnerSteamId")
                        .HasColumnType("bigint");

                    b.HasKey("Id");

                    b.HasIndex("Player1SteamId");

                    b.HasIndex("Player2SteamId");

                    b.HasIndex("WinnerSteamId");

                    b.ToTable("Matches");
                });

            modelBuilder.Entity("MasterServer.Data.Models.User", b =>
                {
                    b.Property<long>("SteamId")
                        .HasColumnType("bigint");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTime?>("LastLogin")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("Mmr")
                        .HasColumnType("integer");

                    b.Property<string>("Username")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("character varying(64)");

                    b.HasKey("SteamId");

                    b.ToTable("Users");
                });

            modelBuilder.Entity("MasterServer.Data.Models.Match", b =>
                {
                    b.HasOne("MasterServer.Data.Models.User", "Player1")
                        .WithMany()
                        .HasForeignKey("Player1SteamId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("MasterServer.Data.Models.User", "Player2")
                        .WithMany()
                        .HasForeignKey("Player2SteamId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("MasterServer.Data.Models.User", "Winner")
                        .WithMany()
                        .HasForeignKey("WinnerSteamId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.Navigation("Player1");

                    b.Navigation("Player2");

                    b.Navigation("Winner");
                });
#pragma warning restore 612, 618
        }
    }
}
