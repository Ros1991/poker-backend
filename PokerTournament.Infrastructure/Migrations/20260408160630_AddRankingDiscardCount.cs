using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRankingDiscardCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.RenameTable(
                name: "users",
                newName: "users",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "transactions",
                newName: "transactions",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "tournaments",
                newName: "tournaments",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "tournament_tables",
                newName: "tournament_tables",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "tournament_prizes",
                newName: "tournament_prizes",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "tournament_entries",
                newName: "tournament_entries",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "tournament_dealers",
                newName: "tournament_dealers",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "tournament_blind_levels",
                newName: "tournament_blind_levels",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "scoring_rules",
                newName: "scoring_rules",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "rankings",
                newName: "rankings",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "ranking_scores",
                newName: "ranking_scores",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "persons",
                newName: "persons",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "notification_logs",
                newName: "notification_logs",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "home_games",
                newName: "home_games",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "home_game_members",
                newName: "home_game_members",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "eliminations",
                newName: "eliminations",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "cost_extras",
                newName: "cost_extras",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "blind_structures",
                newName: "blind_structures",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "blind_levels",
                newName: "blind_levels",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "audit_logs",
                newName: "audit_logs",
                newSchema: "public");

            migrationBuilder.AddColumn<int>(
                name: "DiscardCount",
                schema: "public",
                table: "rankings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscardCount",
                schema: "public",
                table: "rankings");

            migrationBuilder.RenameTable(
                name: "users",
                schema: "public",
                newName: "users");

            migrationBuilder.RenameTable(
                name: "transactions",
                schema: "public",
                newName: "transactions");

            migrationBuilder.RenameTable(
                name: "tournaments",
                schema: "public",
                newName: "tournaments");

            migrationBuilder.RenameTable(
                name: "tournament_tables",
                schema: "public",
                newName: "tournament_tables");

            migrationBuilder.RenameTable(
                name: "tournament_prizes",
                schema: "public",
                newName: "tournament_prizes");

            migrationBuilder.RenameTable(
                name: "tournament_entries",
                schema: "public",
                newName: "tournament_entries");

            migrationBuilder.RenameTable(
                name: "tournament_dealers",
                schema: "public",
                newName: "tournament_dealers");

            migrationBuilder.RenameTable(
                name: "tournament_blind_levels",
                schema: "public",
                newName: "tournament_blind_levels");

            migrationBuilder.RenameTable(
                name: "scoring_rules",
                schema: "public",
                newName: "scoring_rules");

            migrationBuilder.RenameTable(
                name: "rankings",
                schema: "public",
                newName: "rankings");

            migrationBuilder.RenameTable(
                name: "ranking_scores",
                schema: "public",
                newName: "ranking_scores");

            migrationBuilder.RenameTable(
                name: "persons",
                schema: "public",
                newName: "persons");

            migrationBuilder.RenameTable(
                name: "notification_logs",
                schema: "public",
                newName: "notification_logs");

            migrationBuilder.RenameTable(
                name: "home_games",
                schema: "public",
                newName: "home_games");

            migrationBuilder.RenameTable(
                name: "home_game_members",
                schema: "public",
                newName: "home_game_members");

            migrationBuilder.RenameTable(
                name: "eliminations",
                schema: "public",
                newName: "eliminations");

            migrationBuilder.RenameTable(
                name: "cost_extras",
                schema: "public",
                newName: "cost_extras");

            migrationBuilder.RenameTable(
                name: "blind_structures",
                schema: "public",
                newName: "blind_structures");

            migrationBuilder.RenameTable(
                name: "blind_levels",
                schema: "public",
                newName: "blind_levels");

            migrationBuilder.RenameTable(
                name: "audit_logs",
                schema: "public",
                newName: "audit_logs");
        }
    }
}
