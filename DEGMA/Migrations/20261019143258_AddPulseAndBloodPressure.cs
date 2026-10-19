using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DEGMA.Migrations
{
    /// <inheritdoc />
    public partial class AddPulseAndBloodPressure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiastolicBp",
                table: "Reports",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Pulse",
                table: "Reports",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SystolicBp",
                table: "Reports",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiastolicBp",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "Pulse",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "SystolicBp",
                table: "Reports");
        }
    }
}
