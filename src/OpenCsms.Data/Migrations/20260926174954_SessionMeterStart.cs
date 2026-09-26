using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCsms.Data.Migrations
{
    /// <inheritdoc />
    public partial class SessionMeterStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MeterStartKwh",
                table: "Sessions",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MeterStartKwh",
                table: "Sessions");
        }
    }
}
