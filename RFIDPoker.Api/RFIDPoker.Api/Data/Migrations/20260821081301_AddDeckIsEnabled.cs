using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RFIDPoker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeckIsEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "Decks",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("UPDATE \"Decks\" SET \"IsEnabled\" = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsEnabled",
                table: "Decks");
        }
    }
}
