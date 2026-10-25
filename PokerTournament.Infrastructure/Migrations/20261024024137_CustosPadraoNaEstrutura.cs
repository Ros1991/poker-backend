using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <summary>
    /// SPEC-20261023-1215 — custos padrão na estrutura de blinds: tabela blind_structure_default_costs (custos de
    /// valor fixo por torneio, apagados em cascata com a estrutura) e, em blind_structures, o custo de dealer
    /// (default_has_dealer, default_dealer_fee = valor por dealer). Os torneios recebem cópias na criação.
    /// Sem schema fixo: vale o padrão da conexão (public no banco próprio de produção).
    /// </summary>
    public partial class CustosPadraoNaEstrutura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "default_dealer_fee",
                table: "blind_structures",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "default_has_dealer",
                table: "blind_structures",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "blind_structure_default_costs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    blind_structure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    beneficiary = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    pix_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    pix_key_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blind_structure_default_costs", x => x.id);
                    table.ForeignKey(
                        name: "FK_blind_structure_default_costs_blind_structures_blind_struct~",
                        column: x => x.blind_structure_id,
                        principalTable: "blind_structures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_blind_structure_default_costs_structure",
                table: "blind_structure_default_costs",
                column: "blind_structure_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blind_structure_default_costs");

            migrationBuilder.DropColumn(
                name: "default_dealer_fee",
                table: "blind_structures");

            migrationBuilder.DropColumn(
                name: "default_has_dealer",
                table: "blind_structures");
        }
    }
}
