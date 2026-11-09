using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TorneiosPublicos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_public",
                table: "tournaments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "max_players",
                table: "tournaments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "public_description",
                table: "tournaments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "public_slug",
                table: "tournaments",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "show_organizer_whatsapp",
                table: "tournaments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_tournaments_public_date",
                table: "tournaments",
                columns: new[] { "is_public", "date" },
                filter: "is_public");

            migrationBuilder.CreateIndex(
                name: "ux_tournaments_public_slug",
                table: "tournaments",
                column: "public_slug",
                unique: true,
                filter: "public_slug IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tournaments_public_date",
                table: "tournaments");

            migrationBuilder.DropIndex(
                name: "ux_tournaments_public_slug",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "is_public",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "max_players",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "public_description",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "public_slug",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "show_organizer_whatsapp",
                table: "tournaments");
        }
    }
}
