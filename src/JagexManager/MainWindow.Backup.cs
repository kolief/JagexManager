using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace JagexManager;

public partial class MainWindow
{
    private const string BackupFilter = "JagexManager backup (*.json)|*.json";
    private const int ChangesShown = 10;

    private void Export(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export settings",
            Filter = BackupFilter,
            FileName = "JagexManager backup.json",
        };

        if (dialog.ShowDialog(this) == true
            && Try(() => File.WriteAllText(dialog.FileName, Backup.Write(settings, roster.Accounts.Select(a => a.Account)))))
            Say($"Exported your settings to {Path.GetFileName(dialog.FileName)}. Sign-ins are not in it.");
    }

    private void Import(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import settings",
            Filter = BackupFilter,
            CheckFileExists = true,
        };

        Backup? loaded = null;
        if (dialog.ShowDialog(this) != true || !Try(() => loaded = Backup.Load(dialog.FileName)) || loaded is not { } backup)
            return;

        var question = "Replace your settings and character choices with the ones in this file?"
            + "\n\nOnly import a file you made yourself: it can change which programs Play starts and the options they start with.";
        var changes = Changes(backup);
        if (changes.Count > 0)
        {
            var shown = changes.Take(ChangesShown).Append(changes.Count > ChangesShown ? $"and {changes.Count - ChangesShown} more" : null);
            question += $"\n\nIt sets these clients and launch options:\n{string.Join('\n', shown.OfType<string>())}";
        }

        if (!Confirm(question, "Import settings"))
            return;

        Change(backup.Settings);
        var font = backup.Settings.UseGameFont && !settings.UseGameFont ? $" {FontMissing}" : "";
        var applied = 0;
        if (!Try(() =>
        {
            applied = roster.Import(backup.Characters);
            roster.Prune(settings.Names);
        }))
            return;

        var missing = backup.Characters.Count - applied;
        Say(missing switch
        {
            0 => "Imported your settings.",
            1 => "Imported your settings. 1 character in the file is not here yet, so sign in to its account and import again.",
            _ => $"Imported your settings. {missing} characters in the file are not here yet, so sign in to their accounts and import again.",
        } + font);
    }

    private List<string> Changes(Backup backup)
    {
        var changes = new List<string>();
        if (backup.Settings.Default != settings.Default)
            changes.Add($"Play starts {backup.Settings.Default} unless a character has its own client");

        var characters = new List<(CharacterView View, Preference Preference)>();
        foreach (var (id, preference) in backup.Characters)
        {
            if (roster.Find(id) is { } view && view.Character.Id == id)
                characters.Add((view, preference));
        }

        var offered = backup.Settings.Names.ToHashSet();
        changes.AddRange(characters
            .Where(c => c.Preference.Choice is { } choice && offered.Contains(choice) && choice != c.View.Choice)
            .Select(c => $"{c.View.Name} plays in {c.Preference.Choice}"));

        var kept = settings.Custom.Select(p => p.Name).ToHashSet();
        changes.AddRange(backup.Settings.Custom
            .Where(p => !settings.Custom.Contains(p))
            .Select(p => kept.Contains(p.Name) ? $"Client {p.Name} changes to {p.Path}" : $"New client {p.Name}: {p.Path}"));

        changes.AddRange(backup.Settings.Options
            .Where(o => settings.Options.GetValueOrDefault(o.Key) != o.Value)
            .Select(o => $"{o.Key}: {o.Value}"));

        changes.AddRange(characters
            .Where(c => c.Preference.Options is { } options && options != c.View.Options)
            .Select(c => $"{c.View.Name}: {c.Preference.Options}"));

        return changes;
    }
}
