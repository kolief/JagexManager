using System.Collections.ObjectModel;

namespace JagexManager;

public sealed class PlayException(string message) : Exception(message);

public enum Handover
{
    Ready,
    SignIn,
    Stranger,
}

public sealed class Roster(Vault vault, Jagex jagex)
{
    public ObservableCollection<AccountView> Accounts { get; } = [];

    private IEnumerable<CharacterView> Characters => Accounts.SelectMany(a => a.Characters);

    public void Load()
    {
        foreach (var account in vault.Load())
            Accounts.Add(new AccountView(account));
    }

    public void Add(Account account)
    {
        if (Accounts.FirstOrDefault(a => a.Account.Id == account.Id) is { } known)
        {
            known.Update(account with
            {
                Choices = Kept(known.Account.Choices, account.Characters),
                Preferences = Kept(known.Account.Preferences, account.Characters),
            });
            known.Expired = false;
        }
        else
        {
            Accounts.Add(new AccountView(account));
        }

        Save();
    }

    public void Remove(AccountView account)
    {
        foreach (var character in account.Characters)
            character.Forget();

        Accounts.Remove(account);
        Save();
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
                account.Update(account.Account with
                {
                    Characters = characters,
                    Choices = Kept(account.Account.Choices, characters),
                    Preferences = Kept(account.Account.Preferences, characters),
                });
                Save();
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

    public void Choose(CharacterView character, string? name)
    {
        var account = character.Owner;
        if (!account.Characters.Contains(character))
            return;

        var choices = account.Account.Choices.Where(c => c.Key != character.Character.Id).ToDictionary();
        if (name is not null)
            choices[character.Character.Id] = name;

        account.Update(account.Account with { Choices = choices });
        Save();
    }

    public void Rename(CharacterView character, string? nickname) =>
        Prefer(character, p => p with { Nickname = Typed(nickname) });

    public void SetOptions(CharacterView character, string? options) =>
        Prefer(character, p => p with { Options = Typed(options) });

    public void Hide(CharacterView character, bool hidden) =>
        Prefer(character, p => p with { Hidden = hidden });

    public void Prune(IEnumerable<string> names)
    {
        var offered = names.ToHashSet();
        var stale = Accounts.Where(a => a.Account.Choices.Values.Any(name => !offered.Contains(name))).ToList();
        foreach (var account in stale)
        {
            var choices = account.Account.Choices.Where(c => offered.Contains(c.Value)).ToDictionary();
            account.Update(account.Account with { Choices = choices });
        }

        if (stale.Count > 0)
            Save();
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

    public async Task PlayAsync(CharacterView character, Program program)
    {
        if (!character.Running && !program.Installed)
            throw new PlayException($"{program.Name} was not found. Choose another client.");

        var account = character.Owner;
        await RefreshAsync(account);

        if (!Accounts.Contains(account) || !account.Characters.Contains(character))
            throw new PlayException($"{character.Name} is no longer on this account.");

        if (account.Expired)
            throw new PlayException($"Jagex ended the session for {account.Name}. Sign in again to play.");

        if (!character.Running)
            character.Attach(Client.Start(program.Path, program.Arguments, account.Account.SessionId, character.Character));
    }

    public void DropClosed()
    {
        foreach (var character in Characters)
            character.DropClosed();
    }

    public void Forget()
    {
        foreach (var character in Characters)
            character.Forget();
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

    private void Prefer(CharacterView character, Func<Preference, Preference> change)
    {
        var account = character.Owner;
        if (!account.Characters.Contains(character))
            return;

        var id = character.Character.Id;
        var preferences = account.Account.Preferences.Where(p => p.Key != id).ToDictionary();
        var changed = change(account.Account.Preferences.GetValueOrDefault(id) ?? Preference.None);
        if (changed != Preference.None)
            preferences[id] = changed;

        account.Update(account.Account with { Preferences = preferences });
        Save();
    }

    private static string? Typed(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static bool Unreachable(Exception e) =>
        e is JagexException or HttpRequestException or TaskCanceledException;

    private static Dictionary<string, T> Kept<T>(
        IReadOnlyDictionary<string, T> kept, IReadOnlyList<Character> characters) =>
        kept.Where(entry => characters.Any(c => c.Id == entry.Key)).ToDictionary();
}
