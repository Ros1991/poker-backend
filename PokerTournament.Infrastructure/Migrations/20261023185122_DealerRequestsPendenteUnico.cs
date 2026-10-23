using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <summary>
    /// SPEC-20261023-1208 (correção da revisão): no máximo UM pedido pendente por aparelho
    /// e torneio. Dois POSTs simultâneos do mesmo aparelho criavam dois pedidos Pending; o
    /// índice único parcial deixa entrar um só (o serviço captura a violação e devolve o
    /// pedido que já existe). Antes de criar o índice, resolve duplicatas que já existam:
    /// fica o Pending mais recente de cada par e os outros viram Rejected (como um pedido
    /// vencido: decided_by nulo). Sem schema explícito: segue o search_path / DB_SCHEMA.
    /// </summary>
    public partial class DealerRequestsPendenteUnico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE dealer_requests AS r
                SET status = 'Rejected', decided_at = now(), updated_at = now()
                WHERE r.status = 'Pending'
                  AND EXISTS (
                    SELECT 1
                    FROM dealer_requests AS n
                    WHERE n.tournament_id = r.tournament_id
                      AND n.device_key_hash = r.device_key_hash
                      AND n.status = 'Pending'
                      AND (n.requested_at > r.requested_at
                           OR (n.requested_at = r.requested_at AND n.id > r.id)));
                """);

            migrationBuilder.CreateIndex(
                name: "ux_dealer_requests_pending_device",
                table: "dealer_requests",
                columns: new[] { "tournament_id", "device_key_hash" },
                unique: true,
                filter: "status = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_dealer_requests_pending_device",
                table: "dealer_requests");
        }
    }
}
