using System.Reflection;
using System.Runtime.CompilerServices;
using TeeTimeBooking.Api.Bookings;

namespace TeeTimeBooking.Tests;

/// <summary>
/// The API is compiled with runtime async (<c>&lt;Features&gt;runtime-async=on&lt;/Features&gt;</c>): the runtime runs
/// its async methods, so the compiler emits no state machine for them. Fails if the switch is dropped.
/// </summary>
public class RuntimeAsyncTests
{
    [Fact]
    public void The_api_is_compiled_with_runtime_async()
    {
        var bookAsync = typeof(BookingService).GetMethod(nameof(BookingService.BookAsync))!;

        Assert.Null(bookAsync.GetCustomAttribute<AsyncStateMachineAttribute>());
    }
}
