using System.Windows;
using System.Windows.Media;

namespace JagexManager;

public partial class Prompt : Window
{
    private Prompt(Window owner, string title, string question, string note, string answer, int limit)
    {
        InitializeComponent();

        Owner = owner;
        Title = title;
        FontFamily = owner.FontFamily;
        FontSize = owner.FontSize;
        TextOptions.SetTextFormattingMode(this, TextOptions.GetTextFormattingMode(owner));
        TextOptions.SetTextRenderingMode(this, TextOptions.GetTextRenderingMode(owner));

        Question.Text = question;
        Note.Text = note;
        Answer.Text = answer;
        Answer.MaxLength = limit;
        Answer.SelectAll();
    }

    public static string? Ask(Window owner, string title, string question, string note, string answer, int limit)
    {
        var prompt = new Prompt(owner, title, question, note, answer, limit);
        return prompt.ShowDialog() == true ? prompt.Answer.Text : null;
    }

    private void Save(object sender, RoutedEventArgs e) => DialogResult = true;
}
