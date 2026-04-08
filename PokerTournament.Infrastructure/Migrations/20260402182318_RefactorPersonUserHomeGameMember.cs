using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefactorPersonUserHomeGameMember : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add new columns to users (copy from person)
            migrationBuilder.AddColumn<string>(
                name: "full_name",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nickname",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "photo_url",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "whatsapp",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Step 2: Copy data from persons to users
            migrationBuilder.Sql(@"
                UPDATE users u
                SET full_name = p.full_name,
                    nickname = p.nickname,
                    photo_url = p.photo_url,
                    whatsapp = p.whatsapp
                FROM persons p
                WHERE u.person_id = p.id
            ");

            // Step 3: Make full_name NOT NULL
            migrationBuilder.AlterColumn<string>(
                name: "full_name",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // Step 4: Add home_game_id to persons (nullable first)
            migrationBuilder.AddColumn<Guid>(
                name: "home_game_id",
                table: "persons",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "type",
                table: "persons",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Jogador");

            // Step 5: Set home_game_id for existing persons (use first home_game or null)
            migrationBuilder.Sql(@"
                UPDATE persons p
                SET home_game_id = (
                    SELECT hg.id FROM home_games hg 
                    WHERE hg.is_active = true 
                    ORDER BY hg.created_at 
                    LIMIT 1
                )
                WHERE p.home_game_id IS NULL
            ");

            // Step 6: Delete persons without home_game_id (orphans)
            migrationBuilder.Sql(@"
                DELETE FROM persons WHERE home_game_id IS NULL
            ");

            // Step 7: Make home_game_id NOT NULL
            migrationBuilder.AlterColumn<Guid>(
                name: "home_game_id",
                table: "persons",
                type: "uuid",
                nullable: false);

            // Step 8: Add user_id to home_game_members (nullable first)
            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "home_game_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_admin",
                table: "home_game_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Step 9: Set user_id from person.user relationship
            migrationBuilder.Sql(@"
                UPDATE home_game_members hgm
                SET user_id = u.id,
                    is_admin = (hgm.role = 'Admin')
                FROM persons p
                JOIN users u ON u.person_id = p.id
                WHERE hgm.person_id = p.id
            ");

            // Step 10: Delete home_game_members without user_id
            migrationBuilder.Sql(@"
                DELETE FROM home_game_members WHERE user_id IS NULL
            ");

            // Step 11: Make user_id NOT NULL
            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "home_game_members",
                type: "uuid",
                nullable: false);

            // Step 12: Make person_id nullable
            migrationBuilder.AlterColumn<Guid>(
                name: "person_id",
                table: "home_game_members",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // Step 13: Drop old columns and indexes
            migrationBuilder.DropForeignKey(
                name: "FK_users_persons_person_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_person_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_persons_email",
                table: "persons");

            migrationBuilder.DropIndex(
                name: "ix_persons_full_name",
                table: "persons");

            migrationBuilder.DropIndex(
                name: "ix_persons_whatsapp",
                table: "persons");

            migrationBuilder.DropIndex(
                name: "ix_home_game_members_home_game_person",
                table: "home_game_members");

            migrationBuilder.DropColumn(
                name: "person_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "role",
                table: "home_game_members");

            // Step 14: Create new indexes
            migrationBuilder.CreateIndex(
                name: "ix_persons_home_game_email",
                table: "persons",
                columns: new[] { "home_game_id", "email" },
                unique: true,
                filter: "email IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_persons_home_game_type",
                table: "persons",
                columns: new[] { "home_game_id", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_persons_home_game_whatsapp",
                table: "persons",
                columns: new[] { "home_game_id", "whatsapp" },
                unique: true,
                filter: "whatsapp IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_home_game_members_home_game_user",
                table: "home_game_members",
                columns: new[] { "home_game_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_home_game_members_user_id",
                table: "home_game_members",
                column: "user_id");

            // Step 15: Add foreign keys
            migrationBuilder.AddForeignKey(
                name: "FK_home_game_members_users_user_id",
                table: "home_game_members",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_persons_home_games_home_game_id",
                table: "persons",
                column: "home_game_id",
                principalTable: "home_games",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This migration is not easily reversible due to data transformations
            throw new NotSupportedException("This migration cannot be reversed.");
        }
    }
}
