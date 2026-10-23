using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <summary>
    /// E-mails de conta passam a ser gravados em minúsculas (AuthService / UsersController) e
    /// comparados sem distinguir maiúsculas. Aqui: (1) baixa a caixa de quem NÃO colide com outra
    /// conta; (2) índice único em lower(email) só se não houver colisão legada. Contas que diferem
    /// só por maiúsculas NÃO são apagadas nem renomeadas — fica um RAISE NOTICE para resolver à mão.
    /// Idempotente: rodar de novo não muda nada.
    /// </summary>
    public partial class EmailsMinusculos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE users u
                SET email = lower(u.email)
                WHERE u.email <> lower(u.email)
                  AND NOT EXISTS (
                      SELECT 1 FROM users o
                      WHERE o.id <> u.id AND lower(o.email) = lower(u.email)
                  );
                """);

            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    colisoes integer;
                BEGIN
                    SELECT count(*) INTO colisoes
                    FROM (SELECT lower(email) FROM users GROUP BY lower(email) HAVING count(*) > 1) c;

                    IF colisoes > 0 THEN
                        RAISE NOTICE 'ix_users_email_lower NÃO criado: % e-mail(s) com contas que diferem só por maiúsculas. Resolva à mão e rode: CREATE UNIQUE INDEX ix_users_email_lower ON users (lower(email));', colisoes;
                    ELSE
                        CREATE UNIQUE INDEX IF NOT EXISTS ix_users_email_lower ON users (lower(email));
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_users_email_lower;");
        }
    }
}
