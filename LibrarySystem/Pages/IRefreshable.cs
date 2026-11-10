namespace LibrarySystem.Pages;

/// <summary>Страница, которая умеет заново загрузить данные (вызывается при возврате «Назад», чтобы список был актуальным).</summary>
public interface IRefreshable
{
    void Refresh();
}
