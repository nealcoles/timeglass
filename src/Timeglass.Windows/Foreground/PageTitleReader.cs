using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace Timeglass.Windows.Foreground;

/// <summary>
/// Reads the title of the web page shown inside a Chromium-based app (Electron, WebView2) through
/// UI Automation.
/// </summary>
/// <remarks>
/// <para>
/// Apps like the Claude desktop app keep their window title fixed ("Claude"), while the page inside
/// says what is open ("GitHub issue #102 - Claude Code"). Chromium exposes that page as a
/// <c>Document</c> element in the window's automation tree, named after the page title (#102).
/// </para>
/// <para>
/// Chromium builds that tree only once an automation client asks for it, so the first read for a
/// window may find nothing; the next poll finds it. Asking also turns on Chromium's accessibility
/// support in that app, which costs it some performance, so only the apps listed in
/// <see cref="ForegroundWatcherOptions.PageTitleApps"/> are read.
/// </para>
/// <para>Not thread-safe: create and use it on one STA thread (the foreground watcher's).</para>
/// </remarks>
internal sealed class PageTitleReader
{
    /// <summary>How deep to look for the document. In the Claude app it is ten levels down.</summary>
    private const int MaxDepth = 16;

    /// <summary>How many elements to visit at most, so a large tree can't stall the watcher.</summary>
    private const int MaxElements = 256;

    private IUIAutomation? _automation;
    private bool _unavailable;

    /// <summary>Returns the page title of the window, or null when there is none or it can't be read.</summary>
    public string? Read(HWND window)
    {
        if (window.IsNull || Automation() is not { } automation)
        {
            return null;
        }

        try
        {
            var root = automation.ElementFromHandle(window);
            var visited = 0;
            return Find(automation.RawViewWalker, root, 0, ref visited);
        }
        catch (COMException)
        {
            // The window closed or the app stopped answering; try again on the next change or poll.
            return null;
        }
    }

    private static string? Find(IUIAutomationTreeWalker walker, IUIAutomationElement element, int depth, ref int visited)
    {
        if (++visited > MaxElements)
        {
            return null;
        }

        if (element.CurrentControlType == UIA_CONTROLTYPE_ID.UIA_DocumentControlTypeId)
        {
            // Chromium can have more than one document (an empty one for hidden views); keep looking
            // in the siblings when this one has no name.
            var bstr = element.CurrentName;
            string? name;
            try
            {
                name = bstr.ToString();
            }
            finally
            {
                PInvoke.SysFreeString(bstr);
            }

            return string.IsNullOrWhiteSpace(name) ? null : Trim(name);
        }

        if (depth >= MaxDepth)
        {
            return null;
        }

        for (var child = walker.GetFirstChildElement(element); child is not null; child = walker.GetNextSiblingElement(child))
        {
            if (Find(walker, child, depth + 1, ref visited) is { } found)
            {
                return found;
            }

            if (visited > MaxElements)
            {
                return null;
            }
        }

        return null;
    }

    private static string Trim(string name)
    {
        name = name.Trim();
        return name.Length > WindowInspector.MaxTitleLength ? name[..WindowInspector.MaxTitleLength] : name;
    }

    private IUIAutomation? Automation()
    {
        if (_automation is null && !_unavailable)
        {
            try
            {
                _automation = (IUIAutomation)new CUIAutomation();
            }
            catch (COMException)
            {
                _unavailable = true;
            }
        }

        return _automation;
    }
}
