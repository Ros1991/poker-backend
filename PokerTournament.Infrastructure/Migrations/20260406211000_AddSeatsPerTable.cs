using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSeatsPerTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "seats_per_table",
                table: "tournaments",
                type: "integer",
                nullable: false,
                defaultValue: 9);

            migrationBuilder.AddColumn<int>(
                name: "default_seats_per_table",
                table: "blind_structures",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "seats_per_table",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "default_seats_per_table",
                table: "blind_structures");
        }
    }
}
