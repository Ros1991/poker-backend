using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <summary>
    /// SPEC-20261023-1209: confirmação de e-mail no cadastro. Adiciona users.email_confirmed_at
    /// (null = login bloqueado) e confirma TODAS as contas que já existiam com a data de criação
    /// (D16.3 — os "fundadores" continuam entrando normalmente). Cria email_confirmation_tokens
    /// (mesmas colunas de password_reset_tokens: só o hash SHA-256 do token, uso único, 48 h).
    /// Sem schema fixo: vale o padrão da conexão (public no banco próprio de produção).
    /// </summary>
    public partial class ConfirmacaoDeEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_confirmed_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            // D16.3: quem já tinha conta antes desta SPEC fica confirmado (idempotente)
            migrationBuilder.Sql("""
                UPDATE users
                SET email_confirmed_at = created_at
                WHERE email_confirmed_at IS NULL;
                """);

            migrationBuilder.CreateTable(
                name: "email_confirmation_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    requested_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_confirmation_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_email_confirmation_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_confirmation_tokens_token_hash",
                table: "email_confirmation_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_confirmation_tokens_user_id",
                table: "email_confirmation_tokens",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_confirmation_tokens");

            migrationBuilder.DropColumn(
                name: "email_confirmed_at",
                table: "users");
        }
    }
}
