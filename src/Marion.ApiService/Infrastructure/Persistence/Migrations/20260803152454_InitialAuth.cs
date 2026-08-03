using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marion.ApiService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarionAuthSessions",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    LastActiveAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarionAuthSessions", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "MarionAuthTransactions",
                columns: table => new
                {
                    TransactionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarionAuthTransactions", x => x.TransactionId);
                });

            migrationBuilder.CreateTable(
                name: "MarionExternalIdentities",
                columns: table => new
                {
                    Issuer = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarionExternalIdentities", x => new { x.Issuer, x.Subject })
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarionAuthTransactions_ExpiresAt",
                table: "MarionAuthTransactions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_MarionExternalIdentities_UserId",
                table: "MarionExternalIdentities",
                column: "UserId",
                unique: true)
                .Annotation("SqlServer:Clustered", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarionAuthSessions");

            migrationBuilder.DropTable(
                name: "MarionAuthTransactions");

            migrationBuilder.DropTable(
                name: "MarionExternalIdentities");
        }
    }
}
