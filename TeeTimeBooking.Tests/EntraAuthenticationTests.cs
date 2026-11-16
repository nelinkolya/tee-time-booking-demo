using Microsoft.Data.SqlClient;

namespace TeeTimeBooking.Tests;

/// <summary>
/// In Azure the app reaches SQL with its managed identity (<c>Authentication=Active Directory Managed Identity</c>,
/// infra/main.bicep). Since SqlClient 7 that provider ships separately, and without it the connection fails only
/// in Azure: CI's SQL Server uses a password. This catches a missing provider without Azure.
/// </summary>
public class EntraAuthenticationTests
{
    [Fact]
    public void The_managed_identity_provider_for_Azure_SQL_is_available()
    {
        // Load the API's dependencies the way the app does.
        _ = typeof(TeeTimeBooking.Api.Data.BookingDb);

        Assert.NotNull(SqlAuthenticationProvider.GetProvider(SqlAuthenticationMethod.ActiveDirectoryManagedIdentity));
    }
}
