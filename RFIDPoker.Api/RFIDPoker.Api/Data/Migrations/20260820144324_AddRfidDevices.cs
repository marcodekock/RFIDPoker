using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RFIDPoker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRfidDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RfidDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    WebSocketUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RfidDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RfidAntennas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<int>(type: "INTEGER", nullable: false),
                    AntennaIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Function = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SeatNumber = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RfidAntennas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RfidAntennas_RfidDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "RfidDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RfidAntennas_DeviceId_AntennaIndex",
                table: "RfidAntennas",
                columns: new[] { "DeviceId", "AntennaIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RfidAntennas");

            migrationBuilder.DropTable(
                name: "RfidDevices");
        }
    }
}
