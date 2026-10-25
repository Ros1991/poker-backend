using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <summary>
    /// Nome de home game único POR DONO (revisão geral do programa, ondas 7–9): troca o índice único do banco inteiro
    /// <c>ix_home_games_name</c> (name) por <c>ix_home_games_owner_name</c> (owner_id, name), os dois parciais
    /// (deleted_at IS NULL). Donos diferentes podem repetir o nome; o mesmo dono repetindo recebe 400 na API. Os dados
    /// existentes já cumprem o índice novo (o antigo era mais restrito).
    /// </summary>
    public partial class HomeGameNomeUnicoPorDono : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_home_games_name",
                table: "home_games");

            migrationBuilder.CreateIndex(
                name: "ix_home_games_owner_name",
                table: "home_games",
                columns: new[] { "owner_id", "name" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_home_games_owner_name",
                table: "home_games");

            migrationBuilder.CreateIndex(
                name: "ix_home_games_name",
                table: "home_games",
                column: "name",
                unique: true,
                filter: "deleted_at IS NULL");
        }
    }
}
