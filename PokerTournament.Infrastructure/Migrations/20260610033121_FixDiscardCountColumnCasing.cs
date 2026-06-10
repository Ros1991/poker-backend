using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixDiscardCountColumnCasing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DiscardCount",
                schema: "public",
                table: "rankings",
                newName: "discard_count");

            migrationBuilder.AlterColumn<int>(
                name: "discard_count",
                schema: "public",
                table: "rankings",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "discard_count",
                schema: "public",
                table: "rankings",
                newName: "DiscardCount");

            migrationBuilder.AlterColumn<int>(
                name: "DiscardCount",
                schema: "public",
                table: "rankings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);
        }
    }
}
