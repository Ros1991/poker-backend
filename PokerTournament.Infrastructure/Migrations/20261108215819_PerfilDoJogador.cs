using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <summary>
    /// Perfil público do jogador e privacidade (SPEC-20261108-1636): handle único da URL, cidade/UF, bio e os quatro
    /// controles de privacidade em <c>users</c>. O handle nasce nulo, o backfill em PL/pgSQL dá um a cada conta
    /// existente (idempotente: só <c>handle IS NULL</c>) com a MESMA regra do <c>HandleGenerator</c> — apelido, senão
    /// primeiro nome; minúsculas sem acento; tudo que não é letra/número vira hífen; vazio → "jogador"; curto ou
    /// reservado → "jogador-&lt;base&gt;"; colisão → "-2", "-3"…; conta anonimizada (<c>…@excluida.invalid</c>) →
    /// "excluida-&lt;12 hex do id&gt;" — e só então a coluna vira NOT NULL com o índice único.
    /// </summary>
    public partial class PerfilDoJogador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bio",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "city",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "friend_requests_from",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "PlayedWith");

            // Nasce nula: o backfill abaixo preenche e depois a coluna vira NOT NULL
            migrationBuilder.AddColumn<string>(
                name: "handle",
                table: "users",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "messages_from",
                table: "users",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Friends");

            migrationBuilder.AddColumn<string>(
                name: "profile_visibility",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Everyone");

            migrationBuilder.AddColumn<bool>(
                name: "show_in_played_with",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "state",
                table: "users",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            // Backfill: um handle para cada conta sem handle (idempotente), as mais antigas primeiro (ficam com a base limpa)
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    r RECORD;
                    base text;
                    candidate text;
                    n integer;
                    reserved text[] := ARRAY['admin', 'administrador', 'suporte', 'poker', 'pokerdagalera', 'galera', 'api',
                        'me', 'eu', 'perfil', 'login', 'entrar', 'cadastro', 'sistema', 'root', 'ajuda', 'contato', 'termos',
                        'privacidade', 'manual', 'inicio', 'tv', 'dealer', 'amigos', 'mensagens', 'anonimo', 'convidado',
                        'null', 'undefined'];
                BEGIN
                    FOR r IN
                        SELECT id, nickname, full_name, email
                        FROM users
                        WHERE handle IS NULL OR handle = ''
                        ORDER BY created_at, id
                    LOOP
                        IF r.email LIKE '%@excluida.invalid' THEN
                            base := 'excluida-' || left(replace(r.id::text, '-', ''), 12);
                        ELSE
                            base := coalesce(nullif(btrim(r.nickname), ''), split_part(btrim(coalesce(r.full_name, '')), ' ', 1));
                            base := translate(lower(base),
                                'áàâãäåéèêëíìîïóòôõöúùûüçñýÿ',
                                'aaaaaaeeeeiiiiooooouuuucnyy');
                            base := btrim(regexp_replace(base, '[^a-z0-9]+', '-', 'g'), '-');
                            IF base = '' THEN
                                base := 'jogador';
                            ELSIF length(base) < 3 OR base = ANY (reserved) THEN
                                base := 'jogador-' || base;
                            END IF;
                            base := rtrim(left(base, 30), '-');
                        END IF;

                        candidate := base;
                        n := 1;
                        WHILE EXISTS (SELECT 1 FROM users u WHERE u.handle = candidate AND u.id <> r.id) LOOP
                            n := n + 1;
                            candidate := rtrim(left(base, 30 - length('-' || n::text)), '-') || '-' || n::text;
                        END LOOP;

                        UPDATE users SET handle = candidate WHERE id = r.id;
                    END LOOP;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "handle",
                table: "users",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_handle",
                table: "users",
                column: "handle",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_handle",
                table: "users");

            migrationBuilder.DropColumn(
                name: "bio",
                table: "users");

            migrationBuilder.DropColumn(
                name: "city",
                table: "users");

            migrationBuilder.DropColumn(
                name: "friend_requests_from",
                table: "users");

            migrationBuilder.DropColumn(
                name: "handle",
                table: "users");

            migrationBuilder.DropColumn(
                name: "messages_from",
                table: "users");

            migrationBuilder.DropColumn(
                name: "profile_visibility",
                table: "users");

            migrationBuilder.DropColumn(
                name: "show_in_played_with",
                table: "users");

            migrationBuilder.DropColumn(
                name: "state",
                table: "users");
        }
    }
}
