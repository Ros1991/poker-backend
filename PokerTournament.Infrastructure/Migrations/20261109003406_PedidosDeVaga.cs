using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PedidosDeVaga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tournament_join_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    waitlist_position = table.Column<int>(type: "integer", nullable: true),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tournament_join_requests", x => x.id);
                    table.CheckConstraint("ck_tournament_join_requests_status", "status IN ('Pending','Approved','Waitlisted','Declined','Cancelled')");
                    table.ForeignKey(
                        name: "FK_tournament_join_requests_persons_person_id",
                        column: x => x.person_id,
                        principalSchema: "public",
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tournament_join_requests_tournament_entries_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "public",
                        principalTable: "tournament_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tournament_join_requests_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalSchema: "public",
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tournament_join_requests_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tournament_reminders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tournament_reminders", x => x.id);
                    table.ForeignKey(
                        name: "FK_tournament_reminders_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalSchema: "public",
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tournament_reminders_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tournament_join_requests_entry_id",
                table: "tournament_join_requests",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_tournament_join_requests_person_id",
                table: "tournament_join_requests",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_tournament_join_requests_tournament_id_status",
                table: "tournament_join_requests",
                columns: new[] { "tournament_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tournament_join_requests_user_id_status",
                table: "tournament_join_requests",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_tournament_join_requests_open",
                table: "tournament_join_requests",
                columns: new[] { "tournament_id", "user_id" },
                unique: true,
                filter: "status IN ('Pending','Approved','Waitlisted')");

            migrationBuilder.CreateIndex(
                name: "IX_tournament_reminders_user_id",
                table: "tournament_reminders",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_tournament_reminders_tournament_user",
                table: "tournament_reminders",
                columns: new[] { "tournament_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tournament_join_requests");

            migrationBuilder.DropTable(
                name: "tournament_reminders");
        }
    }
}
