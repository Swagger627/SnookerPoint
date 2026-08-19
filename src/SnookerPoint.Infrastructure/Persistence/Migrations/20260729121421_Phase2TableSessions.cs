using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SnookerPoint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2TableSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Method = table.Column<string>(type: "TEXT", nullable: false),
                    RoundingIncrementMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumBillableMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    GracePeriodMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TableSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CheckoutStatus = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentTableId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FinishUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CustomerLabel = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    ClosingNote = table.Column<string>(type: "TEXT", nullable: true),
                    BillingMethod = table.Column<string>(type: "TEXT", nullable: false),
                    RoundingIncrementMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumBillableMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    GracePeriodMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    OpenedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    OpenedShiftId = table.Column<int>(type: "INTEGER", nullable: false),
                    FinishedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    FinishedShiftId = table.Column<int>(type: "INTEGER", nullable: true),
                    FinalCharge = table.Column<long>(type: "INTEGER", nullable: true),
                    FinalBillableSeconds = table.Column<long>(type: "INTEGER", nullable: true),
                    VoidReason = table.Column<string>(type: "TEXT", nullable: true),
                    VoidedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    VoidedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TableSessions_PoolTables_CurrentTableId",
                        column: x => x.CurrentTableId,
                        principalTable: "PoolTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TableSessions_Shifts_OpenedShiftId",
                        column: x => x.OpenedShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TableSessions_Users_OpenedByUserId",
                        column: x => x.OpenedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionAdjustments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    OldValue = table.Column<string>(type: "TEXT", nullable: true),
                    NewValue = table.Column<string>(type: "TEXT", nullable: true),
                    Amount = table.Column<long>(type: "INTEGER", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ShiftId = table.Column<int>(type: "INTEGER", nullable: false),
                    Utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionAdjustments_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionAdjustments_TableSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "TableSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionAdjustments_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionPauses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    PausedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ResumedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PausedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResumedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    ShiftId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionPauses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionPauses_TableSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "TableSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionSegments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    TableId = table.Column<int>(type: "INTEGER", nullable: false),
                    SegmentIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    HourlyRate = table.Column<long>(type: "INTEGER", nullable: false),
                    StartUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    EndReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionSegments_PoolTables_TableId",
                        column: x => x.TableId,
                        principalTable: "PoolTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionSegments_TableSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "TableSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionAdjustments_ApprovedByUserId",
                table: "SessionAdjustments",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionAdjustments_SessionId",
                table: "SessionAdjustments",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionAdjustments_ShiftId",
                table: "SessionAdjustments",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionPauses_SessionId",
                table: "SessionPauses",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionSegments_SessionId",
                table: "SessionSegments",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionSegments_TableId",
                table: "SessionSegments",
                column: "TableId");

            migrationBuilder.CreateIndex(
                name: "IX_TableSessions_CurrentTableId",
                table: "TableSessions",
                column: "CurrentTableId",
                unique: true,
                filter: "\"Status\" IN ('Active', 'Paused')");

            migrationBuilder.CreateIndex(
                name: "IX_TableSessions_OpenedByUserId",
                table: "TableSessions",
                column: "OpenedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TableSessions_OpenedShiftId",
                table: "TableSessions",
                column: "OpenedShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_TableSessions_SessionNumber",
                table: "TableSessions",
                column: "SessionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TableSessions_StartUtc",
                table: "TableSessions",
                column: "StartUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TableSessions_Status",
                table: "TableSessions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingSettings");

            migrationBuilder.DropTable(
                name: "SessionAdjustments");

            migrationBuilder.DropTable(
                name: "SessionPauses");

            migrationBuilder.DropTable(
                name: "SessionSegments");

            migrationBuilder.DropTable(
                name: "TableSessions");
        }
    }
}
