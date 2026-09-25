using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace GWTP_Windows_POC;

public partial class GuidanceWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private IntPtr _handle;
    private bool _manualPosition;

    public GuidanceWindow()
    {
        InitializeComponent();
        SourceInitialized += GuidanceWindow_SourceInitialized;
    }

    public void ShowNear(Rect targetBounds)
    {
        if (_manualPosition && IsVisible)
        {
            return;
        }

        if (!IsVisible)
        {
            Show();
            UpdateLayout();
        }

        if (_handle == IntPtr.Zero)
        {
            _handle = new WindowInteropHelper(this).Handle;
        }

        var screen = Forms.Screen.FromPoint(
            new System.Drawing.Point(
                (int)Math.Round(targetBounds.Left + (targetBounds.Width / 2)),
                (int)Math.Round(targetBounds.Top + (targetBounds.Height / 2))));

        var workArea = screen.WorkingArea;
        var width = Math.Max(ActualWidth, 340);
        var height = Math.Max(ActualHeight, 130);
        const int gap = 12;

        var x = targetBounds.Left + ((targetBounds.Width - width) / 2);
        var yBelow = targetBounds.Bottom + gap;
        var yAbove = targetBounds.Top - height - gap;
        var y = yBelow + height <= workArea.Bottom ? yBelow : yAbove;

        x = Math.Max(workArea.Left + gap, Math.Min(x, workArea.Right - width - gap));
        y = Math.Max(workArea.Top + gap, Math.Min(y, workArea.Bottom - height - gap));

        SetWindowPos(
            _handle,
            HwndTopmost,
            (int)Math.Round(x),
            (int)Math.Round(y),
            (int)Math.Round(width),
            (int)Math.Round(height),
            SwpNoActivate | SwpShowWindow);
    }

    public void ResetManualPosition()
    {
        _manualPosition = false;
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _manualPosition = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            _manualPosition = false;
        }
    }

    private void GuidanceWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
        style |= WsExToolWindow | WsExNoActivate;
        SetWindowLongPtr(_handle, GwlExStyle, new IntPtr(style));
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

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
        => IntPtr.Size == 8 ? SetWindowLongPtr64(windowHandle, index, newLong) : SetWindowLong32(windowHandle, index, newLong);
}
