using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <summary>
    /// Revisão da SPEC-20261023-1209 + SPEC-20261023-1205 (uma migration só, depois da LoginComGoogle):
    /// <list type="bullet">
    /// <item>D16.10 — o cadastro fica preso ao link: email_confirmation_tokens ganha pending_full_name,
    /// pending_nickname, pending_whatsapp e pending_password_hash (só vão para a conta quando AQUELE token é
    /// confirmado com a senha certa) e email_sent (o cooldown de 2 min conta só os links que saíram por e-mail;
    /// as linhas antigas foram todas enviadas → true). Tokens antigos ficam sem cadastro preso e confirmam com a
    /// senha da conta.</item>
    /// <item>Limite semanal do Mesa — tournaments.had_entries: gravado na primeira inscrição e nunca limpo (só o
    /// rascunho excluído que NUNCA teve inscrição devolve a vaga). Preenchido para quem já tem inscrição, contador
    /// ou atividade.</item>
    /// </list>
    /// Sem schema fixo: vale o padrão da conexão (public no banco próprio de produção).
    /// </summary>
    public partial class CadastroPendenteETorneioComInscricao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "had_entries",
                table: "tournaments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE tournaments t SET had_entries = TRUE
                WHERE t.total_entries > 0
                   OR EXISTS (SELECT 1 FROM tournament_entries e WHERE e.tournament_id = t.id)
                   OR EXISTS (SELECT 1 FROM tournament_activities a WHERE a.tournament_id = t.id);
                """);

            migrationBuilder.AddColumn<bool>(
                name: "email_sent",
                table: "email_confirmation_tokens",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_full_name",
                table: "email_confirmation_tokens",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_nickname",
                table: "email_confirmation_tokens",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_password_hash",
                table: "email_confirmation_tokens",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_whatsapp",
                table: "email_confirmation_tokens",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "had_entries",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "email_sent",
                table: "email_confirmation_tokens");

            migrationBuilder.DropColumn(
                name: "pending_full_name",
                table: "email_confirmation_tokens");

            migrationBuilder.DropColumn(
                name: "pending_nickname",
                table: "email_confirmation_tokens");

            migrationBuilder.DropColumn(
                name: "pending_password_hash",
                table: "email_confirmation_tokens");

            migrationBuilder.DropColumn(
                name: "pending_whatsapp",
                table: "email_confirmation_tokens");
        }
    }
}
