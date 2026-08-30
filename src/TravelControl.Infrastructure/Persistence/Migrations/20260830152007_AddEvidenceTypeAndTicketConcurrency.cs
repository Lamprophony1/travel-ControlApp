using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEvidenceTypeAndTicketConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AttachmentLinks_AttachmentId_BaggageEntitlementId",
                table: "AttachmentLinks");

            migrationBuilder.DropIndex(
                name: "IX_AttachmentLinks_AttachmentId_FlightBookingId",
                table: "AttachmentLinks");

            migrationBuilder.DropIndex(
                name: "IX_AttachmentLinks_AttachmentId_PassengerId",
                table: "AttachmentLinks");

            migrationBuilder.DropIndex(
                name: "IX_AttachmentLinks_AttachmentId_RoomReservationId",
                table: "AttachmentLinks");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "PassengerFlights",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedById",
                table: "PassengerFlights",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "PassengerFlights",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "EvidenceType",
                table: "AttachmentLinks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Attachment.DocumentType remains available for rollback compatibility, but link classification is authoritative.
            migrationBuilder.Sql("""
                UPDATE AttachmentLinks
                SET EvidenceType = (
                    SELECT Attachments.DocumentType
                    FROM Attachments
                    WHERE Attachments.Id = AttachmentLinks.AttachmentId
                );
                """);
            migrationBuilder.Sql("""
                UPDATE PassengerFlights
                SET Version = 1,
                    UpdatedAt = COALESCE(
                        (SELECT FlightBookings.UpdatedAt FROM FlightBookings WHERE FlightBookings.Id = PassengerFlights.FlightBookingId),
                        CURRENT_TIMESTAMP
                    )
                WHERE Version < 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_AttachmentId_BaggageEntitlementId_EvidenceType",
                table: "AttachmentLinks",
                columns: new[] { "AttachmentId", "BaggageEntitlementId", "EvidenceType" },
                unique: true,
                filter: "BaggageEntitlementId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_AttachmentId_FlightBookingId_EvidenceType",
                table: "AttachmentLinks",
                columns: new[] { "AttachmentId", "FlightBookingId", "EvidenceType" },
                unique: true,
                filter: "FlightBookingId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_AttachmentId_PassengerId_EvidenceType",
                table: "AttachmentLinks",
                columns: new[] { "AttachmentId", "PassengerId", "EvidenceType" },
                unique: true,
                filter: "PassengerId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_AttachmentId_RoomReservationId_EvidenceType",
                table: "AttachmentLinks",
                columns: new[] { "AttachmentId", "RoomReservationId", "EvidenceType" },
                unique: true,
                filter: "RoomReservationId IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AttachmentLinks_AttachmentId_BaggageEntitlementId_EvidenceType",
                table: "AttachmentLinks");

            migrationBuilder.DropIndex(
                name: "IX_AttachmentLinks_AttachmentId_FlightBookingId_EvidenceType",
                table: "AttachmentLinks");

            migrationBuilder.DropIndex(
                name: "IX_AttachmentLinks_AttachmentId_PassengerId_EvidenceType",
                table: "AttachmentLinks");

            migrationBuilder.DropIndex(
                name: "IX_AttachmentLinks_AttachmentId_RoomReservationId_EvidenceType",
                table: "AttachmentLinks");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "PassengerFlights");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                table: "PassengerFlights");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "PassengerFlights");

            migrationBuilder.DropColumn(
                name: "EvidenceType",
                table: "AttachmentLinks");

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
        }
    }
}
