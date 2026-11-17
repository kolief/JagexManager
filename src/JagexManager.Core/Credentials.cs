using System.Text.Json.Serialization;

namespace JagexManager;

public sealed record Credentials(string AccessToken, string RefreshToken, string IdToken)
{
    [JsonIgnore]
    public string? Subject => Jwt.Claim(IdToken, "sub");

    [JsonIgnore]
    public long Issued => Jwt.Time(IdToken, "iat") ?? 0;

    [JsonIgnore]
    public bool Complete
    {
        get
        {
            try
            {
                return IdToken is { Length: > 0 }
                    && AccessToken is { Length: > 0 }
                    && RefreshToken is { Length: > 0 }
                    && Subject is { Length: > 0 }
                    && Issued > 0;
            }
            catch (AuthException)
            {
                return false;
            }
        }
    }

    public override string ToString() => nameof(Credentials);
}
