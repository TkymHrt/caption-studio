using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Automation;

namespace CaptionStudio;

internal static class LiveCaptionReader
{
    private static readonly PropertyCondition WindowClass =
        new(AutomationElement.ClassNameProperty, "LiveCaptionsDesktopWindow");

    private static readonly PropertyCondition CaptionId =
        new(AutomationElement.AutomationIdProperty, "CaptionsScrollViewer");

    public static bool TryOpenWindow()
    {
        try
        {
            var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "LiveCaptions.exe"))
            {
                UseShellExecute = true,
            };
            using var process = Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    public static (int WindowHandle, string? Text) Read()
    {
        var window = AutomationElement.RootElement.FindFirst(TreeScope.Children, WindowClass);
        if (window is null)
        {
            return (0, null);
        }

        var caption = window.FindFirst(TreeScope.Descendants, CaptionId);
        return (window.Current.NativeWindowHandle, caption?.Current.Name);
    }
}
