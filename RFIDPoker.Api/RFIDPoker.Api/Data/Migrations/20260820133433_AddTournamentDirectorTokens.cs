using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RFIDPoker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentDirectorTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TournamentDirectorTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsRevoked = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentDirectorTokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentDirectorTokens_TokenHash",
                table: "TournamentDirectorTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TournamentDirectorTokens");
        }
    }
}
