using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApi.Data.Migrations;

/// <inheritdoc />
public partial class AddContentFingerprint : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ContentFingerprint",
            table: "UploadSessions",
            type: "TEXT",
            maxLength: 128,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_UploadSessions_Checksum_TotalSize_Status",
            table: "UploadSessions",
            columns: new[] { "Checksum", "TotalSize", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_UploadSessions_ContentFingerprint_TotalSize_Status",
            table: "UploadSessions",
            columns: new[] { "ContentFingerprint", "TotalSize", "Status" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_UploadSessions_ContentFingerprint_TotalSize_Status",
            table: "UploadSessions");

        migrationBuilder.DropIndex(
            name: "IX_UploadSessions_Checksum_TotalSize_Status",
            table: "UploadSessions");

        migrationBuilder.DropColumn(
            name: "ContentFingerprint",
            table: "UploadSessions");
    }
}
