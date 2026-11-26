using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;

namespace JagexManager;

public sealed class PlayException(string message) : Exception(message);

public sealed record Outcome(CharacterView Character, Program Program, string? Error)
{
    public static string Summary(IReadOnlyList<Outcome> outcomes, string place)
    {
        var failed = outcomes.Where(o => o.Error is not null).ToList();
        var started = outcomes.Count - failed.Count;
        if (failed.Count == 0)
        {
            return started == 1
                ? $"Started {outcomes[0].Character.Name} in {outcomes[0].Program.Name}."
                : $"Started {started} characters {place}.";
        }

        var first = failed[0];
        if (started == 0 && failed.All(f => f.Error == first.Error) && first.Error is { } shared)
            return shared;

        return started == 0
            ? $"No characters {place} started. {first.Character.Name}: {first.Error}"
            : $"Started {started} of {outcomes.Count} characters {place}. {first.Character.Name}: {first.Error}";
    }
}

public enum Handover
{
    Ready,
    SignIn,
    Stranger,
}

public sealed class Roster(Vault vault, Jagex jagex) : View
{
    public const int SearchFrom = 6;

    private const int NamesShown = 3;
    private const int RecentShown = 5;

    private readonly HashSet<(string Id, DateTimeOffset Ends)> warned = [];
    private bool refreshing;
    private bool finding;
    private Sort sort;
    private string? search;

    public ObservableCollection<AccountView> Accounts { get; } = [];

    public ObservableCollection<Group> Groups { get; } = [];

    public Sort Sort
    {
        get => sort;
        set
        {
            if (sort == value)
                return;

            sort = value;
            foreach (var account in Accounts)
                account.Sort = value;
        }
    }

    public string? Search
    {
        get => search;
        set
        {
            search = Preference.Clean(value);
            foreach (var account in Accounts)
                account.Search = search;

            Changed();
        }
    }

    public bool Matched => search is null || Accounts.Any(a => a.Matched);

    public bool Searchable => search is not null || Characters.Count() >= SearchFrom;

    public bool GroupsOffered => Groups.Count > 0 && search is null;

    public bool KeepPlacement { get; set; } = true;

    public IReadOnlyList<CharacterView> Running => Characters.Where(c => c.Running).ToList();

    public IReadOnlyList<CharacterView> Recent =>
        Characters.Where(c => c.Played is not null && !c.Hidden).OrderByDescending(c => c.Played).Take(RecentShown).ToList();

    private IEnumerable<CharacterView> Characters => Accounts.SelectMany(a => a.Characters);

    public void Load()
    {
        foreach (var account in vault.Load())
            Accounts.Add(Track(account));

        Regroup();
        Changed();
    }

    public void Add(Account account)
    {
        if (Accounts.FirstOrDefault(a => a.Account.Id == account.Id) is { } known)
        {
            known.Update(account with { Preferences = Kept(known.Account.Preferences, account.Characters) });
            known.Expired = false;
        }
        else
        {
            Accounts.Add(Track(account));
        }

        Commit();
    }

    public void Remove(AccountView account)
    {
        foreach (var character in account.Characters)
            character.Forget();

        Accounts.Remove(account);
        Commit();
    }

    public bool Move(AccountView account, int position)
    {
        var current = Accounts.IndexOf(account);
        if (current < 0)
            return false;

        position = Math.Clamp(position, 0, Accounts.Count - 1);
        if (position == current)
            return false;

        Accounts.Move(current, position);
        Regroup();
        return true;
    }

    public async Task<bool> RefreshAsync(AccountView account)
    {
        var session = account.Account.SessionId;

        bool Current() => Accounts.Contains(account) && account.Account.SessionId == session;

        try
        {
            var characters = await jagex.ListCharactersAsync(session);
            if (!Current())
                return true;

            account.Expired = false;
            if (!characters.SequenceEqual(account.Account.Characters))
            {
                account.Update(account.Account with { Characters = characters });
                Commit();
            }

            return true;
        }
        catch (JagexException e) when (e.Rejected)
        {
            if (!Current())
                return true;

            account.Expired = true;
            return true;
        }
        catch (Exception e) when (Unreachable(e))
        {
            return false;
        }
    }

    public async Task RefreshAllAsync()
    {
        if (refreshing)
            return;

        refreshing = true;
        try
        {
            await Task.WhenAll(Accounts.ToList().Select(RefreshAsync));
        }
        finally
        {
            refreshing = false;
            foreach (var account in Accounts)
                account.Tick();
        }
    }

