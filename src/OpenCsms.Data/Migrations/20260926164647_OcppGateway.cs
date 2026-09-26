using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenCsms.Data.Migrations
{
    /// <inheritdoc />
    public partial class OcppGateway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChargePointId",
                table: "Stations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Stations registered before OCPP existed have no charge point identity yet; the row's own
            // id keeps the new unique index valid until an operator registers the real identity.
            migrationBuilder.Sql("UPDATE \"Stations\" SET \"ChargePointId\" = \"Id\"::text WHERE \"ChargePointId\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "ChargePointId",
                table: "Stations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSeenAtUtc",
                table: "Stations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TransactionId",
                table: "Sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn);

            migrationBuilder.CreateTable(
                name: "Connectors",
                columns: table => new
                {
                    StationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectorId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connectors", x => new { x.StationId, x.ConnectorId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stations_ChargePointId",
                table: "Stations",
                column: "ChargePointId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_TransactionId",
                table: "Sessions",
                column: "TransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Connectors");

            migrationBuilder.DropIndex(
                name: "IX_Stations_ChargePointId",
                table: "Stations");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_TransactionId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ChargePointId",
                table: "Stations");

            migrationBuilder.DropColumn(
                name: "LastSeenAtUtc",
                table: "Stations");

            migrationBuilder.DropColumn(
                name: "TransactionId",
                table: "Sessions");
        }
    }
}
