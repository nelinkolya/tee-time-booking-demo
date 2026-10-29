using Microsoft.EntityFrameworkCore;

namespace TeeTimeBooking.Api.Data;

public static class Seed
{
    public const string Course = "Riverside Links";

    public static async Task EnsureTeeTimesAsync(BookingDb db, DateOnly from, int days = 7)
    {
        for (var d = 0; d < days; d++)
        {
            var day = from.AddDays(d).ToDateTime(TimeOnly.MinValue);
            if (await db.TeeTimes.AnyAsync(t => t.Course == Course && t.StartsAt >= day && t.StartsAt < day.AddDays(1)))
            {
                continue;
            }
            for (var start = day.AddHours(7); start < day.AddHours(17); start = start.AddMinutes(10))
            {
                db.TeeTimes.Add(new TeeTime { Course = Course, StartsAt = start });
            }
        }
        await db.SaveChangesAsync();
    }

    public static async Task ResetAsync(BookingDb db)
    {
        await db.Bookings.ExecuteDeleteAsync();
        await db.TeeTimes.ExecuteUpdateAsync(s => s.SetProperty(t => t.BookedPlayers, 0));
    }
}
