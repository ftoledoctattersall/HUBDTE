using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HUBDTE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxLockingAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Error",
                schema: "dbo",
                table: "OutboxMessages",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_LockedAt",
                schema: "dbo",
                table: "OutboxMessages",
                columns: new[] { "Status", "LockedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Status_LockedAt",
                schema: "dbo",
                table: "OutboxMessages");

            migrationBuilder.AlterColumn<string>(
                name: "Error",
                schema: "dbo",
                table: "OutboxMessages",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);
        }
    }
}
