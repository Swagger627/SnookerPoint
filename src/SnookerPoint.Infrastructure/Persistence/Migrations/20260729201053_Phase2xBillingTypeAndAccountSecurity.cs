using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnookerPoint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2xBillingTypeAndAccountSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryCodeHash",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecoveryCodeSetUtc",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecoveryFailedAttempts",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecoveryLockedUntilUtc",
                table: "Users",
                type: "TEXT",
                nullable: true);

            // Default existing rows to "Hourly" so pre-Phase-2x sessions remain valid
            // (an empty string would fail enum parsing on read).
            migrationBuilder.AddColumn<string>(
                name: "BillingType",
                table: "TableSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "Hourly");

            migrationBuilder.AddColumn<long>(
                name: "FixedAmount",
                table: "TableSessions",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RecoveryCodeHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RecoveryCodeSetUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RecoveryFailedAttempts",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RecoveryLockedUntilUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BillingType",
                table: "TableSessions");

            migrationBuilder.DropColumn(
                name: "FixedAmount",
                table: "TableSessions");
        }
    }
}
