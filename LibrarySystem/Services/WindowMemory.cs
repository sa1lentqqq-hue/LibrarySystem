using System.Windows;

namespace LibrarySystem.Services;

/// <summary>
/// Запоминает состояние окна (во весь экран или обычное) и его положение.
/// Следующее окно (вход → главное → снова вход) открывается в том же состоянии и там же,
/// где пользователь оставил предыдущее.
/// </summary>
public static class WindowMemory
{
    private static WindowState _state = WindowState.Normal;
    private static Point? _center;          // центр окна в обычном состоянии

    public static void Attach(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;

        Rect area = SystemParameters.WorkArea;
        double width = Math.Min(window.Width, area.Width);
        double height = Math.Min(window.Height, area.Height);
        Point center = _center ?? new Point(area.Left + area.Width / 2, area.Top + area.Height / 2);

        // окно целиком остаётся в пределах видимой области экранов
        double left = center.X - width / 2, top = center.Y - height / 2;
        double minX = SystemParameters.VirtualScreenLeft, maxX = minX + SystemParameters.VirtualScreenWidth - width;
        double minY = SystemParameters.VirtualScreenTop, maxY = minY + SystemParameters.VirtualScreenHeight - height;
        window.Left = Math.Max(minX, Math.Min(left, maxX));
        window.Top = Math.Max(minY, Math.Min(top, maxY));

        if (_state == WindowState.Maximized)
            window.WindowState = WindowState.Maximized;

        window.StateChanged += (_, _) => Remember(window);
        window.LocationChanged += (_, _) => Remember(window);
        window.Closing += (_, _) => Remember(window);
    }

    private static void Remember(Window window)
    {
        if (window.WindowState == WindowState.Minimized) return;
        _state = window.WindowState;

        Rect bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
            : window.RestoreBounds;
        if (bounds.Width > 0 && bounds.Height > 0)
            _center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }
}
