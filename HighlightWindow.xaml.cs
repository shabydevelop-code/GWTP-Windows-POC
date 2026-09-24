using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GWTP_Windows_POC;

public partial class HighlightWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private IntPtr _handle;

    public HighlightWindow()
    {
        InitializeComponent();
        SourceInitialized += HighlightWindow_SourceInitialized;
    }

    public void ShowAt(Rect bounds)
    {
        if (!IsVisible)
        {
            Show();
        }

        if (_handle == IntPtr.Zero)
        {
            _handle = new WindowInteropHelper(this).Handle;
        }

        const int margin = 4;
        SetWindowPos(
            _handle,
            HwndTopmost,
            (int)Math.Round(bounds.Left) - margin,
            (int)Math.Round(bounds.Top) - margin,
            Math.Max(1, (int)Math.Round(bounds.Width) + (margin * 2)),
            Math.Max(1, (int)Math.Round(bounds.Height) + (margin * 2)),
            SwpNoActivate | SwpShowWindow);
    }

    private void HighlightWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
        style |= WsExTransparent | WsExToolWindow | WsExNoActivate;
        SetWindowLongPtr(_handle, GwlExStyle, new IntPtr(style));
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern IntPtr GetWindowLong32(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr windowHandle, int index, IntPtr newLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLong32(IntPtr windowHandle, int index, IntPtr newLong);

    private static IntPtr GetWindowLongPtr(IntPtr windowHandle, int index)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(windowHandle, index) : GetWindowLong32(windowHandle, index);

    private static IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newLong)
        => IntPtr.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, newLong)
            : SetWindowLong32(windowHandle, index, newLong);
}
