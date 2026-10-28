using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <summary>
    /// SPEC-20261027-1311: o "addon duplo" sai e entra o máximo de add-ons por jogador (0 = ilimitado, padrão 1).
    /// <list type="bullet">
    /// <item>Inscrição: addon_purchased/addon_double → addon_count (duplo = 2, simples = 1, sem add-on = 0); addon_amount
    /// (o total em dinheiro) não muda.</item>
    /// <item>Torneio: addon_double_allowed → max_addons (duplo permitido = 2; senão 1; "Permitir add-on" desmarcado
    /// continua desmarcado, com o máximo guardado 1). total_addons passa a contar unidades: recalculado pela soma de
    /// addon_count das inscrições (torneio sem inscrição fica como está).</item>
    /// <item>Estrutura: default_addon_double_allowed → default_max_addons (mesma regra) e default_addon_allowed sem valor
    /// padrão no banco (com ele, o EF não gravava o false).</item>
    /// </list>
    /// Nenhum valor em dinheiro muda (prize pool, devido, pago, saldo, custos, lançamentos).
    /// </summary>
    public partial class AddonMaximo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_addons",
                table: "tournaments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "addon_count",
                table: "tournament_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "default_max_addons",
                table: "blind_structures",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Backfill antes de apagar as colunas antigas
            migrationBuilder.Sql(
                "UPDATE tournament_entries SET addon_count = CASE WHEN addon_purchased " +
                "THEN (CASE WHEN addon_double THEN 2 ELSE 1 END) ELSE 0 END;");
            migrationBuilder.Sql(
                "UPDATE tournaments SET max_addons = CASE WHEN addon_double_allowed THEN 2 ELSE 1 END;");
            migrationBuilder.Sql(
                "UPDATE blind_structures SET default_max_addons = CASE WHEN default_addon_double_allowed THEN 2 ELSE 1 END;");
            migrationBuilder.Sql(
                "UPDATE tournaments t SET total_addons = s.units " +
                "FROM (SELECT tournament_id, SUM(addon_count) AS units FROM tournament_entries GROUP BY tournament_id) s " +
                "WHERE s.tournament_id = t.id AND t.total_addons <> s.units;");

            migrationBuilder.DropColumn(
                name: "addon_double_allowed",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "addon_double",
                table: "tournament_entries");

            migrationBuilder.DropColumn(
                name: "addon_purchased",
                table: "tournament_entries");

            migrationBuilder.DropColumn(
                name: "default_addon_double_allowed",
                table: "blind_structures");

            migrationBuilder.AlterColumn<bool>(
                name: "default_addon_allowed",
                table: "blind_structures",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "addon_double_allowed",
                table: "tournaments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "addon_double",
                table: "tournament_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "addon_purchased",
                table: "tournament_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "default_addon_double_allowed",
                table: "blind_structures",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Volta aproximada: 1 add-on = simples, 2 ou mais = duplo; máximo 2+ ou ilimitado = duplo permitido
            migrationBuilder.Sql(
                "UPDATE tournament_entries SET addon_purchased = addon_count > 0, addon_double = addon_count >= 2;");
            migrationBuilder.Sql(
                "UPDATE tournaments SET addon_double_allowed = (max_addons = 0 OR max_addons >= 2);");
            migrationBuilder.Sql(
                "UPDATE blind_structures SET default_addon_double_allowed = (default_max_addons = 0 OR default_max_addons >= 2);");
            migrationBuilder.Sql(
                "UPDATE tournaments t SET total_addons = s.n " +
                "FROM (SELECT tournament_id, COUNT(*) FILTER (WHERE addon_count > 0) AS n FROM tournament_entries GROUP BY tournament_id) s " +
                "WHERE s.tournament_id = t.id;");

            migrationBuilder.DropColumn(
                name: "max_addons",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "addon_count",
                table: "tournament_entries");

            migrationBuilder.DropColumn(
                name: "default_max_addons",
                table: "blind_structures");

            migrationBuilder.AlterColumn<bool>(
                name: "default_addon_allowed",
                table: "blind_structures",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");
        }
    }
}
