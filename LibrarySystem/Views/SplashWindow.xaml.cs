using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LibrarySystem.Data;

namespace LibrarySystem.Views;

/// <summary>Заставка при запуске. По окончании анимации плавно гаснет; после этого открывается окно входа.</summary>
public partial class SplashWindow : Window
{
    private const int FadeStartMs = 3300;     // когда начинается угасание
    private const int FadeMs = 450;

    private readonly TaskCompletionSource _finished = new();
    private bool _closing;

    public Task Finished => _finished.Task;

    public SplashWindow()
    {
        InitializeComponent();
        FooterText.Text = $"Версия {AppConfig.Version}   ·   {AppConfig.AuthorShort}, гр. {AppConfig.Group}";
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Logo.Play();

        Animate(Glow, OpacityProperty, 0, 1, 300, 1400);
        Animate(TitleText, OpacityProperty, 0, 1, 1100, 650);
        Animate(TitleShift, TranslateTransform.YProperty, 18, 0, 1100, 650, new CubicEase { EasingMode = EasingMode.EaseOut });
        Animate(SubText, OpacityProperty, 0, 1, 1500, 650);
        Animate(LoaderFill, WidthProperty, 0, 280, 500, 2600, new SineEase { EasingMode = EasingMode.EaseInOut });

        // плавное угасание всего окна, затем закрытие
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(FadeMs)) { BeginTime = TimeSpan.FromMilliseconds(FadeStartMs) };
        fade.Completed += (_, _) => Finish();
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Щелчок или клавиша — не ждать окончания, быстро погасить заставку.</summary>
    private void OnSkip(object sender, MouseButtonEventArgs e) => Skip();
    private void OnKey(object sender, KeyEventArgs e) => Skip();

    private void Skip()
    {
        if (_closing) return;
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => Finish();
        BeginAnimation(OpacityProperty, fade);
    }

    private void Finish()
    {
        if (_closing) return;
        _closing = true;
        Close();
        _finished.TrySetResult();
    }

    private static void Animate(IAnimatable target, DependencyProperty property, double from, double to,
                                int beginMs, int durationMs, IEasingFunction? easing = null)
    {
        target.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
            EasingFunction = easing,
        });
    }
}
