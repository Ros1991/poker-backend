using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "row_version",
                table: "users");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "tournament_tables");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "tournament_prizes");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "tournament_entries");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "scoring_rules");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "rankings");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "ranking_scores");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "persons");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "notification_logs");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "home_games");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "home_game_members");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "eliminations");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "cost_extras");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "blind_structures");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "blind_levels");

            // xmin is a PostgreSQL system column that already exists on every table.
            // No need to add it -- EF Core/Npgsql will read it automatically.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // xmin is a PostgreSQL system column -- cannot be dropped.

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "users",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "transactions",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "tournaments",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "tournament_tables",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "tournament_prizes",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "tournament_entries",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "scoring_rules",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "rankings",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "ranking_scores",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "persons",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "notification_logs",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "home_games",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "home_game_members",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "eliminations",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "cost_extras",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "blind_structures",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "blind_levels",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }
    }
}
