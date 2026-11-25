using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace JagexManager;

public static class Display
{
    public static void Match(FrameworkElement target, Window source)
    {
        TextElement.SetFontFamily(target, source.FontFamily);
        TextElement.SetFontSize(target, source.FontSize);
        TextOptions.SetTextFormattingMode(target, TextOptions.GetTextFormattingMode(source));
        TextOptions.SetTextRenderingMode(target, TextOptions.GetTextRenderingMode(source));
    }

    public static MenuItem Item(string header, Action click)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }
}
