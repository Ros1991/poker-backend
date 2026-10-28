using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <summary>
    /// SPEC-20261027-1312 — IA na administração: ai_settings (linha única: liga/desliga, modelo padrão, limite mensal, chave
    /// da Anthropic cifrada com AES-256-GCM e o último teste), ai_model_prices (US$ por milhão de tokens, editáveis),
    /// ai_features (liga/desliga por recurso) e ai_usages (registro de cada chamada/uso, SEM o conteúdo). Sementes: IA ligada
    /// com Claude Opus 5.5 e limite US$ 0 (nenhum uso pago até o Admin definir), "Tirar o fundo da foto" ligado e os preços
    /// oficiais conferidos em 2026-10-27. A subida da API (AiAdminService.EnsureCatalogAsync) faz o mesmo de forma idempotente.
    /// Sem schema fixo: vale o padrão da conexão (public no banco próprio de produção).
    /// </summary>
    public partial class IaFundacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_features",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false),
                    runs_on_device = table.Column<bool>(type: "boolean", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_features", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_model_prices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    input_per_mtok = table.Column<decimal>(type: "numeric(12,4)", nullable: false),
                    output_per_mtok = table.Column<decimal>(type: "numeric(12,4)", nullable: false),
                    cache_write_per_mtok = table.Column<decimal>(type: "numeric(12,4)", nullable: false),
                    cache_read_per_mtok = table.Column<decimal>(type: "numeric(12,4)", nullable: false),
                    long_context_threshold_tokens = table.Column<int>(type: "integer", nullable: true),
                    long_input_per_mtok = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                    long_output_per_mtok = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                    long_cache_write_per_mtok = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                    long_cache_read_per_mtok = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_model_prices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    default_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    monthly_limit_usd = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    api_key_encrypted = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    api_key_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    api_key_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    api_key_updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    last_test_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_test_ok = table.Column<bool>(type: "boolean", nullable: true),
                    last_test_message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    last_test_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_usages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    on_device = table.Column<bool>(type: "boolean", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    home_game_id = table.Column<Guid>(type: "uuid", nullable: true),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cache_creation_input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cache_read_input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(14,6)", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    error_message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_usages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_features_key",
                table: "ai_features",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_model_prices_model",
                table: "ai_model_prices",
                column: "model",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_usages_created_at",
                table: "ai_usages",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_ai_usages_feature_created_at",
                table: "ai_usages",
                columns: new[] { "feature_key", "created_at" });

            // Sementes (idempotentes: a subida da API faz o mesmo e não duplica — PK da linha única e índices únicos em key/model)
            migrationBuilder.Sql(
                "INSERT INTO ai_settings (id, enabled, default_model, monthly_limit_usd, created_at, updated_at) " +
                "VALUES ('6a1e5e77-1312-4c1a-9d5e-000000000001', TRUE, 'claude-opus-5-5', 0, now(), now()) ON CONFLICT (id) DO NOTHING;");
            migrationBuilder.Sql(
                "INSERT INTO ai_features (id, key, name, description, is_paid, runs_on_device, enabled, sort_order, created_at, updated_at) " +
                "VALUES ('6a1e5e77-1312-4c1a-9d5e-000000000101', 'tirar-o-fundo', 'Tirar o fundo da foto', " +
                "'No próprio aparelho, sem custo: recorta a pessoa e põe um fundo liso claro na foto do jogador.', FALSE, TRUE, TRUE, 10, now(), now()) " +
                "ON CONFLICT (key) DO NOTHING;");
            migrationBuilder.Sql(
                "INSERT INTO ai_model_prices (id, model, display_name, input_per_mtok, output_per_mtok, cache_write_per_mtok, cache_read_per_mtok, " +
                "long_context_threshold_tokens, long_input_per_mtok, long_output_per_mtok, long_cache_write_per_mtok, long_cache_read_per_mtok, " +
                "sort_order, created_at, updated_at) VALUES " +
                "('6a1e5e77-1312-4c1a-9d5e-000000000201', 'claude-opus-5-5', 'Claude Opus 5.5', 4, 20, 5, 0.20, NULL, NULL, NULL, NULL, NULL, 10, now(), now()), " +
                "('6a1e5e77-1312-4c1a-9d5e-000000000202', 'claude-sonnet-5-5', 'Claude Sonnet 5.5', 2, 10, 2.50, 0.10, NULL, NULL, NULL, NULL, NULL, 20, now(), now()), " +
                "('6a1e5e77-1312-4c1a-9d5e-000000000203', 'claude-haiku-5-5', 'Claude Haiku 5.5', 0.10, 0.50, 0.125, 0.01, 100000, 0.50, 2.50, 0.625, 0.05, 30, now(), now()) " +
                "ON CONFLICT (model) DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_features");

            migrationBuilder.DropTable(
                name: "ai_model_prices");

            migrationBuilder.DropTable(
                name: "ai_settings");

            migrationBuilder.DropTable(
                name: "ai_usages");
        }
    }
}
