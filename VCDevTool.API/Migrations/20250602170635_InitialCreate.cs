using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCDevTool.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileLocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    LockingNodeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AcquiredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileLocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Nodes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                    IsAvailable = table.Column<bool>(type: "bit", nullable: false),
                    LastHeartbeat = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HardwareFingerprint = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ActiveDirectoryName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DomainController = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    OrganizationalUnit = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DistinguishedName = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DnsHostName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    OperatingSystem = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastAdLogon = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsAdEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AdGroups = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    LastAdSync = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ServicePrincipalName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AssignedNodeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AssignedNodeIdsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Parameters = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ResultMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskFolderProgress",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    FolderPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FolderName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AssignedNodeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AssignedNodeName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Progress = table.Column<double>(type: "float", nullable: false),
                    OutputPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskFolderProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskFolderProgress_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileLocks_AcquiredAt",
                table: "FileLocks",
                column: "AcquiredAt");

            migrationBuilder.CreateIndex(
                name: "IX_FileLocks_FilePath",
                table: "FileLocks",
                column: "FilePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileLocks_LastUpdated_NodeId",
                table: "FileLocks",
                columns: new[] { "LastUpdatedAt", "LockingNodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_FileLocks_LockingNodeId",
                table: "FileLocks",
                column: "LockingNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_ADEnabled_LastSync",
                table: "Nodes",
                columns: new[] { "IsAdEnabled", "LastAdSync" });

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_ADName",
                table: "Nodes",
                column: "ActiveDirectoryName");

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_Availability_Heartbeat",
                table: "Nodes",
                columns: new[] { "IsAvailable", "LastHeartbeat" });

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_DistinguishedName",
                table: "Nodes",
                column: "DistinguishedName");

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_IpAddress",
                table: "Nodes",
                column: "IpAddress",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_Name",
                table: "Nodes",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TaskFolderProgress_CreatedAt",
                table: "TaskFolderProgress",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TaskFolderProgress_Status",
                table: "TaskFolderProgress",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TaskFolderProgress_Status_NodeId",
                table: "TaskFolderProgress",
                columns: new[] { "Status", "AssignedNodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskFolderProgress_TaskId",
                table: "TaskFolderProgress",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskFolderProgress_TaskId_FolderPath",
                table: "TaskFolderProgress",
                columns: new[] { "TaskId", "FolderPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_AssignedNodeId",
                table: "Tasks",
                column: "AssignedNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_AssignedNodeId_Status",
                table: "Tasks",
                columns: new[] { "AssignedNodeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CompletedAt",
                table: "Tasks",
                column: "CompletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CreatedAt",
                table: "Tasks",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_StartedAt",
                table: "Tasks",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Status",
                table: "Tasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Status_CreatedAt",
                table: "Tasks",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Type",
                table: "Tasks",
                column: "Type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileLocks");

            migrationBuilder.DropTable(
                name: "Nodes");

            migrationBuilder.DropTable(
                name: "TaskFolderProgress");

            migrationBuilder.DropTable(
                name: "Tasks");
        }
    }
}
