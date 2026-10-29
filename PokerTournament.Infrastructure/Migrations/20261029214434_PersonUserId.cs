using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <summary>
    /// SPEC-20261029-1725: jogador vinculado à conta pelo e-mail (<c>persons.user_id</c> → <c>users.id</c>, ON DELETE SET
    /// NULL, índice <c>ix_persons_user_id</c> — a área do jogador, SPEC-20261029-1726, lê por ele). Backfill em SQL puro,
    /// idempotente (só preenche quem ainda está sem conta; rodar de novo não muda nada) e em lote (roda no boot de
    /// produção pelo Migrate()), com a MESMA regra do <c>PlayerLinkService</c>:
    /// <list type="bullet">
    /// <item>jogador não excluído (<c>deleted_at IS NULL</c>), com e-mail, ainda sem conta;</item>
    /// <item>conta com o e-mail confirmado, ativa e não anonimizada (e-mail <c>@excluida.invalid</c>);</item>
    /// <item>e-mails comparados com <c>lower(btrim(..))</c> dos dois lados; duas contas antigas que só diferem por
    /// maiúsculas → vale a mais antiga (<c>DISTINCT ON</c> por <c>created_at</c>).</item>
    /// </list>
    /// Nada é fundido nem apagado; <c>updated_at</c> dos jogadores não muda (não é edição de ninguém).
    /// </summary>
    public partial class PersonUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "persons",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_persons_user_id",
                table: "persons",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_persons_users_user_id",
                table: "persons",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // Backfill: cada jogador com o e-mail de uma conta confirmada passa a ser dela
            migrationBuilder.Sql(
                "UPDATE persons p SET user_id = a.id" +
                " FROM (" +
                "  SELECT DISTINCT ON (lower(btrim(u.email))) u.id, lower(btrim(u.email)) AS email_key" +
                "  FROM users u" +
                "  WHERE u.email_confirmed_at IS NOT NULL" +
                "   AND u.is_active" +
                "   AND u.email NOT LIKE '%@excluida.invalid'" +
                "  ORDER BY lower(btrim(u.email)), u.created_at, u.id" +
                " ) a" +
                " WHERE p.user_id IS NULL" +
                "  AND p.deleted_at IS NULL" +
                "  AND p.email IS NOT NULL" +
                "  AND btrim(p.email) <> ''" +
                "  AND lower(btrim(p.email)) = a.email_key;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_persons_users_user_id",
                table: "persons");

            migrationBuilder.DropIndex(
                name: "ix_persons_user_id",
                table: "persons");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "persons");
        }
    }
}
