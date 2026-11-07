using System.Windows.Automation;

namespace CaptionStudio;

internal static class LiveCaptionReader
{
    private static readonly PropertyCondition WindowClass =
        new(AutomationElement.ClassNameProperty, "LiveCaptionsDesktopWindow");

    private static readonly PropertyCondition CaptionId =
        new(AutomationElement.AutomationIdProperty, "CaptionsScrollViewer");

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
