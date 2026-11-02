using Timeglass.Windows.Foreground;

namespace Timeglass.Windows.IntegrationTests;

public class ForegroundChangeDetectorTests
{
    private readonly ForegroundChangeDetector _detector = new();

    [Fact]
    public void The_first_observation_is_a_change()
    {
        _detector.IsChange(1, "Inbox").ShouldBeTrue();
    }

    [Fact]
    public void The_same_window_and_title_again_is_not_a_change()
    {
        _detector.IsChange(1, "Inbox");

        _detector.IsChange(1, "Inbox").ShouldBeFalse();
        _detector.IsChange(1, "Inbox").ShouldBeFalse();
    }

    [Fact]
    public void Another_window_is_a_change()
    {
        _detector.IsChange(1, "Inbox");

        _detector.IsChange(2, "Inbox").ShouldBeTrue();
    }

    [Theory]
    [InlineData("RE: Budget")]
    [InlineData("inbox")]
    [InlineData(null)]
    public void A_new_title_of_the_same_window_is_a_change(string? title)
    {
        _detector.IsChange(1, "Inbox");

        _detector.IsChange(1, title).ShouldBeTrue();
    }

    [Fact]
    public void No_foreground_window_is_reported_once()
    {
        _detector.IsChange(1, "Inbox");

        _detector.IsChange(0, null).ShouldBeTrue();
        _detector.IsChange(0, null).ShouldBeFalse();
    }

    [Fact]
    public void Back_to_an_earlier_window_is_a_change()
    {
        _detector.IsChange(1, "Inbox");
        _detector.IsChange(2, "Code");

        _detector.IsChange(1, "Inbox").ShouldBeTrue();
    }

    [Fact]
    public void After_reset_the_same_observation_is_reported_again()
    {
        _detector.IsChange(1, "Inbox");
        _detector.Reset();

        _detector.IsChange(1, "Inbox").ShouldBeTrue();
    }
}
