using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErsatzTV.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Add_SkipEndCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "CreditsStart",
                table: "MediaVersion",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EndCreditsKeepSeconds",
                table: "Channel",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "SkipEndCredits",
                table: "Channel",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreditsStart",
                table: "MediaVersion");

            migrationBuilder.DropColumn(
                name: "EndCreditsKeepSeconds",
                table: "Channel");

            migrationBuilder.DropColumn(
                name: "SkipEndCredits",
                table: "Channel");
        }
    }
}
