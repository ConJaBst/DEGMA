using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DEGMA.Migrations
{
    /// <inheritdoc />
    public partial class AddGoalWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "GoalWeight",
                table: "Patients",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoalWeight",
                table: "Patients");
        }
    }
}
