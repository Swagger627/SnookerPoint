using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnookerPoint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6ReportsAndAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoBackupDaily",
                table: "ClubSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoBackupEnabled",
                table: "ClubSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoBackupOnClose",
                table: "ClubSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AutoBackupRetention",
                table: "ClubSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAutoBackupUtc",
                table: "ClubSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ServiceChargeEnabled",
                table: "ClubSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ServiceChargePercent",
                table: "ClubSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "TaxEnabled",
                table: "ClubSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxPercent",
                table: "ClubSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoBackupDaily",
                table: "ClubSettings");

            migrationBuilder.DropColumn(
                name: "AutoBackupEnabled",
                table: "ClubSettings");

            migrationBuilder.DropColumn(
                name: "AutoBackupOnClose",
                table: "ClubSettings");

            migrationBuilder.DropColumn(
                name: "AutoBackupRetention",
                table: "ClubSettings");

            migrationBuilder.DropColumn(
                name: "LastAutoBackupUtc",
                table: "ClubSettings");

            migrationBuilder.DropColumn(
                name: "ServiceChargeEnabled",
                table: "ClubSettings");

            migrationBuilder.DropColumn(
                name: "ServiceChargePercent",
                table: "ClubSettings");

            migrationBuilder.DropColumn(
                name: "TaxEnabled",
                table: "ClubSettings");

            migrationBuilder.DropColumn(
                name: "TaxPercent",
                table: "ClubSettings");
        }
    }
}
