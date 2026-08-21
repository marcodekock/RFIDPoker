using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RFIDPoker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_CardMappings",
                table: "CardMappings");

            migrationBuilder.AddColumn<int>(
                name: "DeckId",
                table: "CardMappings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_CardMappings",
                table: "CardMappings",
                columns: new[] { "DeckId", "TagId" });

            migrationBuilder.CreateTable(
                name: "Decks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Decks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Decks_Name",
                table: "Decks",
                column: "Name",
                unique: true);

            // Insert a default deck and backfill existing mappings before enforcing the FK.
            migrationBuilder.Sql("INSERT INTO \"Decks\" (\"Name\") VALUES ('Default Deck');");
            migrationBuilder.Sql(
                "UPDATE \"CardMappings\" SET \"DeckId\" = (SELECT \"Id\" FROM \"Decks\" ORDER BY \"Id\" LIMIT 1) WHERE \"DeckId\" = 0;");

            migrationBuilder.AddForeignKey(
                name: "FK_CardMappings_Decks_DeckId",
                table: "CardMappings",
                column: "DeckId",
                principalTable: "Decks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CardMappings_Decks_DeckId",
                table: "CardMappings");

            migrationBuilder.DropTable(
                name: "Decks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CardMappings",
                table: "CardMappings");

            migrationBuilder.DropColumn(
                name: "DeckId",
                table: "CardMappings");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CardMappings",
                table: "CardMappings",
                column: "TagId");
        }
    }
}
