using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JagexManager;

public sealed class Launcher(string folder, byte[] key)
{
    private const string KeyName = "Jagex Safe Storage";
    private const int GenericType = 1;
    private const int NotFound = 1168;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly string[] Keys = ["credentialsList", "gameSessionId"];
    private static readonly string[] EntryKeys = ["accessToken", "refreshToken", "idToken", "accessTokenExpiry"];

    private static readonly string Home = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jagex Launcher");

    public static string Location { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Jagex Launcher", "JagexLauncher.exe");

    public static bool Installed => File.Exists(Location);

    private string CredsPath => Path.Combine(folder, "auth", "creds");

    private string CachePath => Path.Combine(folder, "cache.bin");

    public static Launcher? Find() => ReadKey() is { } key ? new Launcher(Home, key) : null;

    public static bool Running
    {
        get
        {
            var running = InSession(_ => true);
            running.ForEach(p => p.Dispose());
            return running.Count > 0;
        }
    }

    public static async Task CloseAsync()
    {
        var processes = Processes();
        try
        {
            var asked = false;
            foreach (var process in processes)
                asked |= Ask(process);

            if (asked)
                await Task.WhenAll(processes.Select(p => ExitAsync(p, TimeSpan.FromSeconds(5))));

            processes.ForEach(Kill);
            await Task.WhenAll(processes.Select(p => ExitAsync(p, TimeSpan.FromSeconds(10))));
        }
        finally
        {
            processes.ForEach(p => p.Dispose());
        }

        if (Running)
            throw new IOException("The Jagex Launcher could not be closed. Close it yourself, then try again.");
    }

    public static void Start()
    {
        var start = new ProcessStartInfo(Location)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Location),
        };

        using var process = Process.Start(start) ?? throw new IOException("The Jagex Launcher did not start.");
    }

    public Credentials? Read()
    {
        if (!File.Exists(CredsPath))
            return null;

        var root = Parse(Decrypt(File.ReadAllText(CredsPath)));
        if (root["credentialsList"] is not JsonArray list || list.Count > 1)
            throw Unfamiliar();

        if (list.Count == 0)
            return null;

        if (list[0] is not JsonObject entry || entry.Any(f => !EntryKeys.Contains(f.Key)))
            throw Unfamiliar();

        var credentials = new Credentials(Field(entry, "accessToken"), Field(entry, "refreshToken"), Field(entry, "idToken"));
        return credentials.Complete ? credentials : throw Unfamiliar();
    }

    public void Write(Credentials credentials, string sessionId)
    {
        var root = new JsonObject
        {
            ["credentialsList"] = new JsonArray(new JsonObject
            {
                ["accessToken"] = credentials.AccessToken,
                ["refreshToken"] = credentials.RefreshToken,
                ["idToken"] = credentials.IdToken,
                ["accessTokenExpiry"] = 0,
            }),
            ["gameSessionId"] = sessionId,
        };

        if (File.Exists(CachePath))
            File.Delete(CachePath);

        Disk.Replace(CredsPath, Encoding.UTF8.GetBytes(Encrypt(Encoding.UTF8.GetBytes(root.ToJsonString()))));
    }

    public void Clear()
    {
        foreach (var path in new[] { CachePath, CredsPath }.Where(File.Exists))
            File.Delete(path);
    }

    private static List<Process> Processes() => InSession(FromInstall);

    private static bool Ask(Process process)
    {
        try
        {
            return process.CloseMainWindow();
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
        }
    }

    private static async Task ExitAsync(Process process, TimeSpan patience)
    {
        using var timeout = new CancellationTokenSource(patience);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (Exception e) when (e is InvalidOperationException or OperationCanceledException)
        {
        }
    }

    private static List<Process> InSession(Func<Process, bool> wanted)
    {
        using var current = Process.GetCurrentProcess();
        var found = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Location)))
        {
            if (Ours(process, current.SessionId) && wanted(process))
                found.Add(process);
            else
                process.Dispose();
        }

        return found;
    }

    private static bool Ours(Process process, int session)
    {
        try
        {
            return process.SessionId == session;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool FromInstall(Process process)
    {
        try
        {
            return string.Equals(process.MainModule?.FileName, Location, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private string Encrypt(byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var sealedData = new byte[plain.Length + TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, sealedData.AsSpan(0, plain.Length), sealedData.AsSpan(plain.Length));

        return $"{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(sealedData)}";
    }

    private byte[] Decrypt(string text)
    {
        try
        {
            var parts = text.Trim().Split(':');
            if (parts.Length != 2)
                throw Unfamiliar();

            var nonce = Convert.FromBase64String(parts[0]);
            var sealedData = Convert.FromBase64String(parts[1]);
            if (nonce.Length != NonceSize || sealedData.Length < TagSize)
                throw Unfamiliar();

            var plain = new byte[sealedData.Length - TagSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, sealedData.AsSpan(0, plain.Length), sealedData.AsSpan(plain.Length), plain);
            return plain;
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            throw new InvalidDataException("The Jagex Launcher's sign-in could not be read. Open the launcher and sign in there once.");
        }
    }

    private static JsonObject Parse(byte[] plain)
    {
        try
        {
            return JsonNode.Parse(plain) is JsonObject root && root.All(m => Keys.Contains(m.Key))
                ? root
                : throw Unfamiliar();
        }
        catch (JsonException)
        {
            throw Unfamiliar();
        }
    }

    private static string Field(JsonObject entry, string name) =>
        entry[name] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0
            ? text
            : throw Unfamiliar();

    private static InvalidDataException Unfamiliar() =>
        new("This version of the Jagex Launcher stores its sign-in in a way JagexManager does not understand.");

    private static byte[]? ReadKey()
    {
        if (!CredRead(KeyName, GenericType, 0, out var handle))
        {
            var error = Marshal.GetLastWin32Error();
            return error == NotFound ? null : throw new Win32Exception(error);
        }

        try
        {
            var stored = Marshal.PtrToStructure<Stored>(handle);
            var blob = new byte[stored.BlobSize];
            Marshal.Copy(stored.Blob, blob, 0, blob.Length);

            var key = Convert.FromBase64String(Encoding.ASCII.GetString(blob));
            return key.Length == 32 ? key : throw Unfamiliar();
        }
        catch (FormatException)
        {
            throw Unfamiliar();
        }
        finally
        {
            CredFree(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Stored
    {
        public int Flags;
        public int Type;
        public nint TargetName;
        public nint Comment;
        public int WrittenLow;
        public int WrittenHigh;
        public int BlobSize;
        public nint Blob;
        public int Persist;
        public int AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        public nint UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int flags, out nint credential);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(nint credential);
}
