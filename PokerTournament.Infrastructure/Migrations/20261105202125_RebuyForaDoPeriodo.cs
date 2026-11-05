using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RebuyForaDoPeriodo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "outside_window",
                table: "transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "outside_window",
                table: "transactions");
        }
    }
}
