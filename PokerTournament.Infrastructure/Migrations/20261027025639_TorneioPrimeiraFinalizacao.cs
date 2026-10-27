using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TorneioPrimeiraFinalizacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_finished_at",
                table: "tournaments",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill: torneio que está Finalizado (inclusive excluído) recebe a data da última gravação (a de criação nos
            // torneios históricos do seed, que não têm updated_at); torneio que já
            // foi finalizado pelo botão e depois reaberto pelo desfazer eliminação, a data do registro dessa troca de status
            // na auditoria (o finalizar sozinho na última eliminação não gera auditoria: esses ficam sem a data).
            migrationBuilder.Sql(
                "UPDATE tournaments SET first_finished_at = COALESCE(updated_at, created_at) " +
                "WHERE status = 'Finished' AND first_finished_at IS NULL;");
            migrationBuilder.Sql(
                "UPDATE tournaments t SET first_finished_at = a.finished_at " +
                "FROM (SELECT entity_id, MIN(created_at) AS finished_at FROM audit_logs " +
                "WHERE entity_type = 'Tournament' AND action = 'StatusChange' " +
                "AND new_values ->> 'Status' = 'Finished' GROUP BY entity_id) a " +
                "WHERE a.entity_id = t.id AND t.first_finished_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "first_finished_at",
                table: "tournaments");
        }
    }
}
