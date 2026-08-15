using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApi.Data.Migrations;

/// <inheritdoc />
public partial class InitialUploadSessions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UploadSessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                FileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                FinalFileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                TotalSize = table.Column<long>(type: "INTEGER", nullable: false),
                ChunkSize = table.Column<int>(type: "INTEGER", nullable: false),
                TotalChunks = table.Column<int>(type: "INTEGER", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                Version = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                Checksum = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                ContentType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                ClientIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                ReceivedChunksCsv = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UploadSessions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_UploadSessions_ClientIp_Status",
            table: "UploadSessions",
            columns: new[] { "ClientIp", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_UploadSessions_ExpiresAt",
            table: "UploadSessions",
            column: "ExpiresAt");

        migrationBuilder.CreateIndex(
            name: "IX_UploadSessions_Status",
            table: "UploadSessions",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_UploadSessions_Status_ExpiresAt",
            table: "UploadSessions",
            columns: new[] { "Status", "ExpiresAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "UploadSessions");
    }
}
