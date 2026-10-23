using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <summary>
    /// SPEC-20261023-1208: autorização do dealer pelo organizador. Cria dealer_requests
    /// (pedido por aparelho: Pending/Approved/Rejected/Revoked) e, em dealer_accesses,
    /// troca os contadores da troca direta (removida) por superseded_at (QR substituído
    /// por um novo sem derrubar quem já foi aprovado).
    /// </summary>
    public partial class DealerRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "exchange_count",
                table: "dealer_accesses");

            migrationBuilder.DropColumn(
                name: "last_exchange_at",
                table: "dealer_accesses");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "superseded_at",
                table: "dealer_accesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "dealer_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dealer_access_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    device_key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    user_agent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dealer_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_dealer_requests_dealer_accesses_dealer_access_id",
                        column: x => x.dealer_access_id,
                        principalTable: "dealer_accesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_dealer_requests_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dealer_requests_dealer_access_id",
                table: "dealer_requests",
                column: "dealer_access_id");

            migrationBuilder.CreateIndex(
                name: "ix_dealer_requests_device_key_hash",
                table: "dealer_requests",
                column: "device_key_hash");

            migrationBuilder.CreateIndex(
                name: "ix_dealer_requests_tournament_id_status",
                table: "dealer_requests",
                columns: new[] { "tournament_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dealer_requests");

            migrationBuilder.DropColumn(
                name: "superseded_at",
                table: "dealer_accesses");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_exchange_at",
                table: "dealer_accesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "exchange_count",
                table: "dealer_accesses",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
