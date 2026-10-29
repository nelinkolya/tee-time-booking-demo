using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace TeeTimeBooking.Api;

public class RateLimitOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Booking and cancel requests per client per window: enough for the UI's 1,000-player rush, not a flood.</summary>
    public int BookingPermits { get; set; } = 1100;
    public int BookingWindowMinutes { get; set; } = 10;

    /// <summary>Other API requests per client per minute.</summary>
    public int ApiPermitsPerMinute { get; set; } = 600;
}

/// <summary>
/// Per-client fixed windows keyed by IP. Behind the Container Apps ingress the client IP arrives in X-Forwarded-For,
/// so production sets ASPNETCORE_FORWARDEDHEADERS_ENABLED=true for RemoteIpAddress to be the client, not the proxy.
/// </summary>
public static class RateLimits
{
    public const string Bookings = "bookings";
    public const string Api = "api";

    public static IServiceCollection AddClientRateLimits(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection("RateLimiting").Get<RateLimitOptions>() ?? new RateLimitOptions();

        return services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(Bookings, http => Window(http, options.Enabled, options.BookingPermits, TimeSpan.FromMinutes(options.BookingWindowMinutes)));
            limiter.AddPolicy(Api, http => Window(http, options.Enabled, options.ApiPermitsPerMinute, TimeSpan.FromMinutes(1)));

            limiter.OnRejected = async (context, ct) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }
                await response.WriteAsJsonAsync(
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests. Try again later.",
                        Type = "https://tools.ietf.org/html/rfc6585#section-4",
                    },
                    options: null, contentType: "application/problem+json", cancellationToken: ct);
            };
        });
    }

    private static RateLimitPartition<string> Window(HttpContext http, bool enabled, int permits, TimeSpan window)
    {
        if (!enabled)
        {
            return RateLimitPartition.GetNoLimiter("off");
        }
        var client = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            QueueLimit = 0,
        });
    }
}
