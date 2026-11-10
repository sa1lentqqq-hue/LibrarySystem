using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LibrarySystem.Controls;

public partial class AnimatedBackground : UserControl
{
    private static readonly Color[] SpineColors =
    {
        Color.FromRgb(122, 40, 50), Color.FromRgb(48, 84, 64), Color.FromRgb(42, 58, 96),
        Color.FromRgb(122, 86, 48), Color.FromRgb(150, 112, 50), Color.FromRgb(78, 58, 48),
    };

    private static readonly SolidColorBrush[] SpineBrushes = SpineColors.Select(Frozen).ToArray();
    private static readonly SolidColorBrush BandBrush = Frozen(Color.FromRgb(214, 170, 84));
    private static readonly SolidColorBrush BoardBrush = Frozen(Color.FromRgb(59, 42, 32));
    private static readonly SolidColorBrush PaperBrush = Frozen(Color.FromArgb(48, 243, 234, 215));
    private static readonly SolidColorBrush LineBrush = Frozen(Color.FromArgb(40, 243, 234, 215));

    private const int PageCount = 12;

    private readonly DispatcherTimer _resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private double _builtWidth;
    private double _builtHeight;

    public AnimatedBackground()
    {
        InitializeComponent();
        _resizeTimer.Tick += (_, _) => { _resizeTimer.Stop(); Rebuild(); };
        Loaded += (_, _) => Rebuild();
        Unloaded += (_, _) =>
        {
            // фон ушёл с экрана — листы удаляются вместе с их анимациями; при возвращении создаются заново
            _resizeTimer.Stop();
            PagesLayer.Children.Clear();
            _builtWidth = _builtHeight = 0;
        };
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();      // замороженные кисти дешевле: не отслеживают изменения и общие для всех фигур
        return brush;
    }

    // Размер окна меняется десятки раз в секунду, пока его тянут мышью. Полку и листы пересоздаём один раз —
    // когда размер перестал меняться (раньше они перестраивались на каждое событие, и окно «тормозило»).
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void Rebuild()
    {
        if (ActualWidth < 50 || ActualHeight < 50) return;
        if (Math.Abs(ActualWidth - _builtWidth) < 1 && Math.Abs(ActualHeight - _builtHeight) < 1 && PagesLayer.Children.Count > 0) return;
        BuildShelf();
        BuildPages();
        _builtWidth = ActualWidth;
        _builtHeight = ActualHeight;
    }

    /// <summary>Книжная полка: корешки разной ширины, высоты и цвета на деревянной доске (рисуется один раз, слой кэшируется).</summary>
    private void BuildShelf()
    {
        ShelfLayer.Children.Clear();
        var rnd = new Random(5);
        double x = 0, baseY = ShelfLayer.Height - 14;
        while (x < ActualWidth)
        {
            double w = 14 + rnd.Next(0, 14);
            double h = 84 * (0.62 + rnd.NextDouble() * 0.38);
            var spine = new Rectangle { Width = w - 2, Height = h, Fill = SpineBrushes[rnd.Next(SpineBrushes.Length)] };
            Canvas.SetLeft(spine, x);
            Canvas.SetTop(spine, baseY - h);
            ShelfLayer.Children.Add(spine);

            var stripe = new Rectangle { Width = w - 2, Height = 3, Fill = BandBrush };
            Canvas.SetLeft(stripe, x);
            Canvas.SetTop(stripe, baseY - h + 10);
            ShelfLayer.Children.Add(stripe);
            x += w;
        }
        var board = new Rectangle { Width = ActualWidth, Height = 14, Fill = BoardBrush };
        Canvas.SetTop(board, baseY);
        ShelfLayer.Children.Add(board);
    }

    /// <summary>
    /// Листы бумаги: медленно опускаются, покачиваются и вращаются. Каждый лист — одна кэшированная картинка,
    /// которую видеокарта просто двигает и поворачивает; падение идёт через transform (а не через Canvas.Top),
    /// поэтому на каждом кадре не пересчитывается разметка.
    /// </summary>
    private void BuildPages()
    {
        PagesLayer.Children.Clear();
        var rnd = new Random(12);
        for (int i = 0; i < PageCount; i++)
        {
            double size = 40 + rnd.NextDouble() * 40;
            var page = new Canvas { Width = size * 0.7, Height = size, IsHitTestVisible = false };
            page.Children.Add(new Rectangle { Width = page.Width, Height = page.Height, Fill = PaperBrush });
            for (int l = 1; l < 5; l++)
            {
                var ln = new Rectangle { Width = page.Width * 0.6, Height = 1, Fill = LineBrush };
                Canvas.SetLeft(ln, page.Width * 0.2);
                Canvas.SetTop(ln, l * size * 0.18);
                page.Children.Add(ln);
            }
            page.CacheMode = new BitmapCache();

            var rotate = new RotateTransform(rnd.NextDouble() * 360, page.Width / 2, page.Height / 2);
            var move = new TranslateTransform();
            page.RenderTransform = new TransformGroup { Children = { rotate, move } };
            Canvas.SetLeft(page, rnd.NextDouble() * ActualWidth);
            Canvas.SetTop(page, 0);
            PagesLayer.Children.Add(page);

            double seconds = 16 + rnd.NextDouble() * 18;
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-size - 20, ActualHeight + 20, TimeSpan.FromSeconds(seconds))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(-rnd.NextDouble() * seconds),   // листы стартуют на разной высоте
            });

            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-30, 30, TimeSpan.FromSeconds(3 + rnd.NextDouble() * 3))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });

            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, rnd.Next(2) == 0 ? 360 : -360, TimeSpan.FromSeconds(seconds * 1.5))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            });
        }
    }
}
