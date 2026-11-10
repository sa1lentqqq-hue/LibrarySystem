using System.Windows.Controls;

namespace LibrarySystem.Views;

/// <summary>
/// Один экран приложения: страница, заголовок в шапке и название в «хлебных крошках».
/// Экраны складываются в стек навигации главного окна (кнопка «Назад» возвращает к предыдущему).
/// </summary>
public sealed record Screen(UserControl Page, string Title, string? Crumb = null)
{
    public string CrumbText => Crumb ?? Title;
}
