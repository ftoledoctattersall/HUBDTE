using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HUBDTE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxProcessingStartedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte>(
                name: "Status",
                schema: "dbo",
                table: "SapDocuments",
                type: "tinyint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<byte>(
                name: "Status",
                schema: "dbo",
                table: "OutboxMessages",
                type: "tinyint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingStartedAt",
                schema: "dbo",
                table: "OutboxMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_ProcessingStartedAt",
                schema: "dbo",
                table: "OutboxMessages",
                columns: new[] { "Status", "ProcessingStartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Status_ProcessingStartedAt",
                schema: "dbo",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ProcessingStartedAt",
                schema: "dbo",
                table: "OutboxMessages");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                schema: "dbo",
                table: "SapDocuments",
                type: "int",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "tinyint");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                schema: "dbo",
                table: "OutboxMessages",
                type: "int",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "tinyint");
        }
    }
}
