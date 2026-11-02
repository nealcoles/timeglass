using System.Runtime.InteropServices;

namespace Timeglass.Windows.IntegrationTests;

/// <summary>
/// Top-level windows owned by the test process, on their own UI thread.
/// A process may bring its own windows to the front, so tests never depend on other apps.
/// </summary>
public sealed partial class TestDesktop : IDisposable
{
    private readonly Thread _thread;
    private readonly TaskCompletionSource<Control> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Control _dispatcher = null!;

    public TestDesktop()
    {
        _thread = new Thread(() =>
        {
            _dispatcher = new Control();
            _dispatcher.CreateControl();
            _ready.SetResult(_dispatcher);
            Application.Run();
        })
        {
            IsBackground = true,
            Name = "Test windows",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Task.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("test UI thread started");
    }

    /// <summary>Opens a new top-level window with the given title.</summary>
    public Form Open(string title) => Invoke(() =>
    {
        var form = new Form { Text = title, Width = 300, Height = 200, ShowInTaskbar = true };
        form.Show();
        return form;
    });

    public void SetTitle(Form form, string title) => Invoke(() => form.Text = title);

    /// <summary>
    /// Brings <paramref name="form"/> to the front. Skips the test if this desktop won't let the
    /// test process take the foreground (foreground lock), rather than failing it.
    /// </summary>
    public void Activate(Form form)
    {
        var handle = Invoke(() => form.Handle);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Invoke(() =>
            {
                // A synthetic Alt press counts as input, which lifts the foreground lock for this process.
                if (attempt > 0)
                {
                    keybd_event(VkMenu, 0, 0, 0);
                    keybd_event(VkMenu, 0, KeyEventFKeyUp, 0);
                }

                SetForegroundWindow(handle);
                form.Activate();
            });

            if (SpinWait.SpinUntil(() => GetForegroundWindow() == handle, TimeSpan.FromSeconds(1)))
            {
                return;
            }
        }

        Assert.Skip("This desktop does not let the test process take the foreground.");
    }

    public void Dispose()
    {
        Invoke(() =>
        {
            foreach (var form in Application.OpenForms.Cast<Form>().ToList())
            {
                form.Close();
            }

            Application.ExitThread();
            return 0;
        });
        _thread.Join(TimeSpan.FromSeconds(5));
    }

    private T Invoke<T>(Func<T> action) => (T)_dispatcher.Invoke(action);

    private void Invoke(Action action) => _dispatcher.Invoke(action);

    private const byte VkMenu = 0x12;
    private const uint KeyEventFKeyUp = 0x0002;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte vk, byte scan, uint flags, nuint extraInfo);
}
