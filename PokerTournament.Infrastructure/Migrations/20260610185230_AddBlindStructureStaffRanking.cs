using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBlindStructureStaffRanking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "default_ranking_contrib_mode",                table: "blind_structures",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "default_ranking_contrib_value",                table: "blind_structures",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "default_staff_amount",                table: "blind_structures",
                type: "numeric(10,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_ranking_contrib_mode",                table: "blind_structures");

            migrationBuilder.DropColumn(
                name: "default_ranking_contrib_value",                table: "blind_structures");

            migrationBuilder.DropColumn(
                name: "default_staff_amount",                table: "blind_structures");
        }
    }
}
