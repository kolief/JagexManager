using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace JagexManager;

public sealed record Pkce(string Verifier, string Challenge)
{
    public static Pkce Create() => From(Secret.Create(32));

    public static Pkce From(string verifier) =>
        new(verifier, Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));

    public override string ToString() => nameof(Pkce);
}
