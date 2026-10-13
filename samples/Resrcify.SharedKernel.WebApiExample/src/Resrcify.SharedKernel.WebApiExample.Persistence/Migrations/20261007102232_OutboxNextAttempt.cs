using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resrcify.SharedKernel.WebApiExample.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxNextAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptOnUtc",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextAttemptOnUtc",
                table: "OutboxMessages");
        }
    }
}
