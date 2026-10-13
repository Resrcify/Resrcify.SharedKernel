using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resrcify.SharedKernel.WebApiExample.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeToSharedKernel4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DedupKey",
                table: "OutboxMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "OutboxMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<DateTime>(
                name: "DeletedOnUtc",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            // Before 4.0 a row that was never deleted held DateTime.MinValue, which Npgsql writes as -infinity
            // (or as 0001-01-01 with its infinity conversions off): it becomes NULL.
            migrationBuilder.Sql(
                """UPDATE "Companies" SET "DeletedOnUtc" = NULL WHERE "DeletedOnUtc" = '-infinity' OR "DeletedOnUtc" = '0001-01-01 00:00:00+00';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DedupKey",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "OutboxMessages");

            migrationBuilder.Sql(
                """UPDATE "Companies" SET "DeletedOnUtc" = '-infinity' WHERE "DeletedOnUtc" IS NULL;""");

            migrationBuilder.AlterColumn<DateTime>(
                name: "DeletedOnUtc",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
