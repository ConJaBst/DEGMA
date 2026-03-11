using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DEGMA.Migrations
{
    /// <inheritdoc />
    public partial class FixReportForeignKeyCasing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reports_Patients_patientId",
                table: "Reports");

            migrationBuilder.RenameColumn(
                name: "patientId",
                table: "Reports",
                newName: "PatientId");

            migrationBuilder.RenameIndex(
                name: "IX_Reports_patientId",
                table: "Reports",
                newName: "IX_Reports_PatientId");

            migrationBuilder.AddForeignKey(
                name: "FK_Reports_Patients_PatientId",
                table: "Reports",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "PatientId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reports_Patients_PatientId",
                table: "Reports");

            migrationBuilder.RenameColumn(
                name: "PatientId",
                table: "Reports",
                newName: "patientId");

            migrationBuilder.RenameIndex(
                name: "IX_Reports_PatientId",
                table: "Reports",
                newName: "IX_Reports_patientId");

            migrationBuilder.AddForeignKey(
                name: "FK_Reports_Patients_patientId",
                table: "Reports",
                column: "patientId",
                principalTable: "Patients",
                principalColumn: "PatientId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
