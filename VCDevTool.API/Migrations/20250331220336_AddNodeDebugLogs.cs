using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCDevTool.API.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeDebugLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcquiredAt",
                table: "FileLocks");

            migrationBuilder.RenameColumn(
                name: "LockingNodeId",
                table: "FileLocks",
                newName: "NodeId");

            migrationBuilder.RenameColumn(
                name: "LastUpdatedAt",
                table: "FileLocks",
                newName: "CreatedAt");

            migrationBuilder.CreateTable(
                name: "NodeDebugLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NodeId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LogContent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TaskId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeDebugLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NodeDebugLogs_NodeId",
                table: "NodeDebugLogs",
                column: "NodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NodeDebugLogs");

            migrationBuilder.RenameColumn(
                name: "NodeId",
                table: "FileLocks",
                newName: "LockingNodeId");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "FileLocks",
                newName: "LastUpdatedAt");

            migrationBuilder.AddColumn<DateTime>(
                name: "AcquiredAt",
                table: "FileLocks",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }
    }
}
