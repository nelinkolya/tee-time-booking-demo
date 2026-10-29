namespace TeeTimeBooking.Tests;

public class TestDatabaseGuardTests
{
    [Theory]
    [InlineData("Server=x;Database=TeeTimeBooking")]
    [InlineData("Server=x;Database=master")]
    [InlineData("Server=x;Database=TeeTimeBooking_Testing")]
    public void Refuses_a_database_that_is_not_named_Test(string connectionString)
    {
        Assert.Throws<InvalidOperationException>(() => TestDatabase.EnsureTestDatabase(connectionString));
    }

    [Fact]
    public void Accepts_a_Test_database()
    {
        TestDatabase.EnsureTestDatabase("Server=x;Database=TeeTimeBooking_Test");
    }
}
