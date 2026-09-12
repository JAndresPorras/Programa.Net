using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Web.Data.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AjusteRedondeoEfectivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RoundingAdjustment",
                table: "Sales",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);
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
