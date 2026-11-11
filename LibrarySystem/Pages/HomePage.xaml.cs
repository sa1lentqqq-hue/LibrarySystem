using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using LibrarySystem.Controls;
using LibrarySystem.Data;
using LibrarySystem.Services;
using LibrarySystem.Views;

namespace LibrarySystem.Pages;

/// <summary>Главное меню: показатели библиотеки (расчёт по БД) и пилюли быстрого перехода. Элементы появляются «каскадом».</summary>
public partial class HomePage : UserControl, IRefreshable
{
    public HomePage()
    {
        InitializeComponent();
        BuildQuickNav();
        LoadStats();

        Loaded += (_, _) => PlayCascade();
    }

    private sealed record NavItem(string Key, string Glyph, string Title);

    /// <summary>Ряд пилюль «Быстрый переход»: все разделы перед глазами, без раскрывающихся списков.</summary>
    private void BuildQuickNav()
    {
        var items = new List<NavItem>
        {
            new("books", "📚", "Фонд книг"),
            new("readers", "👥", "Читатели"),
            new("loans", "🔁", "Выдача и возврат"),
            new("fines", "💰", "Штрафы"),
            new("reports", "📊", "Отчёты"),
            new("about", "ℹ", "О программе"),
            new("logout", "🚪", "Сменить пользователя"),
            new("exit", "🚪", "Выход"),
        };
        if (Session.IsAdmin)
            items.InsertRange(4, new[]
            {
                new NavItem("genres", "🏷", "Жанры"),
                new NavItem("users", "🔑", "Пользователи"),
                new NavItem("log", "📜", "Журнал действий"),
            });

        foreach (var item in items) AddPill(item);
    }

