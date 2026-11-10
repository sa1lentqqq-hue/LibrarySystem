using System.Windows;
using LibrarySystem.Data;
using LibrarySystem.Services;
using LibrarySystem.Views;

namespace LibrarySystem;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += (_, args) =>
        {
            Ui.Error("Непредвиденная ошибка: " + args.Exception.Message);
            args.Handled = true;
        };

        // 1. Заставка: логотип раскрывается; пока он играет, открывается база данных
        var splash = new SplashWindow();
        splash.Show();
        try
        {
            await Task.Run(Database.Initialize);      // открыть БД (или создать из schema.sql при первом запуске)
        }
        catch (Exception ex)
        {
            splash.Close();
            Ui.Error("Не удалось открыть базу данных:\n" + ex.Message);
            Shutdown();
            return;
        }
        await splash.Finished;

        // 2. Цикл: вход -> главное окно -> (смена пользователя | выход)
        while (true)
        {
            Session.Clear();
            var login = new LoginWindow();
            if (login.ShowDialog() != true) break;

            var main = new MainWindow();
            main.ShowDialog();
            if (!main.LogoutRequested) break;
        }
        Shutdown();
    }
}
