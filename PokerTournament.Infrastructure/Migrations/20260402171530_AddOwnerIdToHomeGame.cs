using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerIdToHomeGame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add column as nullable first
            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                table: "home_games",
                type: "uuid",
                nullable: true);

            // Step 2: Update existing home games to set owner_id to the first admin user
            migrationBuilder.Sql(@"
                UPDATE home_games 
                SET owner_id = (SELECT id FROM users WHERE role = 'Admin' AND is_active = true LIMIT 1)
                WHERE owner_id IS NULL
            ");

            // Step 3: Make column NOT NULL
            migrationBuilder.AlterColumn<Guid>(
                name: "owner_id",
                table: "home_games",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_home_games_owner_id",
                table: "home_games",
                column: "owner_id");

            migrationBuilder.AddForeignKey(
                name: "FK_home_games_users_owner_id",
                table: "home_games",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_home_games_users_owner_id",
                table: "home_games");

            migrationBuilder.DropIndex(
                name: "IX_home_games_owner_id",
                table: "home_games");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "home_games");
        }
    }
}
