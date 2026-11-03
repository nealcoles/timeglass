using Microsoft.Extensions.Time.Testing;
using Timeglass.Windows.Idle;

namespace Timeglass.Windows.IntegrationTests.Idle;

[Trait("Category", "WindowsIntegration")]
public class Win32IdleSourceTests
{
    [Fact]
    public void Idle_time_is_between_zero_and_uptime()
    {
        var idle = Win32IdleSource.GetIdleMilliseconds();
        var uptime = Environment.TickCount64;

        idle.ShouldBeGreaterThanOrEqualTo(0);
        idle.ShouldBeLessThanOrEqualTo(uptime);
    }

    [Fact]
    public void Last_input_is_not_in_the_future_and_not_before_boot()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
        var source = new Win32IdleSource(time);

        var lastInput = source.GetLastInputTime();

        lastInput.ShouldBeLessThanOrEqualTo(time.GetUtcNow());
        lastInput.ShouldBeGreaterThanOrEqualTo(time.GetUtcNow() - TimeSpan.FromMilliseconds(Environment.TickCount64));
    }
}
