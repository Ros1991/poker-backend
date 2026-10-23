using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <summary>
    /// SPEC-20261023-1205: planos Mesa/Clube/Liga na conta. Adiciona users.plan (default 'Mesa'),
    /// plan_expires_at, plan_source (default 'Manual'), plan_note, plan_updated_at, plan_updated_by e o
    /// índice ix_users_plan. D10: TODAS as contas que já existem viram Clube cortesia sem vencimento com
    /// nota "fundador", com uma linha PlanChange por conta em audit_logs (user_name = 'migration'; Id e
    /// CreatedAt preenchidos aqui — AuditLog não é BaseEntity). O default Mesa só vale para conta criada
    /// depois (cadastro novo nasce Mesa). Roda depois da ConfirmacaoDeEmail.
    /// Sem schema fixo: vale o padrão da conexão (public no banco próprio de produção).
    /// </summary>
    public partial class PlanosMesaClubeLiga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "plan",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Mesa");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "plan_expires_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plan_note",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plan_source",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "plan_updated_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "plan_updated_by",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_plan",
                table: "users",
                column: "plan");

            // D10: quem já usa vira Clube cortesia "fundador", sem vencimento (todas as linhas)
            migrationBuilder.Sql("""
                UPDATE users
                SET plan = 'Clube',
                    plan_expires_at = NULL,
                    plan_source = 'Cortesia',
                    plan_note = 'fundador',
                    plan_updated_at = now(),
                    plan_updated_by = NULL;
                """);

            // Histórico da troca: uma linha PlanChange por conta (mesmo formato do PlanService.ChangePlanAsync)
            migrationBuilder.Sql("""
                INSERT INTO audit_logs (id, entity_type, entity_id, action, old_values, new_values, changed_fields, user_id, user_name, created_at)
                SELECT gen_random_uuid(),
                       'User',
                       u.id,
                       'PlanChange',
                       '{"Plan":"Mesa","PlanExpiresAt":null,"PlanSource":"Manual","PlanNote":null}'::jsonb,
                       '{"Plan":"Clube","PlanSource":"Cortesia","PlanNote":"fundador"}'::jsonb,
                       '["Plan","PlanSource","PlanNote"]'::jsonb,
                       NULL,
                       'migration',
                       now()
                FROM users u;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM audit_logs
                WHERE entity_type = 'User' AND action = 'PlanChange' AND user_name = 'migration';
                """);

            migrationBuilder.DropIndex(
                name: "ix_users_plan",
                table: "users");

            migrationBuilder.DropColumn(
                name: "plan",
                table: "users");

            migrationBuilder.DropColumn(
                name: "plan_expires_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "plan_note",
                table: "users");

            migrationBuilder.DropColumn(
                name: "plan_source",
                table: "users");

            migrationBuilder.DropColumn(
                name: "plan_updated_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "plan_updated_by",
                table: "users");
        }
    }
}
