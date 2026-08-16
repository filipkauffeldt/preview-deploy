using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PreviewDeploy.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSweepAndTtl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageTag",
                table: "deployments",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TtlDays",
                table: "apps",
                type: "INTEGER",
                nullable: false,
                defaultValue: 14);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageTag",
                table: "deployments");

            migrationBuilder.DropColumn(
                name: "TtlDays",
                table: "apps");
        }
    }
}
