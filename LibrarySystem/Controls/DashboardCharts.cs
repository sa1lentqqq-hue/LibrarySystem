using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LibrarySystem.Controls;

/// <summary>
/// Две небольшие диаграммы для главной страницы, нарисованные обычными элементами WPF (без внешних библиотек).
/// Столбцы и полосы плавно «вырастают» каждый раз, когда страница появляется на экране.
/// </summary>
public static class DashboardCharts
{
    private static readonly Brush CardBackground = Freeze(new SolidColorBrush(Color.FromArgb(105, 18, 12, 8)));
    private static readonly Brush TrackBackground = Freeze(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xD9, 0xBE)));

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Brush Resource(string key) => (Brush)Application.Current.FindResource(key);

    private static Border Card(string title, UIElement content)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title, FontFamily = (FontFamily)Application.Current.FindResource("HeadingFont"),
            FontWeight = FontWeights.Bold, FontSize = 16, Foreground = Resource("BrassBrush"), Margin = new Thickness(0, 0, 0, 12),
        });
        stack.Children.Add(content);
        return new Border
        {
            Width = 440, Margin = new Thickness(6), Padding = new Thickness(18, 14, 18, 16),
            CornerRadius = new CornerRadius(4), Background = CardBackground, Child = stack,
        };
    }

    /// <summary>Анимация роста: масштаб от 0 до 1 с задержкой (каждая следующая полоса — чуть позже).</summary>
    private static void Grow(FrameworkElement bar, DependencyProperty scaleProperty, int index)
    {
        if (bar.RenderTransform is not ScaleTransform scale) return;
        scale.BeginAnimation(scaleProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(650))
        {
            BeginTime = TimeSpan.FromMilliseconds(350 + index * 70),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>Горизонтальные полосы: сколько выдач в каждом жанре.</summary>
    public static FrameworkElement GenreChart(IReadOnlyList<(string Genre, long Count)> data)
    {
        const double trackWidth = 200;
        long max = Math.Max(1, data.Count == 0 ? 1 : data.Max(d => d.Count));
        var bars = new List<FrameworkElement>();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(trackWidth + 10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (int i = 0; i < data.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
            var (genre, count) = data[i];

            var label = new TextBlock { Text = genre, Foreground = LabelBrush, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(label, i);
            grid.Children.Add(label);

            var track = new Border { Height = 12, Width = trackWidth, CornerRadius = new CornerRadius(6), Background = TrackBackground, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var fill = new Border
            {
                Height = 12, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(count > 0 ? 6 : 0, trackWidth * count / max), Background = Resource("BrassBrush"),
                RenderTransformOrigin = new Point(0, 0.5), RenderTransform = new ScaleTransform(0, 1),
                ToolTip = $"{genre}: выдач — {count}",
            };
            track.Child = fill;
            Grid.SetRow(track, i);
            Grid.SetColumn(track, 1);
            grid.Children.Add(track);
            bars.Add(fill);

            var value = new TextBlock { Text = count.ToString(), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 2);
            grid.Children.Add(value);
        }

        Border card = Card("Выдачи по жанрам", grid);
        card.Loaded += (_, _) => { for (int i = 0; i < bars.Count; i++) Grow(bars[i], ScaleTransform.ScaleXProperty, i); };
        return card;
    }

    /// <summary>Вертикальные столбцы: сколько книг выдано в каждую из последних недель.</summary>
    public static FrameworkElement WeeklyChart(IReadOnlyList<(DateTime Start, long Count)> data)
    {
        const double areaHeight = 100;
        long max = Math.Max(1, data.Count == 0 ? 1 : data.Max(d => d.Count));
        var bars = new List<FrameworkElement>();

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(areaHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < data.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var (start, count) = data[i];

            var value = new TextBlock { Text = count.ToString(), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3) };
            Grid.SetColumn(value, i);
            grid.Children.Add(value);

            var bar = new Border
            {
                Width = 24, Height = Math.Max(count > 0 ? 4 : 2, areaHeight * count / max),
                VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center,
                CornerRadius = new CornerRadius(3, 3, 0, 0), Background = count > 0 ? Resource("BrassBrush") : TrackBackground,
                RenderTransformOrigin = new Point(0.5, 1), RenderTransform = new ScaleTransform(1, 0),
                ToolTip = $"Неделя с {start:dd.MM.yyyy}: выдач — {count}",
            };
            Grid.SetRow(bar, 1);
            Grid.SetColumn(bar, i);
            grid.Children.Add(bar);
            bars.Add(bar);

            var label = new TextBlock { Text = start.ToString("dd.MM"), Foreground = LabelBrush, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            Grid.SetRow(label, 2);
            Grid.SetColumn(label, i);
            grid.Children.Add(label);
        }

        Border card = Card("Выдачи по неделям", grid);
        card.Loaded += (_, _) => { for (int i = 0; i < bars.Count; i++) Grow(bars[i], ScaleTransform.ScaleYProperty, i); };
        return card;
    }
}
