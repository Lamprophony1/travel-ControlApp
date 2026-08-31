using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FlightBaggageAndTicketAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AirlineOrderId",
                table: "PassengerFlights",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BookingLookupLastName",
                table: "PassengerFlights",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicTicketAccessToken",
                table: "PassengerFlights",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TicketAccessGeneratedAt",
                table: "PassengerFlights",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TicketAccessStatus",
                table: "PassengerFlights",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TicketAccessUrl",
                table: "PassengerFlights",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TicketAccessVerifiedAt",
                table: "PassengerFlights",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BaggageAppliesOutbound",
                table: "FlightBookings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BaggageAppliesReturn",
                table: "FlightBookings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BaggageNotes",
                table: "FlightBookings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaggageSourceReference",
                table: "FlightBookings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BaggageStatus",
                table: "FlightBookings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BaggageVerifiedAt",
                table: "FlightBookings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BaggageVerifiedById",
                table: "FlightBookings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CheckedBagCount",
                table: "FlightBookings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "CheckedBagIncluded",
                table: "FlightBookings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "CheckedBagWeightKg",
                table: "FlightBookings",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE PassengerFlights
                SET PublicTicketAccessToken = lower(hex(randomblob(32))),
                    Version = CASE WHEN Version IS NULL OR Version < 1 THEN 1 ELSE Version END;

                UPDATE FlightBookings
                SET BaggageStatus = CASE
                        WHEN NOT EXISTS (
                            SELECT 1 FROM BaggageEntitlements legacy
                            WHERE legacy.FlightBookingId = FlightBookings.Id
                        ) THEN 1
                        WHEN (
                            SELECT MIN(legacy.Status) = 3 AND MAX(legacy.Status) = 3
                            FROM BaggageEntitlements legacy
                            WHERE legacy.FlightBookingId = FlightBookings.Id
                        ) THEN 3
                        WHEN (
                            SELECT MIN(legacy.Status) = 0 AND MAX(legacy.Status) = 0
                                AND MIN(legacy.CheckedBagCount) = MAX(legacy.CheckedBagCount)
                                AND MIN(legacy.CheckedBagCount) >= 1
                                AND MIN(CAST(legacy.WeightPerBagKg AS REAL)) = MAX(CAST(legacy.WeightPerBagKg AS REAL))
                                AND MIN(CAST(legacy.WeightPerBagKg AS REAL)) >= 23
                                AND MIN(legacy.AppliesOutbound) = 1 AND MAX(legacy.AppliesOutbound) = 1
                                AND MIN(legacy.AppliesReturn) = 1 AND MAX(legacy.AppliesReturn) = 1
                            FROM BaggageEntitlements legacy
                            WHERE legacy.FlightBookingId = FlightBookings.Id
                        ) THEN 0
                        ELSE 2
                    END,
                    CheckedBagIncluded = CASE WHEN (
                        SELECT MIN(legacy.Status) = 0 AND MAX(legacy.Status) = 0
                            AND MIN(legacy.CheckedBagCount) = MAX(legacy.CheckedBagCount)
                            AND MIN(legacy.CheckedBagCount) >= 1
                            AND MIN(CAST(legacy.WeightPerBagKg AS REAL)) = MAX(CAST(legacy.WeightPerBagKg AS REAL))
                            AND MIN(CAST(legacy.WeightPerBagKg AS REAL)) >= 23
                            AND MIN(legacy.AppliesOutbound) = 1 AND MAX(legacy.AppliesOutbound) = 1
                            AND MIN(legacy.AppliesReturn) = 1 AND MAX(legacy.AppliesReturn) = 1
                        FROM BaggageEntitlements legacy
                        WHERE legacy.FlightBookingId = FlightBookings.Id
                    ) THEN 1 ELSE 0 END,
                    CheckedBagCount = CASE WHEN (
                        SELECT MIN(legacy.Status) = 0 AND MAX(legacy.Status) = 0
                            AND MIN(legacy.CheckedBagCount) = MAX(legacy.CheckedBagCount)
                            AND MIN(legacy.CheckedBagCount) >= 1
                            AND MIN(CAST(legacy.WeightPerBagKg AS REAL)) = MAX(CAST(legacy.WeightPerBagKg AS REAL))
                            AND MIN(CAST(legacy.WeightPerBagKg AS REAL)) >= 23
                            AND MIN(legacy.AppliesOutbound) = 1 AND MAX(legacy.AppliesOutbound) = 1
                            AND MIN(legacy.AppliesReturn) = 1 AND MAX(legacy.AppliesReturn) = 1
                        FROM BaggageEntitlements legacy
                        WHERE legacy.FlightBookingId = FlightBookings.Id
                    ) THEN (
                        SELECT MIN(legacy.CheckedBagCount) FROM BaggageEntitlements legacy
                        WHERE legacy.FlightBookingId = FlightBookings.Id
                    ) ELSE 0 END,
                    CheckedBagWeightKg = CASE WHEN (
                        SELECT MIN(legacy.Status) = 0 AND MAX(legacy.Status) = 0
                            AND MIN(legacy.CheckedBagCount) = MAX(legacy.CheckedBagCount)
                            AND MIN(legacy.CheckedBagCount) >= 1
                            AND MIN(CAST(legacy.WeightPerBagKg AS REAL)) = MAX(CAST(legacy.WeightPerBagKg AS REAL))
                            AND MIN(CAST(legacy.WeightPerBagKg AS REAL)) >= 23
                            AND MIN(legacy.AppliesOutbound) = 1 AND MAX(legacy.AppliesOutbound) = 1
                            AND MIN(legacy.AppliesReturn) = 1 AND MAX(legacy.AppliesReturn) = 1
                        FROM BaggageEntitlements legacy
                        WHERE legacy.FlightBookingId = FlightBookings.Id
                    ) THEN (
                        SELECT MIN(legacy.WeightPerBagKg) FROM BaggageEntitlements legacy
                        WHERE legacy.FlightBookingId = FlightBookings.Id
                    ) ELSE 0 END,
                    BaggageAppliesOutbound = CASE WHEN (
                        SELECT MIN(legacy.Status) = 0 AND MAX(legacy.Status) = 0
                            AND MIN(legacy.CheckedBagCount) = MAX(legacy.CheckedBagCount)
                            AND MIN(legacy.CheckedBagCount) >= 1
                            AND MIN(CAST(legacy.WeightPerBagKg AS REAL)) = MAX(CAST(legacy.WeightPerBagKg AS REAL))
                            AND MIN(CAST(legacy.WeightPerBagKg AS REAL)) >= 23
                            AND MIN(legacy.AppliesOutbound) = 1 AND MAX(legacy.AppliesOutbound) = 1
                            AND MIN(legacy.AppliesReturn) = 1 AND MAX(legacy.AppliesReturn) = 1
                        FROM BaggageEntitlements legacy
                        WHERE legacy.FlightBookingId = FlightBookings.Id
                    ) THEN 1 ELSE 0 END,
                    BaggageAppliesReturn = CASE WHEN (
                        SELECT MIN(legacy.Status) = 0 AND MAX(legacy.Status) = 0
                            AND MIN(legacy.CheckedBagCount) = MAX(legacy.CheckedBagCount)
                            AND MIN(legacy.CheckedBagCount) >= 1
                            AND MIN(CAST(legacy.WeightPerBagKg AS REAL)) = MAX(CAST(legacy.WeightPerBagKg AS REAL))
                            AND MIN(CAST(legacy.WeightPerBagKg AS REAL)) >= 23
                            AND MIN(legacy.AppliesOutbound) = 1 AND MAX(legacy.AppliesOutbound) = 1
                            AND MIN(legacy.AppliesReturn) = 1 AND MAX(legacy.AppliesReturn) = 1
                        FROM BaggageEntitlements legacy
                        WHERE legacy.FlightBookingId = FlightBookings.Id
                    ) THEN 1 ELSE 0 END,
                    BaggageSourceReference = CASE WHEN (
                        SELECT COUNT(DISTINCT COALESCE(legacy.SourceReference, '')) = 1
                        FROM BaggageEntitlements legacy
                        WHERE legacy.FlightBookingId = FlightBookings.Id
                    ) THEN (
                        SELECT NULLIF(MIN(COALESCE(legacy.SourceReference, '')), '')
                        FROM BaggageEntitlements legacy
                        WHERE legacy.FlightBookingId = FlightBookings.Id
                    ) ELSE NULL END,
                    BaggageNotes = CASE WHEN (
                        SELECT COUNT(DISTINCT COALESCE(legacy.Notes, '')) = 1
                        FROM BaggageEntitlements legacy
                        WHERE legacy.FlightBookingId = FlightBookings.Id
                    ) THEN (
                        SELECT NULLIF(MIN(COALESCE(legacy.Notes, '')), '')
                        FROM BaggageEntitlements legacy
                        WHERE legacy.FlightBookingId = FlightBookings.Id
                    ) ELSE NULL END;

                INSERT INTO AuditLogs (At, EntityName, EntityId, Action, NewValue)
                SELECT strftime('%Y-%m-%d %H:%M:%f+00:00', 'now'),
                       'FlightBooking', booking.Id, 'LegacyBaggageConflict',
                       '{"legacyConflict":true,"legacyRows":' || COUNT(legacy.Id) || '}'
                FROM FlightBookings booking
                JOIN BaggageEntitlements legacy ON legacy.FlightBookingId = booking.Id
                WHERE booking.BaggageStatus = 2
                GROUP BY booking.Id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PassengerFlights_PublicTicketAccessToken",
                table: "PassengerFlights",
                column: "PublicTicketAccessToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PassengerFlights_PublicTicketAccessToken",
                table: "PassengerFlights");

            migrationBuilder.DropColumn(
                name: "AirlineOrderId",
                table: "PassengerFlights");

            migrationBuilder.DropColumn(
                name: "BookingLookupLastName",
                table: "PassengerFlights");

            migrationBuilder.DropColumn(
                name: "PublicTicketAccessToken",
                table: "PassengerFlights");

            migrationBuilder.DropColumn(
                name: "TicketAccessGeneratedAt",
                table: "PassengerFlights");

            migrationBuilder.DropColumn(
                name: "TicketAccessStatus",
                table: "PassengerFlights");

            migrationBuilder.DropColumn(
                name: "TicketAccessUrl",
                table: "PassengerFlights");

            migrationBuilder.DropColumn(
                name: "TicketAccessVerifiedAt",
                table: "PassengerFlights");

            migrationBuilder.DropColumn(
                name: "BaggageAppliesOutbound",
                table: "FlightBookings");

            migrationBuilder.DropColumn(
                name: "BaggageAppliesReturn",
                table: "FlightBookings");

            migrationBuilder.DropColumn(
                name: "BaggageNotes",
                table: "FlightBookings");

            migrationBuilder.DropColumn(
                name: "BaggageSourceReference",
                table: "FlightBookings");

            migrationBuilder.DropColumn(
                name: "BaggageStatus",
                table: "FlightBookings");

            migrationBuilder.DropColumn(
                name: "BaggageVerifiedAt",
                table: "FlightBookings");

            migrationBuilder.DropColumn(
                name: "BaggageVerifiedById",
                table: "FlightBookings");

            migrationBuilder.DropColumn(
                name: "CheckedBagCount",
                table: "FlightBookings");

            migrationBuilder.DropColumn(
                name: "CheckedBagIncluded",
                table: "FlightBookings");

            migrationBuilder.DropColumn(
                name: "CheckedBagWeightKg",
                table: "FlightBookings");
        }
    }
}