    public void Choose(CharacterView character, string? name) =>
        Prefer(character, p => p.Choice == name ? p : p with { Choice = name, Options = null });

    public void Rename(CharacterView character, string? nickname) =>
        Prefer(character, p => p with { Nickname = Preference.Clean(nickname) == character.Character.Name ? null : nickname });

    public void SetOptions(CharacterView character, string? options, string client) =>
        Prefer(character, p => Preference.Clean(options) is null ? p with { Options = null } : p with { Choice = client, Options = options });

    public void SetHidden(CharacterView character, bool hidden) =>
        Prefer(character, p => p with { Hidden = hidden });

    public void SetNote(CharacterView character, string? note) =>
        Prefer(character, p => p with { Note = note });

    public void Join(CharacterView character, string group) => Join([character], group);

    public void Join(IEnumerable<CharacterView> characters, string group)
    {
        var name = Existing(group);
        Prefer(characters, p => p with { Groups = [.. p.Groups, name] });
    }

    public void Leave(CharacterView character, string group) =>
        Prefer(character, p => p with { Groups = Without(p.Groups, group) });

    public string RenameGroup(string group, string name)
    {
        var renamed = Group.NameComparer.Equals(group, name) ? name : Existing(name);
        Prefer(Members(group), p => p with { Groups = [.. Without(p.Groups, group), renamed] });
        return renamed;
    }

    public IReadOnlyList<CharacterView> DeleteGroup(string group)
    {
        var members = Members(group);
        Prefer(members, p => p with { Groups = Without(p.Groups, group) });
        return members;
    }

    public int Import(IReadOnlyDictionary<string, Preference> chosen)
    {
        var matched = Characters.Where(c => chosen.ContainsKey(c.Character.Id)).ToList();
        Prefer(matched, (c, p) => p.Choose(chosen[c.Character.Id]));
        return matched.Count;
    }

    public CharacterView? Find(string target) =>
        Characters.FirstOrDefault(c => c.Character.Id == target)
        ?? Characters.FirstOrDefault(c =>
            string.Equals(c.Name, target, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.RealName, target, StringComparison.OrdinalIgnoreCase));

    public Group? FindGroup(string name) => Groups.FirstOrDefault(g => Group.NameComparer.Equals(g.Name, name));

    public void Prune(IEnumerable<string> names)
    {
        var offered = names.ToHashSet();
        bool Stale(Preference p) => p.Choice is { } choice && !offered.Contains(choice);

        var stale = Accounts.Where(a => a.Account.Preferences.Values.Any(Stale)).ToList();
        foreach (var account in stale)
        {
            var preferences = account.Account.Preferences
                .Select(p => (p.Key, Value: Stale(p.Value) ? p.Value with { Choice = null, Options = null } : p.Value))
                .Where(p => p.Value != Preference.None)
                .ToDictionary(p => p.Key, p => p.Value);
            account.Update(account.Account with { Preferences = preferences });
        }

        if (stale.Count > 0)
            Commit();
    }

    public static string Unready(AccountView account) =>
        $"Sign in to {account.Name} again to open it in the Jagex Launcher.";

    public Handover Check(AccountView account, Credentials? signedIn)
    {
        var owner = signedIn is null ? null : Owner(signedIn);
        if (account.Account.Credentials is null && owner != account)
            return Handover.SignIn;

        return signedIn is not null && owner is null ? Handover.Stranger : Handover.Ready;
    }

    public void HandOver(AccountView account, Launcher launcher, bool replace)
    {
        if (!Accounts.Contains(account))
            throw new PlayException($"{account.Name} is no longer in JagexManager.");

        var held = launcher.Read();
        var owner = held is null ? null : Owner(held);
        if (held is not null && owner is null && !replace)
            throw new PlayException("The Jagex Launcher was signed in to another account in the meantime. Try again.");

        var adopted = owner is not null && Newer(owner, held!) ? held : null;
        var returned = owner == account && adopted is not null;
        if ((returned ? adopted : account.Account.Credentials) is not { } credentials)
            throw new PlayException(Unready(account));

        account.Update(account.Account with { Credentials = null });
        try
        {
            Save();
            launcher.Write(credentials, account.Account.SessionId);
        }
        catch
        {
            if (!returned)
            {
                account.Update(account.Account with { Credentials = credentials });
                Save();
            }

            throw;
        }

        if (adopted is not null && !returned)
            Adopt(owner!, adopted);
    }

