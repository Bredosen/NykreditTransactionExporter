using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NykreditTransactionExporter.EnableBanking;

internal sealed class JwtTokenFactory
{
    #region Fields
    private readonly string _applicationId;
    private readonly string _privateKeyPem;
    #endregion

    #region Create token factory
    public JwtTokenFactory(string applicationId, string privateKeyPath)
    {
        _applicationId = applicationId;
        _privateKeyPem = File.ReadAllText(privateKeyPath);
    }
    #endregion

    #region Create API token
    public string CreateToken()
    {
        long issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        byte[] header = JsonSerializer.SerializeToUtf8Bytes(new
        {
            typ = "JWT",
            alg = "RS256",
            kid = _applicationId
        });
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = "enablebanking.com",
            aud = "api.enablebanking.com",
            iat = issuedAt,
            exp = issuedAt + 300
        });

        string unsignedToken = $"{Base64UrlEncode(header)}.{Base64UrlEncode(payload)}";
        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(_privateKeyPem);
        byte[] signature = rsa.SignData(
            Encoding.ASCII.GetBytes(unsignedToken),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{unsignedToken}.{Base64UrlEncode(signature)}";
    }
    #endregion

    #region Encode Base64 URL value
    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
    #endregion
}
