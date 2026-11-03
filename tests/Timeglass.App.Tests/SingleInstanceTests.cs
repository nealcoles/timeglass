namespace Timeglass.App.Tests;

[Trait("Category", Desktop.Category)]
public sealed class SingleInstanceTests
{
    private static string UniqueName() => $"TimeglassTest{Guid.NewGuid():N}";

    [Fact(Timeout = Desktop.TestTimeout)]
    public async Task Only_the_first_instance_is_first()
    {
        var name = UniqueName();
        using var first = new SingleInstance(name);
        using var second = new SingleInstance(name);

        first.IsFirst.ShouldBeTrue();
        second.IsFirst.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Fact(Timeout = Desktop.TestTimeout)]
    public async Task The_name_is_free_again_once_the_first_instance_exits()
    {
        var name = UniqueName();
        new SingleInstance(name).Dispose();

        using var next = new SingleInstance(name);

        next.IsFirst.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Fact(Timeout = Desktop.TestTimeout)]
    public async Task A_second_launch_activates_the_first_instance()
    {
        var name = UniqueName();
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = new SingleInstance(name);
        first.StartListening(() => activated.TrySetResult());

        using var second = new SingleInstance(name);
        second.SignalFirst().ShouldBeTrue();

        await activated.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = Desktop.TestTimeout)]
    public async Task Signalling_fails_quietly_when_nobody_listens()
    {
        var name = UniqueName();
        using var first = new SingleInstance(name);
        using var second = new SingleInstance(name);

        second.SignalFirst().ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Fact]
    public void A_later_instance_cannot_listen()
    {
        var name = UniqueName();
        using var first = new SingleInstance(name);
        using var second = new SingleInstance(name);

        Should.Throw<InvalidOperationException>(() => second.StartListening(() => { }));
    }
}
