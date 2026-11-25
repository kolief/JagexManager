using System.Windows;

namespace JagexManager;

public partial class Prompt : Window
{
    private Prompt(Window owner, string title, string question, string note, string answer, int limit)
    {
        InitializeComponent();

        Owner = owner;
        Title = title;
        Display.Match(this, owner);

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
