using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRankingScoringModeAndTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "scoring_mode",
                table: "rankings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Formula");

            migrationBuilder.AddColumn<string>(
                name: "scoring_table",
                table: "rankings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "scoring_mode",
                table: "rankings");

            migrationBuilder.DropColumn(
                name: "scoring_table",
                table: "rankings");
        }
    }
}
