using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <summary>
    /// SPEC-20261029-1723: home game principal da conta (<c>home_games.is_primary</c>, um por dono entre os não excluídos
    /// — índice único parcial <c>ix_home_games_owner_primary</c>). Backfill em SQL puro, idempotente (rodar de novo não
    /// muda nada) e em lote (roda no boot de produção pelo Migrate()):
    /// <list type="number">
    /// <item>Marca: conta dona de home games não excluídos e ainda sem principal → o principal é o que tem MAIS torneios
    /// (não cancelados, não excluídos); empate → o ativo, depois o mais antigo (decisão do orquestrador em 2026-10-29: em
    /// produção o "Poker Clandestino", com dezenas de torneios, e não o "Home game de testes" criado depois).</item>
    /// <item>Cria: conta confirmada, não excluída (e-mail @excluida.invalid) e sem principal → home game novo
    /// "Clube de &lt;nome da conta&gt;" (a mesma regra de <c>HomeGamePrimary.DefaultName</c>; "Meu clube" sem nome). Conta
    /// ainda não confirmada ganha o principal ao confirmar.</item>
    /// </list>
    /// Nada é apagado: contas donas de 2+ home games continuam com todos (os extras seguem visíveis e operáveis).
    /// <para>
    /// Down (revisão independente da SPEC-20261029-1723): antes de apagar a coluna, apaga de vez (SQL) os principais
    /// vazios — sem torneio, jogador, membro, ranking, estrutura de blinds nem regra de pontuação, contando também os
    /// excluídos logicamente —, que são os "Clube de X" criados pelo backfill (e pelas contas confirmadas depois). Sem a
    /// coluna eles virariam home games comuns que ninguém criou. Principal com qualquer dado fica.
    /// </para>
    /// </summary>
    public partial class HomeGamePrincipal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_primary",
                table: "home_games",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_home_games_owner_primary",
                table: "home_games",
                column: "owner_id",
                unique: true,
                filter: "is_primary AND deleted_at IS NULL");

            // 1. Marca o principal de quem já é dono de home game (o mais usado; empate: ativo, mais antigo)
            migrationBuilder.Sql(
                "WITH ranked AS (" +
                " SELECT h.id, row_number() OVER (PARTITION BY h.owner_id ORDER BY" +
                "  (SELECT count(*) FROM tournaments t WHERE t.home_game_id = h.id" +
                "    AND t.deleted_at IS NULL AND t.status <> 'Cancelled') DESC," +
                "  h.is_active DESC, h.created_at, h.id) AS rn" +
                " FROM home_games h" +
                " WHERE h.deleted_at IS NULL" +
                "  AND NOT EXISTS (SELECT 1 FROM home_games p" +
                "   WHERE p.owner_id = h.owner_id AND p.is_primary AND p.deleted_at IS NULL))" +
                " UPDATE home_games h SET is_primary = TRUE" +
                " FROM ranked r WHERE r.id = h.id AND r.rn = 1;");

            // 2. Cria o principal de quem não é dono de nenhum (conta confirmada e não excluída)
            migrationBuilder.Sql(
                "INSERT INTO home_games (id, owner_id, name, timezone, is_active, is_primary, created_at, updated_at)" +
                " SELECT gen_random_uuid(), u.id," +
                "  CASE WHEN btrim(coalesce(u.full_name, '')) = '' THEN 'Meu clube'" +
                "   ELSE 'Clube de ' || left(btrim(u.full_name), 191) END," +
                "  'America/Sao_Paulo', TRUE, TRUE, now(), now()" +
                " FROM users u" +
                " WHERE u.email_confirmed_at IS NOT NULL" +
                "  AND u.email NOT LIKE '%@excluida.invalid'" +
                "  AND NOT EXISTS (SELECT 1 FROM home_games h" +
                "   WHERE h.owner_id = u.id AND h.is_primary AND h.deleted_at IS NULL);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Principais vazios saem de vez (o SQL não tem o filtro de soft delete: excluídos logicamente contam como dado)
            migrationBuilder.Sql(
                "DELETE FROM home_games h" +
                " WHERE h.is_primary" +
                "  AND NOT EXISTS (SELECT 1 FROM tournaments t WHERE t.home_game_id = h.id)" +
                "  AND NOT EXISTS (SELECT 1 FROM persons p WHERE p.home_game_id = h.id)" +
                "  AND NOT EXISTS (SELECT 1 FROM home_game_members m WHERE m.home_game_id = h.id)" +
                "  AND NOT EXISTS (SELECT 1 FROM rankings r WHERE r.home_game_id = h.id)" +
                "  AND NOT EXISTS (SELECT 1 FROM blind_structures b WHERE b.home_game_id = h.id)" +
                "  AND NOT EXISTS (SELECT 1 FROM scoring_rules s WHERE s.home_game_id = h.id);");

            migrationBuilder.DropIndex(
                name: "ix_home_games_owner_primary",
                table: "home_games");

            migrationBuilder.DropColumn(
                name: "is_primary",
                table: "home_games");
        }
    }
}
