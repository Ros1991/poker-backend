using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TimebankVirtual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Timebank virtual (SPEC-20261029-1722). Backfill pelos próprios padrões das colunas (idempotente): torneios e
            // estruturas que já existem ficam sem timebank (null) e as inscrições com 0 timebanks — nada muda nas telas.
            // As atividades antigas ficam sem delta e sem desfazer (só as de timebank se desfazem).
            migrationBuilder.AddColumn<int>(
                name: "timebank_count",
                table: "tournaments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "timebank_seconds",
                table: "tournaments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "timebanks_remaining",
                table: "tournament_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "delta",
                table: "tournament_activities",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "undone_at",
                table: "tournament_activities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "undone_by",
                table: "tournament_activities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "default_timebank_count",
                table: "blind_structures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "default_timebank_seconds",
                table: "blind_structures",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "timebank_count",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "timebank_seconds",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "timebanks_remaining",
                table: "tournament_entries");

            migrationBuilder.DropColumn(
                name: "delta",
                table: "tournament_activities");

            migrationBuilder.DropColumn(
                name: "undone_at",
                table: "tournament_activities");

            migrationBuilder.DropColumn(
                name: "undone_by",
                table: "tournament_activities");

            migrationBuilder.DropColumn(
                name: "default_timebank_count",
                table: "blind_structures");

            migrationBuilder.DropColumn(
                name: "default_timebank_seconds",
                table: "blind_structures");
        }
    }
}
