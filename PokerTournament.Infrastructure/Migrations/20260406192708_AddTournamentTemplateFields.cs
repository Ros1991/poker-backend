using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentTemplateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "default_addon",
                table: "blind_structures",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "default_addon_allowed",
                table: "blind_structures",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "default_addon_double_allowed",
                table: "blind_structures",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "default_addon_stack",
                table: "blind_structures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "default_buy_in",
                table: "blind_structures",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "default_late_registration_level",
                table: "blind_structures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "default_max_rebuys",
                table: "blind_structures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "default_rebuy",
                table: "blind_structures",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "default_rebuy_stack",
                table: "blind_structures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "default_rebuy_until_level",
                table: "blind_structures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "default_starting_stack",
                table: "blind_structures",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_addon",
                table: "blind_structures");

            migrationBuilder.DropColumn(
                name: "default_addon_allowed",
                table: "blind_structures");

            migrationBuilder.DropColumn(
                name: "default_addon_double_allowed",
                table: "blind_structures");

            migrationBuilder.DropColumn(
                name: "default_addon_stack",
                table: "blind_structures");

            migrationBuilder.DropColumn(
                name: "default_buy_in",
                table: "blind_structures");

            migrationBuilder.DropColumn(
                name: "default_late_registration_level",
                table: "blind_structures");

            migrationBuilder.DropColumn(
                name: "default_max_rebuys",
                table: "blind_structures");

            migrationBuilder.DropColumn(
                name: "default_rebuy",
                table: "blind_structures");

            migrationBuilder.DropColumn(
                name: "default_rebuy_stack",
                table: "blind_structures");

            migrationBuilder.DropColumn(
                name: "default_rebuy_until_level",
                table: "blind_structures");

            migrationBuilder.DropColumn(
                name: "default_starting_stack",
                table: "blind_structures");
        }
    }
}
