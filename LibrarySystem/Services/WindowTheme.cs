using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LibrarySystem.Services;

/// <summary>
/// Тёмная (чёрная) системная полоса заголовка окна вместо белой.
/// Windows 10 (1809+) — режим «тёмный заголовок»; Windows 11 — дополнительно задаётся цвет рамки, заголовка и текста.
/// На более старых версиях Windows вызов безвредно игнорируется.
/// </summary>
public static class WindowTheme
{
    private const int UseImmersiveDarkModeOld = 19;   // Windows 10 1809–1903
    private const int UseImmersiveDarkMode = 20;      // Windows 10 1903+ и Windows 11
    private const int BorderColor = 34;               // Windows 11
    private const int CaptionColor = 35;              // Windows 11
    private const int TextColor = 36;                 // Windows 11

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                int on = 1;
                if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, UseImmersiveDarkModeOld, ref on, sizeof(int));

                int black = 0x000000;                       // COLORREF: 0x00BBGGRR
                int paper = 0xBED9E6;                       // светлый «пергаментный» текст заголовка (#E6D9BE)
                DwmSetWindowAttribute(handle, BorderColor, ref black, sizeof(int));
                DwmSetWindowAttribute(handle, CaptionColor, ref black, sizeof(int));
                DwmSetWindowAttribute(handle, TextColor, ref paper, sizeof(int));
            }
            catch
            {
                // оформление заголовка не критично для работы программы
            }
        };
    }
}
