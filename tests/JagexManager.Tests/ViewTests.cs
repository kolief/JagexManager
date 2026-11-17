namespace JagexManager.Tests;

public class ViewTests
{
    private static readonly Character Main = new("c1", "Main");
    private static readonly Character Alt = new("c2", "Alt");
    private static readonly Account Player = new("user-1", "Player", "session-1", [Main, Alt]);

    [Fact]
    public void UpdateKeepsRowsOfCharactersThatRemain()
    {
        var view = new AccountView(Player);
        var main = view.Characters[0];

        view.Update(Player with { Characters = [new("c3", "New"), Alt, Main with { Name = "Renamed" }] });

        Assert.Equal(["New", "Alt", "Renamed"], view.Characters.Select(c => c.Name));
        Assert.Same(main, view.Characters[2]);
    }

    [Fact]
    public void UpdateDropsRowsOfCharactersThatAreGone()
    {
        var view = new AccountView(Player);

        view.Update(Player with { Characters = [Alt] });

        Assert.Equal("Alt", Assert.Single(view.Characters).Name);
    }

    [Fact]
    public void SummaryCountsTheCharacters()
    {
        var view = new AccountView(Player with { Characters = [] });
        Assert.Equal("No characters yet", view.Summary);

        view.Update(Player with { Characters = [Main] });
        Assert.Equal("1 character", view.Summary);

        view.Update(Player);
        Assert.Equal("2 characters", view.Summary);
    }

    [Fact]
    public void UnnamedCharacterGetsAPlaceholder()
    {
        var view = new AccountView(Player with { Characters = [new Character("c9", "")] });

        Assert.Equal("Unnamed character", view.Characters[0].Name);
    }

    [Fact]
    public void BoundPropertiesAnnounceTheirChanges()
    {
        var view = new AccountView(Player);
        var changed = new List<string?>();
        view.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        view.Characters[0].PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        view.Expired = true;
        view.Characters[0].Ready = false;
        view.Update(Player with { Name = "Renamed", Characters = [Main with { Name = "Other" }] });

        Assert.Subset(changed.ToHashSet(), new HashSet<string?> { "Expired", "Ready", "Name", "Summary" });
    }
}
