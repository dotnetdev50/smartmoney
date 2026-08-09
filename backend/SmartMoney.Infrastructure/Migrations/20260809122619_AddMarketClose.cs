using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartMoney.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "market_close",
                columns: table => new
                {
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Close = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_close", x => new { x.Date, x.Symbol });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_close");
        }
    }
}
