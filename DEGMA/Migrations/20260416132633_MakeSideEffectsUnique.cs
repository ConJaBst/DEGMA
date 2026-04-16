using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DEGMA.Migrations
{
    /// <inheritdoc />
    public partial class MakeSideEffectsUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SideEffects_Name",
                table: "SideEffects",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SideEffects_Name",
                table: "SideEffects");
        }
    }
}
