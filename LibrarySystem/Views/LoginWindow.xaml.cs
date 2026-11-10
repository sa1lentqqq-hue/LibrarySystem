using System.Windows;
using System.Windows.Media.Animation;
using LibrarySystem.Data;
using LibrarySystem.Services;

namespace LibrarySystem.Views;

public partial class LoginWindow : Window
{
    private int _attempts;

    public LoginWindow()
    {
        InitializeComponent();
        WindowTheme.Apply(this);       // тёмная полоса заголовка
        WindowMemory.Attach(this);     // то же состояние окна, что у предыдущего
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)));
        var slide = new DoubleAnimation(-520, 0, TimeSpan.FromMilliseconds(750))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        CardShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slide);
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)));
        LoginBox.Focus();
    }

    private void OnShowPass(object sender, RoutedEventArgs e)
    {
        bool show = ShowPass.IsChecked == true;
        if (show) PassText.Text = PassBox.Password; else PassBox.Password = PassText.Text;
        PassText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PassBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnLogin(object sender, RoutedEventArgs e)
    {
        string login = LoginBox.Text.Trim();
        string password = ShowPass.IsChecked == true ? PassText.Text : PassBox.Password;
        if (login.Length == 0 || password.Length == 0)
        {
            ErrorText.Text = "Введите логин и пароль.";
            return;
        }

        var users = Database.Query("SELECT id, salt, pass_hash, role, full_name FROM users WHERE login = @l", ("@l", login));
        bool ok = users.Rows.Count == 1 &&
                  Passwords.Verify(password, (string)users.Rows[0]["salt"], (string)users.Rows[0]["pass_hash"]);   // сравнение по хэшу

        if (!ok)
        {
            _attempts++;
            ErrorText.Text = $"Неверный логин или пароль (попытка {_attempts} из 5).";
            PassBox.Clear();
            PassText.Clear();
            if (_attempts >= 5)
            {
                Ui.Error("Слишком много неудачных попыток входа. Программа будет закрыта.");
                DialogResult = false;
            }
            return;
        }

        var row = users.Rows[0];
        Session.UserId = Convert.ToInt64(row["id"]);
        Session.Login = login;
        Session.FullName = (string)row["full_name"];
        Session.Role = (string)row["role"];
        Audit.Log("Вход в систему");
        DialogResult = true;
    }
}
