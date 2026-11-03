using Timeglass.Windows.Processes;

namespace Timeglass.Windows.IntegrationTests.Processes;

public class UwpAppLocatorTests
{
    private const uint FrameHost = 100;

    [Theory]
    [InlineData("ApplicationFrameHost.exe", true)]
    [InlineData("applicationframehost.EXE", true)]
    [InlineData("CalculatorApp.exe", false)]
    [InlineData("", false)]
    public void Recognises_the_frame_host(string exeName, bool expected)
    {
        UwpAppLocator.IsFrameHost(exeName).ShouldBe(expected);
    }

    [Fact]
    public void Picks_the_core_window_in_another_process()
    {
        ChildWindow[] children =
        [
            new(FrameHost, "ApplicationFrameTitleBarWindow"),
            new(200, "SomethingElse"),
            new(300, UwpAppLocator.CoreWindowClassName),
        ];

        UwpAppLocator.PickAppProcess(FrameHost, children).ShouldBe(300u);
    }

    [Fact]
    public void Falls_back_to_any_child_in_another_process()
    {
        ChildWindow[] children = [new(FrameHost, "ApplicationFrameTitleBarWindow"), new(200, null)];

        UwpAppLocator.PickAppProcess(FrameHost, children).ShouldBe(200u);
    }

    [Fact]
    public void A_core_window_of_the_frame_host_itself_does_not_count()
    {
        ChildWindow[] children = [new(FrameHost, UwpAppLocator.CoreWindowClassName), new(0, UwpAppLocator.CoreWindowClassName)];

        UwpAppLocator.PickAppProcess(FrameHost, children).ShouldBeNull();
    }

    [Fact]
    public void No_children_means_no_app_process()
    {
        UwpAppLocator.PickAppProcess(FrameHost, []).ShouldBeNull();
    }
}
