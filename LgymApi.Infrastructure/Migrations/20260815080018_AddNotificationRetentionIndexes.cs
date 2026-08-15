using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LgymApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationRetentionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PushNotificationMessages_CreatedAt",
                table: "PushNotificationMessages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PushInstallations_DisabledAt",
                table: "PushInstallations",
                column: "DisabledAt");

            migrationBuilder.CreateIndex(
                name: "IX_in_app_notifications_CreatedAt",
                table: "in_app_notifications",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PushNotificationMessages_CreatedAt",
                table: "PushNotificationMessages");

            migrationBuilder.DropIndex(
                name: "IX_PushInstallations_DisabledAt",
                table: "PushInstallations");

            migrationBuilder.DropIndex(
                name: "IX_in_app_notifications_CreatedAt",
                table: "in_app_notifications");
        }
    }
}
