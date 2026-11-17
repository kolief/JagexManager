using System.Buffers.Text;
using System.Security.Cryptography;

namespace JagexManager;

public static class Secret
{
    public static string Create(int bytes) => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));
}