    public async Task<bool> PlayAsync(CharacterView character, Program program)
    {
        if (!character.Running && !program.Installed)
            throw new PlayException($"{program.Name} was not found. Choose another client.");

        await RefreshAsync(character.Owner);

        Confirm(character);
        if (!Start(character, program))
            return false;

        Stamp(character);
        return true;
    }

    public async Task<IReadOnlyList<Outcome>> PlayAllAsync(IReadOnlyList<CharacterView> waiting, Func<CharacterView, Program> pick, TimeSpan gap)
    {
        foreach (var character in waiting)
            character.Ready = false;

        try
        {
            await Task.WhenAll(waiting.Select(c => c.Owner).Distinct().Select(RefreshAsync));

            var outcomes = new List<Outcome>();
            var launched = false;
            foreach (var character in waiting)
            {
                Program? program = null;
                try
                {
                    Confirm(character);
                    if (launched && gap > TimeSpan.Zero && !character.Running)
                    {
                        await Task.Delay(gap);
                        Confirm(character);
                    }

                    program = pick(character);
                    if (!Start(character, program))
                        continue;

                    launched = true;
                    Stamp(character);
                    outcomes.Add(new Outcome(character, program, null));
                }
                catch (Exception e) when (Failed(e))
                {
                    outcomes.Add(new Outcome(character, program ?? pick(character), e.Message));
                }
            }

            return outcomes;
        }
        finally
        {
            foreach (var character in waiting)
                character.Ready = true;
        }
    }

    public void Arrange()
    {
        foreach (var account in Accounts)
            account.Arrange();
    }

    public IReadOnlyList<CharacterView> Watch()
    {
        var closed = Characters.Where(c => c.Closed).ToList();
        foreach (var character in Characters.Where(c => c.Running).Except(closed))
            character.Watch(KeepPlacement);

        if (closed.Count == 0)
            return [];

        Record(closed);
        var crashed = closed.Where(c => c.Crashed).ToList();
        foreach (var character in closed)
            character.Forget();

        Changed();
        return crashed;
    }

    public void Record() => Record(Running);

