using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReportingAnalyticsIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CallDispositions_IsActive_Name",
                table: "CallDispositions");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Customers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Customers",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FollowUpAt",
                table: "Calls",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FollowUpNotes",
                table: "Calls",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Calls",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "CallRecordings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "CallRecordings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "CallQueueEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresFollowUp",
                table: "CallDispositions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresNotes",
                table: "CallDispositions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "CallDispositions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Agents",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Team",
                table: "Agents",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "General"),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DataType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "String"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Calls_FollowUpAt",
                table: "Calls",
                column: "FollowUpAt");

            migrationBuilder.CreateIndex(
                name: "IX_Calls_StartedAt_AssignedAgentId",
                table: "Calls",
                columns: new[] { "StartedAt", "AssignedAgentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Calls_StartedAt_CallDispositionId",
                table: "Calls",
                columns: new[] { "StartedAt", "CallDispositionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Calls_StartedAt_Direction_Status",
                table: "Calls",
                columns: new[] { "StartedAt", "Direction", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CallQueueEntries_CallQueueId_DequeuedAt_Priority_Position",
                table: "CallQueueEntries",
                columns: new[] { "CallQueueId", "DequeuedAt", "Priority", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_CallQueueEntries_EnqueuedAt_CallQueueId_DequeuedAt",
                table: "CallQueueEntries",
                columns: new[] { "EnqueuedAt", "CallQueueId", "DequeuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CallDispositions_IsActive_SortOrder_Name",
                table: "CallDispositions",
                columns: new[] { "IsActive", "SortOrder", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_Key",
                table: "SystemSettings",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropIndex(
                name: "IX_Calls_FollowUpAt",
                table: "Calls");

            migrationBuilder.DropIndex(
                name: "IX_Calls_StartedAt_AssignedAgentId",
                table: "Calls");

            migrationBuilder.DropIndex(
                name: "IX_Calls_StartedAt_CallDispositionId",
                table: "Calls");

            migrationBuilder.DropIndex(
                name: "IX_Calls_StartedAt_Direction_Status",
                table: "Calls");

            migrationBuilder.DropIndex(
                name: "IX_CallQueueEntries_CallQueueId_DequeuedAt_Priority_Position",
                table: "CallQueueEntries");

            migrationBuilder.DropIndex(
                name: "IX_CallQueueEntries_EnqueuedAt_CallQueueId_DequeuedAt",
                table: "CallQueueEntries");

            migrationBuilder.DropIndex(
                name: "IX_CallDispositions_IsActive_SortOrder_Name",
                table: "CallDispositions");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FollowUpAt",
                table: "Calls");

            migrationBuilder.DropColumn(
                name: "FollowUpNotes",
                table: "Calls");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Calls");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "CallRecordings");

            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "CallRecordings");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "CallQueueEntries");

            migrationBuilder.DropColumn(
                name: "RequiresFollowUp",
                table: "CallDispositions");

            migrationBuilder.DropColumn(
                name: "RequiresNotes",
                table: "CallDispositions");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "CallDispositions");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "Team",
                table: "Agents");

            migrationBuilder.CreateIndex(
                name: "IX_CallDispositions_IsActive_Name",
                table: "CallDispositions",
                columns: new[] { "IsActive", "Name" });
        }
    }
}
