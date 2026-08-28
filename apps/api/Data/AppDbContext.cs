using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TravelControl.Api.Domain;

namespace TravelControl.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, Microsoft.AspNetCore.Identity.IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<Passenger> Passengers => Set<Passenger>();
    public DbSet<RoomReservation> RoomReservations => Set<RoomReservation>();
    public DbSet<FlightBooking> FlightBookings => Set<FlightBooking>();
    public DbSet<FlightSegment> FlightSegments => Set<FlightSegment>();
    public DbSet<PassengerFlight> PassengerFlights => Set<PassengerFlight>();
    public DbSet<BaggageEntitlement> BaggageEntitlements => Set<BaggageEntitlement>();
    public DbSet<TransferBooking> TransferBookings => Set<TransferBooking>();
    public DbSet<PassengerTransfer> PassengerTransfers => Set<PassengerTransfer>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasPostgresEnum<VerificationStatus>();
        b.Entity<Trip>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Operator>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Passenger>().HasIndex(x => new { x.TripId, x.NormalizedName }).IsUnique();
        b.Entity<Passenger>().HasIndex(x => new { x.TripId, x.NormalizedPassportNumber }).IsUnique()
            .HasFilter("\"NormalizedPassportNumber\" IS NOT NULL");
        b.Entity<RoomReservation>().HasIndex(x => new { x.TripId, x.InternalCode }).IsUnique();
        b.Entity<PassengerFlight>().HasKey(x => new { x.PassengerId, x.FlightBookingId });
        b.Entity<PassengerTransfer>().HasKey(x => new { x.PassengerId, x.TransferBookingId });
        b.Entity<Attachment>().HasIndex(x => x.Sha256);
        b.Entity<ImportRun>().HasIndex(x => new { x.Sha256, x.DryRun });

        foreach (var type in b.Model.GetEntityTypes().Where(x => typeof(Entity).IsAssignableFrom(x.ClrType)))
        {
            b.Entity(type.ClrType).Property(nameof(Entity.Version)).IsRowVersion();
        }

        b.Entity<Passenger>().HasOne(x => x.RoomReservation).WithMany(x => x.Passengers)
            .HasForeignKey(x => x.RoomReservationId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Passenger>().HasOne(x => x.PrimaryOperator).WithMany()
            .HasForeignKey(x => x.PrimaryOperatorId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<FollowUp>().HasOne(x => x.Passenger).WithMany(x => x.FollowUps)
            .HasForeignKey(x => x.PassengerId).OnDelete(DeleteBehavior.Cascade);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = now;
            if (entry.State is EntityState.Added or EntityState.Modified) entry.Entity.UpdatedAt = now;
        }
        return await base.SaveChangesAsync(cancellationToken);
    }
}