    public async Task FindLevelsAsync(Hiscores hiscores)
    {
        if (finding)
            return;

        finding = true;
        try
        {
            var named = Characters.Where(c => c.Character.Name.Length > 0).GroupBy(c => c.Character.Name, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var characters in named)
            {
                var levels = await hiscores.FindAsync(characters.Key);
                foreach (var character in characters)
                    character.Levels = levels;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
        }
        finally
        {
            finding = false;
        }
    }

    public void ForgetLevels()
    {
        foreach (var character in Characters)
            character.Levels = null;
    }

    public void Forget()
    {
        foreach (var character in Characters)
            character.Forget();
    }

    public string? TakeWarning()
    {
        var fresh = new List<CharacterView>();
        foreach (var character in Accounts.Where(a => !a.Expired).SelectMany(a => a.Characters))
        {
            if (character is { Lapsing: true, Hidden: false, Character.Membership.Ends: { } ends } && warned.Add((character.Character.Id, ends)))
                fresh.Add(character);
        }

        return fresh switch
        {
            [] => null,
            [var one] => $"Membership for {one.Name} ends in {one.DaysLeft} and will not renew.",
            _ => $"Memberships for {Names(fresh)} end within {CharacterView.LapseWarning.Days} days and will not renew.",
        };
    }

    public void Save() => vault.Save(Accounts.Select(a => a.Account));

    public void SignOut(Launcher launcher)
    {
        var held = launcher.Read();
        launcher.Clear();

        if (held is not null && Owner(held) is { } owner && Newer(owner, held))
            Adopt(owner, held);
    }

    private AccountView? Owner(Credentials credentials)
    {
        var subject = credentials.Subject;
        return Accounts.FirstOrDefault(a => a.Account.Id == subject);
    }

    private static bool Newer(AccountView owner, Credentials held) =>
        owner.Account.Credentials is not { } kept || kept.Issued <= held.Issued;

    private void Adopt(AccountView owner, Credentials held)
    {
        owner.Update(owner.Account with { Credentials = held });
        Save();
    }

    private static void Usable(AccountView account)
    {
        if (account.Expired)
            throw new PlayException($"Jagex ended the session for {account.Name}. Sign in again to play.");
    }

    private static bool Start(CharacterView character, Program program)
    {
        if (character.Running)
            return false;

        if (!program.Installed)
            throw new PlayException($"{program.Name} was not found. Choose another client.");

        character.Attach(Client.Start(program.Path, program.Arguments, character.Owner.Account.SessionId, character.Character));
        return true;
    }

    private void Confirm(CharacterView character)
    {
        var account = character.Owner;
        if (!Accounts.Contains(account))
            throw new PlayException($"{account.Name} is no longer in JagexManager.");

        if (!account.Characters.Contains(character))
            throw new PlayException($"{character.Name} is no longer on this account.");

        Usable(account);
    }

    private void Stamp(CharacterView character)
    {
        var now = DateTimeOffset.UtcNow;
        Quietly(() => Prefer(character, p => p with { Played = now }));
    }

    private void Record(IReadOnlyList<CharacterView> characters)
    {
        if (characters.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        Quietly(() => Prefer(characters, (c, p) => p with
        {
            Playtime = PlayDay.Add(p.Playtime, c.Since, now),
            Placement = KeepPlacement && c.Seen is { } seen ? seen : p.Placement,
        }));

        foreach (var character in characters)
            character.Clock(now);
    }

    private static void Quietly(Action save)
    {
        try
        {
            save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
        {
        }
    }

    private static bool Failed(Exception e) =>
        e is PlayException or IOException or UnauthorizedAccessException or Win32Exception;

    private void Commit()
    {
        Regroup();
        Changed();
        Save();
    }

    private void Changed() => Raise(nameof(Matched), nameof(Searchable), nameof(GroupsOffered));

    private static string Names(List<CharacterView> characters)
    {
        if (characters.Count > NamesShown + 1)
            return $"{string.Join(", ", characters.Take(NamesShown).Select(c => c.Name))} and {characters.Count - NamesShown} others";

        return $"{string.Join(", ", characters.SkipLast(1).Select(c => c.Name))} and {characters[^1].Name}";
    }

    private void Prefer(CharacterView character, Func<Preference, Preference> change) => Prefer([character], change);

    private void Prefer(IEnumerable<CharacterView> characters, Func<Preference, Preference> change) =>
        Prefer(characters, (_, preference) => change(preference));

    private void Prefer(IEnumerable<CharacterView> characters, Func<CharacterView, Preference, Preference> change)
    {
        var changed = false;
        foreach (var members in characters.GroupBy(c => c.Owner))
        {
            var account = members.Key;
            var kept = members.Where(account.Characters.Contains).ToList();
            if (!Accounts.Contains(account) || kept.Count == 0)
                continue;

            var preferences = account.Account.Preferences.ToDictionary();
            foreach (var character in kept)
            {
                var id = character.Character.Id;
                var preference = change(character, preferences.GetValueOrDefault(id, Preference.None)).Tidy();
                if (preference == Preference.None)
                    preferences.Remove(id);
                else
                    preferences[id] = preference;
            }

            account.Update(account.Account with { Preferences = preferences });
            changed = true;
        }

        if (changed)
            Commit();
    }

    private List<CharacterView> Members(string group) => Characters.Where(c => c.InGroup(group)).ToList();

    private string Existing(string group)
    {
        var name = Preference.Clean(group) ?? group;
        return Groups.FirstOrDefault(g => Group.NameComparer.Equals(g.Name, name))?.Name ?? name;
    }

    private static List<string> Without(IEnumerable<string> groups, string group) =>
        groups.Where(g => !Group.NameComparer.Equals(g, group)).ToList();

    private void Regroup()
    {
        var latest = Accounts
            .SelectMany(a => a.InJagexOrder)
            .SelectMany(c => c.Groups.Select(name => (Name: name, Character: c)))
            .GroupBy(m => m.Name, Group.NameComparer)
            .Select(g => new Group(g.First().Name, g.Select(m => m.Character).ToList()))
            .OrderBy(g => g.Name, Group.NameComparer)
            .ToList();

        for (var position = 0; position < latest.Count; position++)
        {
            if (position == Groups.Count)
                Groups.Add(latest[position]);
            else if (!Groups[position].Same(latest[position]))
                Groups[position] = latest[position];
        }

        while (Groups.Count > latest.Count)
            Groups.RemoveAt(Groups.Count - 1);
    }

    private AccountView Track(Account account) => new(account) { Sort = sort, Search = search };

    private static bool Unreachable(Exception e) =>
        e is JagexException or HttpRequestException or TaskCanceledException;

    private static Dictionary<string, Preference> Kept(
        IReadOnlyDictionary<string, Preference> preferences, IReadOnlyList<Character> characters) =>
        preferences.Where(entry => characters.Any(c => c.Id == entry.Key)).ToDictionary();
}
