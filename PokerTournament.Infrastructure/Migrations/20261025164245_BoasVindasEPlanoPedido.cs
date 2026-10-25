using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <summary>
    /// Boas-vindas e escolha de plano (SPEC-20261023-1212): <c>onboarded_at</c> (null = o primeiro acesso ainda leva às
    /// boas-vindas), <c>requested_plan</c> e <c>requested_plan_at</c> (Clube/Liga pedido nas boas-vindas; o plano só muda
    /// pelo Admin). Toda conta que já existe — inclusive as fundadoras (Clube cortesia da SPEC-1205) — fica marcada
    /// como integrada e não cai nas boas-vindas; só as contas criadas depois desta migration passam por elas.
    /// </summary>
    public partial class BoasVindasEPlanoPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "onboarded_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "requested_plan",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "requested_plan_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            // Contas existentes (e fundadoras) = já integradas
            migrationBuilder.Sql("""
                UPDATE users
                SET onboarded_at = COALESCE(email_confirmed_at, last_login_at, created_at)
                WHERE onboarded_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "onboarded_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "requested_plan",
                table: "users");

            migrationBuilder.DropColumn(
                name: "requested_plan_at",
                table: "users");
        }
    }
}
