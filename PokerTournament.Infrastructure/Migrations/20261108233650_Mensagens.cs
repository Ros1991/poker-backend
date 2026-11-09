using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Mensagens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_low_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_high_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversations", x => x.id);
                    table.ForeignKey(
                        name: "FK_conversations_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalSchema: "public",
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_conversations_users_user_high_id",
                        column: x => x.user_high_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_conversations_users_user_low_id",
                        column: x => x.user_low_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_members", x => x.id);
                    table.ForeignKey(
                        name: "FK_conversation_members_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "public",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_conversation_members_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "public",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_messages_users_sender_id",
                        column: x => x.sender_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reported_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reviewed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reports", x => x.id);
                    table.ForeignKey(
                        name: "FK_reports_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "public",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_reports_messages_message_id",
                        column: x => x.message_id,
                        principalSchema: "public",
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_reports_users_reported_user_id",
                        column: x => x.reported_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reports_users_reporter_id",
                        column: x => x.reporter_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reports_users_reviewed_by_id",
                        column: x => x.reviewed_by_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_members_user_id",
                table: "conversation_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_conversation_members_conversation_id_user_id",
                table: "conversation_members",
                columns: new[] { "conversation_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_created_by_id_created_at",
                table: "conversations",
                columns: new[] { "created_by_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_tournament_id",
                table: "conversations",
                column: "tournament_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_user_high_id_last_message_at",
                table: "conversations",
                columns: new[] { "user_high_id", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "ux_conversations_direct_pair",
                table: "conversations",
                columns: new[] { "user_low_id", "user_high_id" },
                unique: true,
                filter: "type = 'Direct'");

            migrationBuilder.CreateIndex(
                name: "ux_conversations_request_pair",
                table: "conversations",
                columns: new[] { "user_low_id", "user_high_id", "tournament_id" },
                unique: true,
                filter: "type = 'TournamentRequest'");

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id_sent_at",
                table: "messages",
                columns: new[] { "conversation_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_sender_id_sent_at",
                table: "messages",
                columns: new[] { "sender_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "IX_reports_conversation_id",
                table: "reports",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "IX_reports_message_id",
                table: "reports",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_reported_user_id",
                table: "reports",
                column: "reported_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_reports_reporter_id",
                table: "reports",
                column: "reporter_id");

            migrationBuilder.CreateIndex(
                name: "IX_reports_reviewed_by_id",
                table: "reports",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_status_created_at",
                table: "reports",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_members");

            migrationBuilder.DropTable(
                name: "reports");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "conversations");
        }
    }
}
