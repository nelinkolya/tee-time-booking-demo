namespace TeeTimeBooking.Api.Data;

public class TeeTime
{
    public int Id { get; set; }
    public required string Course { get; set; }

    /// <summary>Course-local time.</summary>
    public DateTime StartsAt { get; set; }

    public int Capacity { get; set; } = 4;

    /// <summary>Sum of confirmed bookings; every booking updates this row, which serialises them.</summary>
    public int BookedPlayers { get; set; }
}

public enum BookingStatus
{
    Confirmed = 0,
    Cancelled = 1,
}

public class Booking
{
    public Guid Id { get; set; }
    public int TeeTimeId { get; set; }
    public TeeTime TeeTime { get; set; } = null!;
    public required string PlayerName { get; set; }
    public int Players { get; set; }

    public required string IdempotencyKey { get; set; }

    public BookingStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
}
