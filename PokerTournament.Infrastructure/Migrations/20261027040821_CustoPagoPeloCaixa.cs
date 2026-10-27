using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CustoPagoPeloCaixa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Parte paga pelo caixa (botão "Pagar" da aba Custos), guardada à parte dos créditos dos jogadores (revisão
            // geral 2 do programa Ajustes pós-lançamento): custo que encolhe depois de pago não perde o que o caixa entregou.
            migrationBuilder.AddColumn<decimal>(
                name: "paid_by_cash_box",
                table: "cost_extras",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            // Backfill: o pago de hoje que os jogadores não explicam veio do caixa. Créditos de jogadores pela mesma regra do
            // CostCredits.Credit: abatimento = valor; pagamento = PIX (ou o valor) limitado ao valor, nunca no custo Caixa;
            // estornado não conta.
            migrationBuilder.Sql(
                "UPDATE cost_extras c SET paid_by_cash_box = GREATEST(0, c.paid_amount - COALESCE((" +
                "SELECT SUM(CASE WHEN t.type = 'Settlement' THEN GREATEST(0, t.amount) " +
                "WHEN t.type = 'Payment' AND NOT c.is_cash_box THEN LEAST(GREATEST(0, COALESCE(t.pix_amount, t.amount)), GREATEST(0, t.amount)) " +
                "ELSE 0 END) FROM transactions t " +
                "WHERE t.pix_destination_id = c.id AND t.tournament_id = c.tournament_id AND t.reversed_at IS NULL " +
                "AND t.type IN ('Payment', 'Settlement')), 0)) " +
                "WHERE c.paid_amount > 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "paid_by_cash_box",
                table: "cost_extras");
        }
    }
}
