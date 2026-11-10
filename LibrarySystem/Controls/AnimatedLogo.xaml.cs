using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LibrarySystem.Controls;

public partial class AnimatedLogo : UserControl
{
    public AnimatedLogo() => InitializeComponent();

    /// <summary>
    /// Книга раскрывается: сначала вырастает корешок, затем обе половины «распахиваются» с лёгким
    /// пружинным эффектом, после чего на страницах одна за другой проступают строки текста.
    /// </summary>
    public void Play()
    {
        Animate(SpineScale, ScaleTransform.ScaleYProperty, 0, 1, 0, 450, new CubicEase { EasingMode = EasingMode.EaseOut });

        var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };
        Animate(LeftScale, ScaleTransform.ScaleXProperty, 0, 1, 350, 1000, spring);
        Animate(RightScale, ScaleTransform.ScaleXProperty, 0, 1, 350, 1000, spring);

        UIElement[] lines = { L1, R1, L2, R2, L3, R3 };
        for (int i = 0; i < lines.Length; i++)
            Animate(lines[i], OpacityProperty, 0, 1, 1150 + i * 90, 300, null);
    }

    private static void Animate(IAnimatable target, DependencyProperty property, double from, double to,
                                int beginMs, int durationMs, IEasingFunction? easing)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
            EasingFunction = easing,
        };
        target.BeginAnimation(property, animation);
    }
}
