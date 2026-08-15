using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApi.Data.Migrations;

/// <inheritdoc />
[Migration("20260815120000_InitialUploadSessions")]
public partial class InitialUploadSessions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UploadSessions",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                FileName = table.Column<string>(maxLength: 512, nullable: false),
                FinalFileName = table.Column<string>(maxLength: 512, nullable: true),
                TotalSize = table.Column<long>(nullable: false),
                ChunkSize = table.Column<int>(nullable: false),
                TotalChunks = table.Column<int>(nullable: false),
                Status = table.Column<int>(nullable: false),
                Version = table.Column<int>(nullable: false, defaultValue: 0),
                CreatedAt = table.Column<DateTime>(nullable: false),
                CompletedAt = table.Column<DateTime>(nullable: true),
                ExpiresAt = table.Column<DateTime>(nullable: false),
                Checksum = table.Column<string>(maxLength: 128, nullable: true),
                ContentType = table.Column<string>(maxLength: 256, nullable: true),
                ClientIp = table.Column<string>(maxLength: 64, nullable: true),
                ReceivedChunksCsv = table.Column<string>(maxLength: 8000, nullable: false,
                    defaultValue: "")
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
