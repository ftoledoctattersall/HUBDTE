using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HUBDTE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxLocking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LockId",
                schema: "dbo",
                table: "OutboxMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAt",
                schema: "dbo",
                table: "OutboxMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_LockId_CreatedAt",
                schema: "dbo",
                table: "OutboxMessages",
                columns: new[] { "Status", "LockId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Status_LockId_CreatedAt",
                schema: "dbo",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LockId",
                schema: "dbo",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LockedAt",
                schema: "dbo",
                table: "OutboxMessages");
        }
    }
}
