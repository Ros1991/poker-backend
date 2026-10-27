using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TorneioKnockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "bounty_amount",
                table: "tournaments",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "knockout_type",
                table: "tournaments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<decimal>(
                name: "total_bounties",
                table: "tournaments",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "bounty_head",
                table: "tournament_entries",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "bounty_won",
                table: "tournament_entries",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "bounty_amount",
                table: "eliminations",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "bounty_cash",
                table: "eliminations",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "bounty_to_head",
                table: "eliminations",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "default_bounty_amount",
                table: "blind_structures",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "default_knockout_type",
                table: "blind_structures",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tournaments_knockout_type",
                table: "tournaments",
                sql: "knockout_type IN ('None','Simple','Progressive')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tournaments_knockout_type",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "bounty_amount",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "knockout_type",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "total_bounties",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "bounty_head",
                table: "tournament_entries");

            migrationBuilder.DropColumn(
                name: "bounty_won",
                table: "tournament_entries");

            migrationBuilder.DropColumn(
                name: "bounty_amount",
                table: "eliminations");

            migrationBuilder.DropColumn(
                name: "bounty_cash",
                table: "eliminations");

            migrationBuilder.DropColumn(
                name: "bounty_to_head",
                table: "eliminations");

            migrationBuilder.DropColumn(
                name: "default_bounty_amount",
                table: "blind_structures");

            migrationBuilder.DropColumn(
                name: "default_knockout_type",
                table: "blind_structures");
        }
    }
}
