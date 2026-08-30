using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEvidenceLinksAndIdentification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BaggageEntitlements_PassengerId",
                table: "BaggageEntitlements");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_Sha256",
                table: "Attachments");

            migrationBuilder.AddColumn<int>(
                name: "Conflicts",
                table: "ImportRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ImportType",
                table: "ImportRuns",
                type: "TEXT",
                nullable: false,
                defaultValue: "Master");

            migrationBuilder.AddColumn<int>(
                name: "Matched",
                table: "ImportRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AttachmentLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttachmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PassengerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RoomReservationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FlightBookingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BaggageEntitlementId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttachmentLinks", x => x.Id);
                    table.CheckConstraint("CK_AttachmentLink_ExactlyOneTarget", "((PassengerId IS NOT NULL) + (RoomReservationId IS NOT NULL) + (FlightBookingId IS NOT NULL) + (BaggageEntitlementId IS NOT NULL)) = 1");
                    table.ForeignKey(
                        name: "FK_AttachmentLinks_Attachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "Attachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttachmentLinks_BaggageEntitlements_BaggageEntitlementId",
                        column: x => x.BaggageEntitlementId,
                        principalTable: "BaggageEntitlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttachmentLinks_FlightBookings_FlightBookingId",
                        column: x => x.FlightBookingId,
                        principalTable: "FlightBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttachmentLinks_Passengers_PassengerId",
                        column: x => x.PassengerId,
                        principalTable: "Passengers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttachmentLinks_RoomReservations_RoomReservationId",
                        column: x => x.RoomReservationId,
                        principalTable: "RoomReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Preserve every legacy association while leaving the physical file metadata untouched.
            // randomblob-generated UUIDs are used because SQLite has no native UUID generator.
            migrationBuilder.Sql("""
                INSERT INTO AttachmentLinks
                    (Id, AttachmentId, PassengerId, RoomReservationId, FlightBookingId, BaggageEntitlementId, CreatedByUserId, CreatedAt, UpdatedAt, Version)
                SELECT
                    lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' || substr(lower(hex(randomblob(2))),2) ||
                    '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(lower(hex(randomblob(2))),2) || '-' || lower(hex(randomblob(6))),
                    Id, PassengerId, NULL, NULL, NULL, UploadedById, UploadedAt, UploadedAt, 1
                FROM Attachments
                WHERE PassengerId IS NOT NULL;
                """);
            migrationBuilder.Sql("""
                INSERT INTO AttachmentLinks
                    (Id, AttachmentId, PassengerId, RoomReservationId, FlightBookingId, BaggageEntitlementId, CreatedByUserId, CreatedAt, UpdatedAt, Version)
                SELECT
                    lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' || substr(lower(hex(randomblob(2))),2) ||
                    '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(lower(hex(randomblob(2))),2) || '-' || lower(hex(randomblob(6))),
                    Id, NULL, RoomReservationId, NULL, NULL, UploadedById, UploadedAt, UploadedAt, 1
                FROM Attachments
                WHERE RoomReservationId IS NOT NULL;
                """);
            migrationBuilder.Sql("""
                INSERT INTO AttachmentLinks
                    (Id, AttachmentId, PassengerId, RoomReservationId, FlightBookingId, BaggageEntitlementId, CreatedByUserId, CreatedAt, UpdatedAt, Version)
                SELECT
                    lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' || substr(lower(hex(randomblob(2))),2) ||
                    '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(lower(hex(randomblob(2))),2) || '-' || lower(hex(randomblob(6))),
                    Id, NULL, NULL, FlightBookingId, NULL, UploadedById, UploadedAt, UploadedAt, 1
                FROM Attachments
                WHERE FlightBookingId IS NOT NULL;
                """);
            migrationBuilder.Sql("""
                INSERT INTO AttachmentLinks
                    (Id, AttachmentId, PassengerId, RoomReservationId, FlightBookingId, BaggageEntitlementId, CreatedByUserId, CreatedAt, UpdatedAt, Version)
                SELECT
                    lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' || substr(lower(hex(randomblob(2))),2) ||
                    '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(lower(hex(randomblob(2))),2) || '-' || lower(hex(randomblob(6))),
                    Id, NULL, NULL, NULL, BaggageEntitlementId, UploadedById, UploadedAt, UploadedAt, 1
                FROM Attachments
                WHERE BaggageEntitlementId IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_BaggageEntitlements_PassengerId_FlightBookingId",
                table: "BaggageEntitlements",
                columns: new[] { "PassengerId", "FlightBookingId" },
                unique: true,
                filter: "FlightBookingId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_Sha256",
                table: "Attachments",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_AttachmentId_BaggageEntitlementId",
                table: "AttachmentLinks",
                columns: new[] { "AttachmentId", "BaggageEntitlementId" },
                unique: true,
                filter: "BaggageEntitlementId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_AttachmentId_FlightBookingId",
                table: "AttachmentLinks",
                columns: new[] { "AttachmentId", "FlightBookingId" },
                unique: true,
                filter: "FlightBookingId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_AttachmentId_PassengerId",
                table: "AttachmentLinks",
                columns: new[] { "AttachmentId", "PassengerId" },
                unique: true,
                filter: "PassengerId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_AttachmentId_RoomReservationId",
                table: "AttachmentLinks",
                columns: new[] { "AttachmentId", "RoomReservationId" },
                unique: true,
                filter: "RoomReservationId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_BaggageEntitlementId",
                table: "AttachmentLinks",
                column: "BaggageEntitlementId");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_FlightBookingId",
                table: "AttachmentLinks",
                column: "FlightBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_PassengerId",
                table: "AttachmentLinks",
                column: "PassengerId");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_RoomReservationId",
                table: "AttachmentLinks",
                column: "RoomReservationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttachmentLinks");

            migrationBuilder.DropIndex(
                name: "IX_BaggageEntitlements_PassengerId_FlightBookingId",
                table: "BaggageEntitlements");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_Sha256",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "Conflicts",
                table: "ImportRuns");

            migrationBuilder.DropColumn(
                name: "ImportType",
                table: "ImportRuns");

            migrationBuilder.DropColumn(
                name: "Matched",
                table: "ImportRuns");

            migrationBuilder.CreateIndex(
                name: "IX_BaggageEntitlements_PassengerId",
                table: "BaggageEntitlements",
                column: "PassengerId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_Sha256",
                table: "Attachments",
                column: "Sha256");
        }
    }
}
