using System.Collections.Frozen;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JagexManager;

public sealed class Vault(string path)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("JagexManager.Vault.v1");

    public IReadOnlyList<Account> Load()
    {
        if (!File.Exists(path))
            return [];

        var data = File.ReadAllBytes(path);
        try
        {
            var json = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
            var accounts = JsonSerializer.Deserialize<List<Account>>(json);

            if (accounts is null || !accounts.All(Complete))
                throw Damaged();

            return accounts.Select(a => a with { Choices = Chosen(a.Choices), Credentials = a.Credentials is { Complete: true } ? a.Credentials : null }).ToList();
        }
        catch (Exception e) when (e is CryptographicException or JsonException)
        {
            throw Damaged();
        }
    }

    public void Save(IEnumerable<Account> accounts)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(accounts);
        Disk.Replace(path, ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser));
    }

    public string SetAside()
    {
        var aside = $"{path}.{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.damaged";
        File.Move(path, aside, overwrite: true);
        return aside;
    }

    private static bool Complete(Account? account) =>
        account is { Id: not null, Name: not null, SessionId: not null, Characters: not null }
        && account.Characters.All(c => c is { Id: not null, Name: not null });

    private static Dictionary<string, string> Chosen(IReadOnlyDictionary<string, string>? choices) =>
        (choices ?? FrozenDictionary<string, string>.Empty).Where(c => c.Value is not null).ToDictionary();

    private static InvalidDataException Damaged() => new("The saved accounts could not be read.");
}
