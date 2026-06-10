using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffAndRankingContrib : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ranking_accrued_at",
                table: "tournaments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ranking_contrib_mode",
                table: "tournaments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ranking_contrib_value",
                table: "tournaments",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ranking_prize_accrued",
                table: "tournaments",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "staff_amount",
                table: "tournaments",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cost_type",
                table: "cost_extras",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Manual");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ranking_accrued_at",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "ranking_contrib_mode",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "ranking_contrib_value",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "ranking_prize_accrued",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "staff_amount",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "cost_type",
                table: "cost_extras");
        }
    }
}
