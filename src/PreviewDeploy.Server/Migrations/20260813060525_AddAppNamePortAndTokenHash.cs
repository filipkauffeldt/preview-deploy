using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PreviewDeploy.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAppNamePortAndTokenHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_deployments_AppId",
                table: "deployments");

            migrationBuilder.AddColumn<int>(
                name: "Port",
                table: "deployments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "apps",
                type: "TEXT",
                maxLength: 63,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Port",
                table: "apps",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "apps",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_deployments_AppId_PrNumber",
                table: "deployments",
                columns: new[] { "AppId", "PrNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_apps_Name",
                table: "apps",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_deployments_AppId_PrNumber",
                table: "deployments");

            migrationBuilder.DropIndex(
                name: "IX_apps_Name",
                table: "apps");

            migrationBuilder.DropColumn(
                name: "Port",
                table: "deployments");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "apps");

            migrationBuilder.DropColumn(
                name: "Port",
                table: "apps");

            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "apps");

            migrationBuilder.CreateIndex(
                name: "IX_deployments_AppId",
                table: "deployments",
                column: "AppId");
        }
    }
}
