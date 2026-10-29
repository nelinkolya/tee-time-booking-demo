using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace TeeTimeBooking.Api.Data;

public class BookingDb(DbContextOptions<BookingDb> options) : DbContext(options)
{
    public const int MaxIdempotencyKeyLength = 64;

    public DbSet<TeeTime> TeeTimes => Set<TeeTime>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<TeeTime>(e =>
        {
            e.Property(t => t.Course).HasMaxLength(100);
            e.HasIndex(t => new { t.Course, t.StartsAt }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint(
                "CK_TeeTime_BookedPlayers", "[BookedPlayers] >= 0 AND [BookedPlayers] <= [Capacity]"));
        });

        model.Entity<Booking>(e =>
        {
            // Clustered key: GUIDs ascending in SQL Server's sort order (unlike NewGuid or CreateVersion7).
            e.Property(b => b.Id).HasValueGenerator<SequentialGuidValueGenerator>();
            e.Property(b => b.PlayerName).HasMaxLength(100);
            e.Property(b => b.IdempotencyKey).HasMaxLength(MaxIdempotencyKeyLength);
            e.HasIndex(b => b.IdempotencyKey).IsUnique();
            e.HasIndex(b => b.TeeTimeId);
            e.ToTable(t => t.HasCheckConstraint("CK_Booking_Players", "[Players] BETWEEN 1 AND 4"));
        });
    }
}
