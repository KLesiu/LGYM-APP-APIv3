using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LgymApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RepairRecurringReportAssignmentRequestIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_ReportRequests_RecurringReportAssignmentId";
                CREATE INDEX "IX_ReportRequests_RecurringReportAssignmentId" ON "ReportRequests" ("RecurringReportAssignmentId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Restoring uniqueness is unsafe after multiple history rows.");
        }
    }
}
