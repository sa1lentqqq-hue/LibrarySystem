using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LibrarySystem.Services;

namespace LibrarySystem.Controls;

/// <summary>
/// Всплывающие уведомления в правом нижнем углу окна: появляются плавно, сами исчезают, не блокируют работу
/// (в отличие от окна MessageBox). Клик закрывает уведомление, наведение мыши останавливает таймер.
/// </summary>
public sealed class ToastPresenter
{
    private const int MaxVisible = 4;
    private readonly Panel _host;

    public ToastPresenter(Panel host) => _host = host;

    public void Show(string message, ToastKind kind)
    {
        // больше четырёх одновременно не показываем: самое старое убираем сразу
        while (_host.Children.Count >= MaxVisible) _host.Children.RemoveAt(0);

        FrameworkElement toast = Build(message, kind);
        _host.Children.Add(toast);

        var shift = (TranslateTransform)toast.RenderTransform;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        toast.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)));
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(48, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });

        TimeSpan lifetime = kind switch
        {
            ToastKind.Error => TimeSpan.FromSeconds(6.5),
            ToastKind.Warning => TimeSpan.FromSeconds(4.5),
            _ => TimeSpan.FromSeconds(3.2),
        };
        var timer = new DispatcherTimer { Interval = lifetime };
        timer.Tick += (_, _) => { timer.Stop(); Dismiss(toast); };
        timer.Start();

        toast.MouseEnter += (_, _) => timer.Stop();                // пока курсор над уведомлением, оно не исчезает
        toast.MouseLeave += (_, _) => { timer.Interval = TimeSpan.FromSeconds(1.5); timer.Start(); };
        toast.MouseLeftButtonUp += (_, _) => { timer.Stop(); Dismiss(toast); };
    }

    private void Dismiss(FrameworkElement toast)
    {
        if (!_host.Children.Contains(toast) || !toast.IsHitTestVisible) return;
        toast.IsHitTestVisible = false;

        var shift = (TranslateTransform)toast.RenderTransform;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => _host.Children.Remove(toast);
        toast.BeginAnimation(UIElement.OpacityProperty, fade);
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 36, TimeSpan.FromMilliseconds(220)));
    }

    private FrameworkElement Build(string message, ToastKind kind)
    {
        (string icon, string brushKey) = kind switch
        {
            ToastKind.Success => ("✓", "SuccessBrush"),
            ToastKind.Warning => ("!", "AccentBrush"),
            ToastKind.Error => ("✕", "DangerBrush"),
            _ => ("i", "GreenBrush"),
        };
        var accent = (Brush)Application.Current.FindResource(brushKey);

        var content = new DockPanel();
        content.Children.Add(new Border { Width = 5, Background = accent, CornerRadius = new CornerRadius(4, 0, 0, 4) });
        DockPanel.SetDock(content.Children[0], Dock.Left);

        var body = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 16, 10) };
        body.Children.Add(new TextBlock
        {
            Text = icon, Foreground = accent, FontSize = 18, FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
        });
        body.Children.Add(new TextBlock
        {
            Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 330,
            Foreground = (Brush)Application.Current.FindResource("TextBrush"), VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(body);

        var card = new Border
        {
            Background = (Brush)Application.Current.FindResource("CardBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("LineBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Child = content,
        };

        // мягкая тень из полупрозрачных слоёв: дешевле DropShadowEffect, анимация не тормозит
        var root = new Grid
        {
            Margin = new Thickness(0, 0, 0, 10), Cursor = Cursors.Hand, Opacity = 0,
            RenderTransform = new TranslateTransform(48, 0),
            CacheMode = new BitmapCache(),
        };
        root.Children.Add(new Border { CornerRadius = new CornerRadius(7), Margin = new Thickness(-5), Background = new SolidColorBrush(Color.FromArgb(18, 0, 0, 0)) });
        root.Children.Add(new Border { CornerRadius = new CornerRadius(5), Margin = new Thickness(-2), Background = new SolidColorBrush(Color.FromArgb(28, 0, 0, 0)) });
        root.Children.Add(card);
        return root;
    }
}
