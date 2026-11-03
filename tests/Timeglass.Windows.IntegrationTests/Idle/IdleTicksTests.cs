using Timeglass.Windows.Idle;

namespace Timeglass.Windows.IntegrationTests.Idle;

public class IdleTicksTests
{
    private const long Wrap = 1L << 32;

    [Fact]
    public void Idle_is_the_difference_between_the_ticks()
    {
        IdleTicks.MillisecondsSinceLastInput(10_000, 7_500).ShouldBe(2_500);
    }

    [Fact]
    public void Input_right_now_is_zero_idle()
    {
        IdleTicks.MillisecondsSinceLastInput(10_000, 10_000).ShouldBe(0);
    }

    [Fact]
    public void Input_tick_from_after_a_wrap_is_handled()
    {
        // Uptime is past one wrap; dwTime holds only the low 32 bits.
        var now = Wrap + 5_000;
        var input = Wrap + 1_000;

        IdleTicks.MillisecondsSinceLastInput(now, (uint)input).ShouldBe(4_000);
    }

    [Fact]
    public void Input_before_the_wrap_and_now_after_it_is_handled()
    {
        var now = Wrap + 2_000;
        var input = Wrap - 3_000;

        IdleTicks.MillisecondsSinceLastInput(now, (uint)input).ShouldBe(5_000);
    }

    [Fact]
    public void Works_after_several_wraps()
    {
        var now = (5 * Wrap) + 60_000;
        var input = (5 * Wrap) - 60_000;

        IdleTicks.MillisecondsSinceLastInput(now, (uint)input).ShouldBe(120_000);
    }

    [Fact]
    public void Input_tick_slightly_ahead_of_now_is_zero_idle()
    {
        IdleTicks.MillisecondsSinceLastInput(10_000, 10_016).ShouldBe(0);
        IdleTicks.MillisecondsSinceLastInput(Wrap + 5, 10).ShouldBe(0);
    }

    [Fact]
    public void Long_idle_up_to_24_days_is_reported()
    {
        var idle = (long)TimeSpan.FromDays(24).TotalMilliseconds;
        var now = Wrap + 1_000;

        IdleTicks.MillisecondsSinceLastInput(now, (uint)(now - idle)).ShouldBe(idle);
    }

    [Fact]
    public void Idle_never_exceeds_uptime()
    {
        // Shortly after boot, a stale value cannot claim more idle than the machine has been up.
        IdleTicks.MillisecondsSinceLastInput(1_000, uint.MaxValue - 100_000).ShouldBeLessThanOrEqualTo(1_000);
    }
}
