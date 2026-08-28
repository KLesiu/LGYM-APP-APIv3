using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LgymApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTraineeNoteHistoryVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NewVisibleToTrainee",
                table: "TraineeNoteHistories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PreviousVisibleToTrainee",
                table: "TraineeNoteHistories",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NewVisibleToTrainee",
                table: "TraineeNoteHistories");

            migrationBuilder.DropColumn(
                name: "PreviousVisibleToTrainee",
                table: "TraineeNoteHistories");
        }
    }
}
