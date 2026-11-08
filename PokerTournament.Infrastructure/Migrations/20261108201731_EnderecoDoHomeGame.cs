using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnderecoDoHomeGame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address_number",
                table: "home_games",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_visibility",
                table: "home_games",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "ApprovedOnly");

            migrationBuilder.AddColumn<string>(
                name: "city",
                table: "home_games",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "complement",
                table: "home_games",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "district",
                table: "home_games",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "geocode_failed_at",
                table: "home_games",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "geocode_precision",
                table: "home_games",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "geocoded_at",
                table: "home_games",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "latitude",
                table: "home_games",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                table: "home_games",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "postal_code",
                table: "home_games",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state",
                table: "home_games",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "street",
                table: "home_games",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "geocode_cache",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    query_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    found = table.Column<bool>(type: "boolean", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    resolved_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_geocode_cache", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_home_games_lat_lng",
                table: "home_games",
                columns: new[] { "latitude", "longitude" },
                filter: "latitude IS NOT NULL AND longitude IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_geocode_cache_query_key",
                table: "geocode_cache",
                column: "query_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "geocode_cache");

            migrationBuilder.DropIndex(
                name: "ix_home_games_lat_lng",
                table: "home_games");

            migrationBuilder.DropColumn(
                name: "address_number",
                table: "home_games");

            migrationBuilder.DropColumn(
                name: "address_visibility",
                table: "home_games");

            migrationBuilder.DropColumn(
                name: "city",
                table: "home_games");

            migrationBuilder.DropColumn(
                name: "complement",
                table: "home_games");

            migrationBuilder.DropColumn(
                name: "district",
                table: "home_games");

            migrationBuilder.DropColumn(
                name: "geocode_failed_at",
                table: "home_games");

            migrationBuilder.DropColumn(
                name: "geocode_precision",
                table: "home_games");

            migrationBuilder.DropColumn(
                name: "geocoded_at",
                table: "home_games");

            migrationBuilder.DropColumn(
                name: "latitude",
                table: "home_games");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "home_games");

            migrationBuilder.DropColumn(
                name: "postal_code",
                table: "home_games");

            migrationBuilder.DropColumn(
                name: "state",
                table: "home_games");

            migrationBuilder.DropColumn(
                name: "street",
                table: "home_games");
        }
    }
}
