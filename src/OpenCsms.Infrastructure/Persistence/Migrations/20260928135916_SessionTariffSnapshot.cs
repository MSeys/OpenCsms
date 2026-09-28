using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCsms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SessionTariffSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TariffCurrency",
                table: "Sessions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TariffEnergyPricePerKwh",
                table: "Sessions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TariffIdleFeePerHour",
                table: "Sessions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "TariffIdleGracePeriod",
                table: "Sessions",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TariffStartFee",
                table: "Sessions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            // Sessions stored before the snapshot existed bill the tariff their station points at
            // today; that is the best available reading and it happens once, on the way to required
            // columns.
            migrationBuilder.Sql(
                """
                UPDATE "Sessions" AS s
                SET "TariffEnergyPricePerKwh" = t."EnergyPricePerKwh",
                    "TariffStartFee" = t."StartFee",
                    "TariffIdleFeePerHour" = t."IdleFeePerHour",
                    "TariffIdleGracePeriod" = t."IdleGracePeriod",
                    "TariffCurrency" = t."Currency"
                FROM "Stations" AS st
                JOIN "Tariffs" AS t ON t."Id" = st."TariffId"
                WHERE s."StationId" = st."Id";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "TariffCurrency",
                table: "Sessions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "TariffEnergyPricePerKwh",
                table: "Sessions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "TariffIdleFeePerHour",
                table: "Sessions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<TimeSpan>(
                name: "TariffIdleGracePeriod",
                table: "Sessions",
                type: "interval",
                nullable: false,
                oldClrType: typeof(TimeSpan),
                oldType: "interval",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "TariffStartFee",
                table: "Sessions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TariffCurrency",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TariffEnergyPricePerKwh",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TariffIdleFeePerHour",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TariffIdleGracePeriod",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TariffStartFee",
                table: "Sessions");
        }
    }
}
