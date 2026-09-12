using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Web.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AjusteRedondeoEfectivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "RoundingAdjustment",
                table: "Sales",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RoundingAdjustment",
                table: "Sales");
        }
    }
}
