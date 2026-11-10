using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LibrarySystem.Controls;
using LibrarySystem.Data;
using LibrarySystem.Pages;
using LibrarySystem.Services;

namespace LibrarySystem.Views;

/// <summary>
/// Главное окно-оболочка: шапка, поиск, «хлебные крошки», кнопка «Назад», подвал и сменяемые страницы.
/// Экраны хранятся в стеке: «Главная → Раздел → Результаты поиска → …». Назад возвращает на предыдущий
/// экран с сохранением введённого фильтра; любая «хлебная крошка» переносит на свой уровень.
/// </summary>
public partial class MainWindow : Window
{
    private readonly List<Screen> _stack = new();
    private readonly DispatcherTimer _suggestTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private bool _suppressSearchEvents;
    private int _navToken;

    /// <summary>Нужно ли вернуться к окну входа (кнопка «Сменить пользователя»).</summary>
    public bool LogoutRequested { get; private set; }

    /// <summary>Текущее главное окно — страницы используют его для перехода между разделами.</summary>
    public static MainWindow Current { get; private set; } = null!;

    public MainWindow()
    {
        InitializeComponent();
        WindowTheme.Apply(this);       // тёмная полоса заголовка
        WindowMemory.Attach(this);     // то же состояние окна (во весь экран / обычное), что и при входе
        Current = this;
        Ui.ToastHandler = new ToastPresenter(ToastHost).Show;    // всплывающие уведомления вместо окон сообщений
        Closed += (_, _) => Ui.ToastHandler = null;
        _suggestTimer.Tick += (_, _) => { _suggestTimer.Stop(); ShowSuggestions(); };
        Deactivated += (_, _) => HideSuggestions();
        Loaded += (_, _) => Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));

        UserText.Text = $"Пользователь: {Session.FullName} ({Session.RoleName})";
        FooterText.Text = $"Библиотечная система v{AppConfig.Version}   |   {AppConfig.AuthorShort}, гр. {AppConfig.Group}   |   {DateTime.Now:dd.MM.yyyy}";
        Navigate("home");
    }

    // ------------------------------------------------------------------ навигация

    /// <summary>Переход в раздел из главного меню (права проверяются в PageFactory).</summary>
    public void Navigate(string key, string? filter = null)
    {
        if (key == "logout") { Audit.Log("Выход из системы"); LogoutRequested = true; Close(); return; }
        if (key == "exit")   { Audit.Log("Выход из системы"); Close(); return; }

        Screen? screen = PageFactory.Create(key, filter);
        if (screen is null)
        {
            Ui.Error("Недостаточно прав для открытия этого раздела.");
            return;
        }

        if (key == "home")
        {
            _stack.Clear();
            _stack.Add(screen);
        }
        else
        {
            Screen home = _stack.Count > 0 ? _stack[0] : PageFactory.Create("home")!;
            _stack.Clear();
            _stack.Add(home);
            _stack.Add(screen);
        }
        Display(screen, forward: true);
    }

    /// <summary>Открывает раздел поверх текущего экрана (например, запись из результатов поиска) — «Назад» вернёт обратно.</summary>
    public void OpenSection(string key, string? filter)
    {
        Screen? screen = PageFactory.Create(key, filter);
        if (screen is null) return;
        Push(screen);
    }

    private void Push(Screen screen)
    {
        _stack.Add(screen);
        Display(screen, forward: true);
    }

    private void ReplaceTop(Screen screen)
    {
        _stack[^1] = screen;
        Display(screen, forward: true);
    }

    /// <summary>На шаг назад (кнопка «Назад», Alt + ←, боковая кнопка мыши).</summary>
    public void GoBack()
    {
        if (_stack.Count < 2) return;
        _stack.RemoveAt(_stack.Count - 1);
        Display(_stack[^1], forward: false);
    }

    /// <summary>Перейти на выбранный уровень «хлебных крошек».</summary>
    private void GoTo(int index)
    {
        if (index < 0 || index >= _stack.Count - 1) return;
        _stack.RemoveRange(index + 1, _stack.Count - index - 1);
        Display(_stack[index], forward: false);
    }

    private void Display(Screen screen, bool forward)
    {
        // вернувшись на страницу, обновляем данные: пока пользователь был в другом разделе, они могли измениться
        if (!forward && screen.Page is IRefreshable refreshable) refreshable.Refresh();

        TitleText.Text = screen.Title;
        BackButton.Visibility = _stack.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        BuildCrumbs();

        // Поиск: на главном меню он не нужен — скрыт. При переходе на новый экран строка очищается;
        // на странице результатов в ней остаётся запрос, по которому эти результаты получены.
        SearchArea.Visibility = screen.Page is HomePage ? Visibility.Hidden : Visibility.Visible;
        SetSearchText(screen.Page is SearchPage results ? results.Query : "");

        AnimateIn(screen.Page, forward);
    }

    /// <summary>
    /// Переход: вперёд — страница «подъезжает» справа, назад — слева, и плавно проявляется.
    /// Пока идёт анимация, страница кэшируется как картинка — таблицы не перерисовываются на каждом кадре, поэтому нет лагов.
    /// После окончания кэш и анимации снимаются, чтобы страница снова рисовалась чётко и не держала ресурсы.
    /// </summary>
    private void AnimateIn(UserControl page, bool forward)
    {
        int token = ++_navToken;
        double from = forward ? 32 : -32;

        page.BeginAnimation(OpacityProperty, null);
        page.Opacity = 0;
        page.RenderTransform = new TranslateTransform(from, 0);
        page.CacheMode = new BitmapCache(VisualTreeHelper.GetDpi(this).DpiScaleX);   // чёткая картинка и на экранах с масштабом 125–200 %
        PageHost.Content = page;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        var slide = new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
        slide.Completed += (_, _) =>
        {
            if (token != _navToken) return;      // за это время уже открыт другой экран
            page.Opacity = 1;
            page.BeginAnimation(OpacityProperty, null);
            page.RenderTransform = null;
            page.CacheMode = null;
        };
        page.BeginAnimation(OpacityProperty, fade);
        ((TranslateTransform)page.RenderTransform).BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private void BuildCrumbs()
    {
        CrumbPanel.Children.Clear();
        for (int i = 0; i < _stack.Count; i++)
        {
            string text = _stack[i].CrumbText;
            if (i < _stack.Count - 1)
            {
                int target = i;
                var link = new Hyperlink(new Run(text)) { Foreground = (Brush)FindResource("PrimaryBrush"), ToolTip = "Перейти: " + text };
                link.Click += (_, _) => GoTo(target);
                CrumbPanel.Children.Add(new TextBlock(link));
                CrumbPanel.Children.Add(new TextBlock { Text = "   ›   ", Foreground = (Brush)FindResource("MutedBrush") });
            }
            else
            {
                CrumbPanel.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold });
            }
        }
    }

    // ------------------------------------------------------------------ обработчики

    private void OnBack(object sender, RoutedEventArgs e) => GoBack();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Left && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)       // Alt + ←
        {
            GoBack();
            e.Handled = true;
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // щелчок мимо строки поиска и списка подсказок закрывает подсказки
        if (SuggestPanel.Visibility == Visibility.Visible && e.OriginalSource is DependencyObject clicked
            && !IsInside(clicked, SuggestPanel) && !IsInside(clicked, SearchBox))
            HideSuggestions();

        if (e.ChangedButton == MouseButton.XButton1)                                // боковая кнопка мыши «Назад»
        {
            GoBack();
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ поиск и подсказки

    private void OnSearch(object sender, RoutedEventArgs e)
    {
        HideSuggestions();
        string q = SearchBox.Text.Trim();
        if (q.Length == 0)
        {
            Ui.Warn("Введите текст для поиска по книгам и читателям.");
            return;
        }
        string shortQuery = q.Length > 24 ? q[..24] + "…" : q;
        var screen = new Screen(new SearchPage(q), "Результаты поиска", $"Поиск: «{shortQuery}»");

        // повторный поиск заменяет прежние результаты, а не накапливает экраны в стеке
        if (_stack.Count > 0 && _stack[^1].Page is SearchPage) ReplaceTop(screen);
        else Push(screen);
    }

    /// <summary>Меняет текст в строке поиска программно (очистка при переходе, выбор подсказки) — без показа подсказок.</summary>
    private void SetSearchText(string text)
    {
        _suppressSearchEvents = true;
        if (SearchBox.Text != text)
        {
            SearchBox.Text = text;
            SearchBox.CaretIndex = text.Length;
        }
        _suppressSearchEvents = false;
        UpdateSearchChrome();
        _suggestTimer.Stop();
        HideSuggestions();
    }

    /// <summary>Крестик очистки виден, только когда есть что очищать; серая подпись — только в пустой строке.</summary>
    private void UpdateSearchChrome()
    {
        bool has = SearchBox.Text.Length > 0;
        SearchClear.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        SearchHint.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchChrome();
        if (_suppressSearchEvents) return;
        _suggestTimer.Stop();                 // подсказки появляются, когда ввод на миг притих, — набор текста не тормозит
        if (SearchBox.Text.Trim().Length == 0) HideSuggestions();
        else _suggestTimer.Start();
    }

    private void OnSearchClear(object sender, RoutedEventArgs e)
    {
        SetSearchText("");
        SearchBox.Focus();
        if (_stack.Count > 0 && _stack[^1].Page is SearchPage) GoBack();    // результаты очищенного запроса не нужны
    }

    private void OnSearchGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        SuggestService.Invalidate();          // список книг и читателей мог измениться — перечитаем при вводе
        if (SearchBox.Text.Trim().Length > 0 && !(_stack.Count > 0 && _stack[^1].Page is SearchPage)) ShowSuggestions();
    }

    private void OnSearchLostFocus(object sender, KeyboardFocusChangedEventArgs e) => HideSuggestions();

    private void ShowSuggestions()
    {
        string q = SearchBox.Text.Trim();
        if (q.Length == 0 || !SearchBox.IsKeyboardFocused || SearchArea.Visibility != Visibility.Visible)
        {
            HideSuggestions();
            return;
        }

        IReadOnlyList<Suggestion> found = SuggestService.Find(q);
        if (found.Count == 0) { HideSuggestions(); return; }

        SuggestList.ItemsSource = found;
        SuggestList.SelectedIndex = -1;
        if (SuggestPanel.Visibility != Visibility.Visible)
        {
            SuggestPanel.Opacity = 0;
            SuggestPanel.Visibility = Visibility.Visible;
            SuggestPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        }
    }

    private void HideSuggestions()
    {
        if (SuggestPanel.Visibility != Visibility.Visible) return;
        SuggestPanel.BeginAnimation(OpacityProperty, null);
        SuggestPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>Выбор подсказки: текст подставляется в строку и сразу выполняется поиск.</summary>
    private void PickSuggestion(Suggestion suggestion)
    {
        SetSearchText(suggestion.Text);
        OnSearch(this, new RoutedEventArgs());
    }

    private void OnSuggestClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(SuggestList, source) is ListBoxItem { DataContext: Suggestion suggestion })
        {
            PickSuggestion(suggestion);
            e.Handled = true;
        }
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        bool open = SuggestPanel.Visibility == Visibility.Visible;
        switch (e.Key)
        {
            case Key.Down:
                if (open) SuggestList.SelectedIndex = Math.Min(SuggestList.SelectedIndex + 1, SuggestList.Items.Count - 1);
                else ShowSuggestions();
                e.Handled = true;
                break;

            case Key.Up:
                if (open) { SuggestList.SelectedIndex = Math.Max(SuggestList.SelectedIndex - 1, -1); e.Handled = true; }
                break;

            case Key.Enter:
                if (open && SuggestList.SelectedItem is Suggestion chosen) PickSuggestion(chosen);
                else OnSearch(sender, e);
                e.Handled = true;
                break;

            case Key.Escape:
                if (open)                     // первое нажатие закрывает подсказки, второе — очищает поиск
                {
                    HideSuggestions();
                }
                else                          // Esc: очистить поиск и вернуться к списку
                {
                    SetSearchText("");
                    if (_stack.Count > 0 && _stack[^1].Page is SearchPage) GoBack();
                }
                e.Handled = true;
                break;

            case Key.Tab:
                HideSuggestions();
                break;
        }
    }

    private static bool IsInside(DependencyObject? element, DependencyObject root)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, root)) return true;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
}
