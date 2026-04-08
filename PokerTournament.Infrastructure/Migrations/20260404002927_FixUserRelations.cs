using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerTournament.Infrastructure.Migrations
{
    public partial class FixUserRelations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op: shadow property UserId1 was never in the database
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
