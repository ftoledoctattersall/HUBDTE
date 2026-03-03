using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HUBDTE.Infrastructure.Migrations 
{
    /// <inheritdoc />
    public partial class AddSapDocumentUniqueKeyAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "SapDocuments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FilialCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocEntry = table.Column<long>(type: "bigint", nullable: false),
                    TipoDte = table.Column<int>(type: "int", nullable: false),
                    QueueName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SapDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SapDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PublishAttempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutboxMessages_SapDocuments_SapDocumentId",
                        column: x => x.SapDocumentId,
                        principalSchema: "dbo",
                        principalTable: "SapDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_SapDocumentId",
                schema: "dbo",
                table: "OutboxMessages",
                column: "SapDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_CreatedAt",
                schema: "dbo",
                table: "OutboxMessages",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SapDocuments_Status_UpdatedAt",
                schema: "dbo",
                table: "SapDocuments",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_SapDocuments_Filial_DocEntry_TipoDte",
                schema: "dbo",
                table: "SapDocuments",
                columns: new[] { "FilialCode", "DocEntry", "TipoDte" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SapDocuments",
                schema: "dbo");
        }
    }
}
