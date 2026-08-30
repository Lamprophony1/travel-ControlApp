using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TravelControl.Domain;
using TravelControl.Infrastructure.Identity;

namespace TravelControl.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripTransferStatus> TripTransferStatuses => Set<TripTransferStatus>();
    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<Passenger> Passengers => Set<Passenger>();
    public DbSet<RoomReservation> RoomReservations => Set<RoomReservation>();
    public DbSet<FlightBooking> FlightBookings => Set<FlightBooking>();
    public DbSet<FlightSegment> FlightSegments => Set<FlightSegment>();
    public DbSet<PassengerFlight> PassengerFlights => Set<PassengerFlight>();
    public DbSet<BaggageEntitlement> BaggageEntitlements => Set<BaggageEntitlement>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<AttachmentLink> AttachmentLinks => Set<AttachmentLink>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<AppUser>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(160);
        });
        builder.Entity<Trip>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.Destination).HasMaxLength(160);
            entity.Property(x => x.TimeZone).HasMaxLength(80);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.HasOne(x => x.TransferStatus).WithOne(x => x.Trip)
                .HasForeignKey<TripTransferStatus>(x => x.TripId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<TripTransferStatus>().HasIndex(x => x.TripId).IsUnique();
        builder.Entity<Operator>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.HasIndex(x => x.Name).IsUnique();
        });
        builder.Entity<Passenger>(entity =>
        {
            entity.Property(x => x.FullName).HasMaxLength(220);
            entity.Property(x => x.NormalizedName).HasMaxLength(220);
            entity.Property(x => x.PassportNumber).HasMaxLength(40);
            entity.Property(x => x.NormalizedPassportNumber).HasMaxLength(40);
            entity.HasIndex(x => new { x.TripId, x.NormalizedName }).IsUnique();
            entity.HasIndex(x => new { x.TripId, x.NormalizedPassportNumber }).IsUnique()
                .HasFilter("NormalizedPassportNumber IS NOT NULL");
            entity.HasOne(x => x.RoomReservation).WithMany(x => x.Passengers)
                .HasForeignKey(x => x.RoomReservationId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.PrimaryOperator).WithMany()
                .HasForeignKey(x => x.PrimaryOperatorId).OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<RoomReservation>(entity =>
        {
            entity.Property(x => x.InternalCode).HasMaxLength(60);
            entity.HasIndex(x => new { x.TripId, x.InternalCode }).IsUnique();
        });
        builder.Entity<FlightBooking>().HasIndex(x => new { x.TripId, x.Pnr });
        builder.Entity<PassengerFlight>(entity =>
        {
            entity.HasKey(x => new { x.PassengerId, x.FlightBookingId });
            entity.Property(x => x.Version).IsConcurrencyToken();
        });
        builder.Entity<BaggageEntitlement>(entity =>
        {
            entity.Ignore(x => x.Includes23Kg);
            entity.HasIndex(x => new { x.PassengerId, x.FlightBookingId }).IsUnique()
                .HasFilter("FlightBookingId IS NOT NULL");
        });
        builder.Entity<Attachment>().HasIndex(x => x.Sha256).IsUnique();
        builder.Entity<AttachmentLink>(entity =>
        {
            entity.HasOne(x => x.Attachment).WithMany(x => x.Links).HasForeignKey(x => x.AttachmentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Passenger).WithMany().HasForeignKey(x => x.PassengerId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.RoomReservation).WithMany().HasForeignKey(x => x.RoomReservationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.FlightBooking).WithMany().HasForeignKey(x => x.FlightBookingId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.BaggageEntitlement).WithMany().HasForeignKey(x => x.BaggageEntitlementId).OnDelete(DeleteBehavior.Cascade);
            entity.ToTable(table => table.HasCheckConstraint("CK_AttachmentLink_ExactlyOneTarget",
                "((PassengerId IS NOT NULL) + (RoomReservationId IS NOT NULL) + (FlightBookingId IS NOT NULL) + (BaggageEntitlementId IS NOT NULL)) = 1"));
            entity.HasIndex(x => new { x.AttachmentId, x.PassengerId, x.EvidenceType }).IsUnique().HasFilter("PassengerId IS NOT NULL");
            entity.HasIndex(x => new { x.AttachmentId, x.RoomReservationId, x.EvidenceType }).IsUnique().HasFilter("RoomReservationId IS NOT NULL");
            entity.HasIndex(x => new { x.AttachmentId, x.FlightBookingId, x.EvidenceType }).IsUnique().HasFilter("FlightBookingId IS NOT NULL");
            entity.HasIndex(x => new { x.AttachmentId, x.BaggageEntitlementId, x.EvidenceType }).IsUnique().HasFilter("BaggageEntitlementId IS NOT NULL");
        });
        builder.Entity<ImportRun>().HasIndex(x => new { x.Sha256, x.DryRun });
        builder.Entity<FollowUp>().HasOne(x => x.Passenger).WithMany(x => x.FollowUps)
            .HasForeignKey(x => x.PassengerId).OnDelete(DeleteBehavior.Cascade);

        foreach (var entityType in builder.Model.GetEntityTypes().Where(x => typeof(Entity).IsAssignableFrom(x.ClrType)))
            builder.Entity(entityType.ClrType).Property(nameof(Entity.Version)).IsConcurrencyToken();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.Version = Math.Max(1, entry.Entity.Version);
            }
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                if (entry.State == EntityState.Modified)
                    entry.Entity.Version = entry.OriginalValues.GetValue<long>(nameof(Entity.Version)) + 1;
            }
        }
        foreach (var entry in ChangeTracker.Entries<PassengerFlight>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.Version = Math.Max(1, entry.Entity.Version);
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.Version = entry.OriginalValues.GetValue<long>(nameof(PassengerFlight.Version)) + 1;
                entry.Entity.UpdatedAt = now;
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
