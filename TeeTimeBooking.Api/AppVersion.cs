using System.Reflection;

namespace TeeTimeBooking.Api;

public record VersionDto(string Version, string? Commit, DateTimeOffset? BuiltAtUtc, string Environment);

/// <summary>What is running, from the assembly: CI passes the commit and build time to <c>dotnet publish</c>.</summary>
public static class AppVersion
{
    private static readonly Assembly Assembly = typeof(AppVersion).Assembly;

    // InformationalVersion is "1.2.3+<commit>" when the build was given SourceRevisionId.
    private static readonly string Informational =
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    public static string Version { get; } = Informational.Split('+')[0];

    public static string? Commit { get; } = Informational.Contains('+') ? Informational.Split('+')[1] : null;

    public static DateTimeOffset? BuiltAtUtc { get; } =
        DateTimeOffset.TryParse(
            Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildTimestamp")?.Value,
            out var at) ? at : null;

    /// <summary>"1.2.3+abc1234": the short form for telemetry and display.</summary>
    public static string Display { get; } = Commit is null ? Version : $"{Version}+{Commit[..Math.Min(7, Commit.Length)]}";
}
