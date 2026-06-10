using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MasterServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameServers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsOfficial = table.Column<bool>(type: "boolean", nullable: false),
                    MaxConcurrentMatches = table.Column<int>(type: "integer", nullable: false),
                    CurrentMatches = table.Column<int>(type: "integer", nullable: false),
                    CustomRulesJson = table.Column<string>(type: "text", nullable: true),
                    LastHeartbeat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApiToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameServers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    SteamId = table.Column<long>(type: "bigint", nullable: false),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Mmr = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLogin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.SteamId);
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Player1SteamId = table.Column<long>(type: "bigint", nullable: false),
                    Player2SteamId = table.Column<long>(type: "bigint", nullable: false),
                    WinnerSteamId = table.Column<long>(type: "bigint", nullable: true),
                    ServerRegion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Matches_Users_Player1SteamId",
                        column: x => x.Player1SteamId,
                        principalTable: "Users",
                        principalColumn: "SteamId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Matches_Users_Player2SteamId",
                        column: x => x.Player2SteamId,
                        principalTable: "Users",
                        principalColumn: "SteamId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Matches_Users_WinnerSteamId",
                        column: x => x.WinnerSteamId,
                        principalTable: "Users",
                        principalColumn: "SteamId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameServers_Region",
                table: "GameServers",
                column: "Region");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_Player1SteamId",
                table: "Matches",
                column: "Player1SteamId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_Player2SteamId",
                table: "Matches",
                column: "Player2SteamId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_WinnerSteamId",
                table: "Matches",
                column: "WinnerSteamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameServers");

            migrationBuilder.DropTable(
                name: "Matches");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