    /// <summary>
    /// Пилюля раздела: иконка и подпись. Наведение — плавная латунная подсветка и лёгкое «приподнятие»,
    /// нажатие — короткое «вдавливание» и открытие раздела. Вместо уродского выпадающего списка.
    /// </summary>
    private void AddPill(NavItem item)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = item.Glyph, FontSize = 15, Margin = new Thickness(0, 0, 8, 0),
            Foreground = (Brush)FindResource("BrassBrush"), VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = item.Title, Foreground = Brushes.White, FontSize = 13,
            FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
        });

        var pill = new Border
        {
            Child = content, Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(15, 8, 15, 8), CornerRadius = new CornerRadius(17),
            Background = new SolidColorBrush(PillColor),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 214, 170, 84)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = "Перейти: " + item.Title,
        };

        var scale = new ScaleTransform();
        pill.RenderTransformOrigin = new Point(0.5, 0.5);
        pill.RenderTransform = scale;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        pill.MouseEnter += (_, _) =>
        {
            pill.Background.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(Color.FromArgb(150, 166, 116, 31), TimeSpan.FromMilliseconds(160)));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        };
        pill.MouseLeave += (_, _) =>
        {
            pill.Background.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(PillColor, TimeSpan.FromMilliseconds(220)));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        };
        pill.MouseLeftButtonUp += (_, _) => MainWindow.Current.Navigate(item.Key);

        NavPanel.Children.Add(pill);
    }

    /// <summary>Спокойный полупрозрачный фон пилюли — мягко читается на дереве стеллажей.</summary>
    private static readonly Color PillColor = Color.FromArgb(64, 251, 246, 234);

    /// <summary>Показатели пересчитываются при возврате на главную.</summary>
    public void Refresh() => LoadStats();

    private void LoadStats()
    {
        StatsPanel.Children.Clear();
        AddStat("Книг в фонде", Database.Scalar<long>("SELECT COALESCE(SUM(copies_total), 0) FROM books").ToString(), "books", null);
        AddStat("Читателей", Database.Scalar<long>("SELECT COUNT(*) FROM readers").ToString(), "readers", null);
        AddStat("Книг на руках", Database.Scalar<long>("SELECT COUNT(*) FROM loans WHERE returned_at IS NULL").ToString(), "loans", "На руках");
        AddStat("Просрочено", Database.Scalar<long>(
            "SELECT COUNT(*) FROM loans WHERE returned_at IS NULL AND date(due_at) < date('now','localtime')").ToString(), "loans", "Просрочена");
        AddStat("Долг по штрафам, руб.", Database.Scalar<double>("SELECT COALESCE(ROUND(SUM(amount), 2), 0) FROM fines WHERE paid = 0").ToString("N2"), "fines", "Не оплачен");
        LoadAlert();
        LoadCharts();
    }

    /// <summary>Плашка вверху: есть ли просроченные книги. Щелчок открывает «Выдачу» сразу с фильтром «Просрочена».</summary>
    private void LoadAlert()
    {
        var (loans, readers) = DashboardService.GetOverdue();
        bool overdue = loans > 0;

        var row = new DockPanel();
        var icon = new TextBlock
        {
            Text = overdue ? "⚠" : "✓", Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0),
        };
        row.Children.Add(icon);
        DockPanel.SetDock(icon, Dock.Left);

        if (overdue)
        {
            var open = new TextBlock { Text = "Открыть список →", Foreground = (Brush)FindResource("BrassBrush"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(open);
            DockPanel.SetDock(open, Dock.Right);
        }
        row.Children.Add(new TextBlock
        {
            Text = overdue ? $"Просрочено книг: {loans}  ·  читателей с долгом по срокам: {readers}" : "Просроченных книг нет — всё в порядке",
            Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
        });

        var banner = new Border
        {
            Padding = new Thickness(18, 11, 18, 11), CornerRadius = new CornerRadius(4), Child = row,
            Background = new SolidColorBrush(overdue ? Color.FromArgb(215, 142, 47, 59) : Color.FromArgb(190, 62, 112, 80)),
        };
        if (overdue)
        {
            banner.Cursor = Cursors.Hand;
            banner.ToolTip = "Открыть выдачи со статусом «Просрочена»";
            banner.MouseLeftButtonUp += (_, _) => MainWindow.Current.Navigate("loans", "Просрочена");
        }
        AlertHost.Child = banner;
    }

    /// <summary>Две диаграммы: выдачи по жанрам и по неделям.</summary>
    private void LoadCharts()
    {
        ChartsPanel.Children.Clear();
        ChartsPanel.Children.Add(DashboardCharts.GenreChart(DashboardService.GetGenreLoans()));
        ChartsPanel.Children.Add(DashboardCharts.WeeklyChart(DashboardService.GetWeeklyLoans(8)));
    }

    /// <summary>Показатели для статистики.</summary>
    private void AddStat(string title, string value, string key, string? filter)
    {
        var card = new Border
        {
            Width = 232, Height = 102, Margin = new Thickness(6), CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(70, 251, 246, 234)),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = title, Foreground = new SolidColorBrush(Color.FromArgb(220, 251, 246, 234)),
                        HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6),
                    },
                    new TextBlock
                    {
                        Text = value, Foreground = Brushes.White, FontSize = 30, FontWeight = FontWeights.Bold,
                        FontFamily = (FontFamily)FindResource("HeadingFont"), HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            },
        };
        card.Cursor = Cursors.Hand;
        card.ToolTip = "Открыть список";
        card.MouseEnter += (_, _) => card.Background = new SolidColorBrush(Color.FromArgb(105, 251, 246, 234));
        card.MouseLeave += (_, _) => card.Background = new SolidColorBrush(Color.FromArgb(70, 251, 246, 234));
        card.MouseLeftButtonUp += (_, _) => MainWindow.Current.Navigate(key, filter);
        StatsPanel.Children.Add(card);
    }

    /// <summary>Карточки и плитки по очереди проявляются и «всплывают» снизу — плавное, живое появление меню.</summary>
    private void PlayCascade()
    {
        var elements = new List<UIElement> { AlertHost, NavPanel, StatsHeader };
        elements.AddRange(StatsPanel.Children.Cast<UIElement>());
        elements.Add(ChartsHeader);
        elements.AddRange(ChartsPanel.Children.Cast<UIElement>());

        int index = 0;
        foreach (UIElement element in elements)
        {
            var shift = new TranslateTransform(0, 26);
            element.RenderTransform = shift;
            element.Opacity = 0;

            TimeSpan delay = TimeSpan.FromMilliseconds(120 + index * 55);
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { BeginTime = delay });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(26, 0, TimeSpan.FromMilliseconds(480))
            {
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
            index++;
        }
    }
}
