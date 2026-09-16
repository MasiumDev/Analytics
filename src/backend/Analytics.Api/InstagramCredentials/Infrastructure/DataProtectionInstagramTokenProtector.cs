using System.Security.Cryptography;
using Analytics.Api.InstagramCredentials.Application;
using Microsoft.AspNetCore.DataProtection;

namespace Analytics.Api.InstagramCredentials.Infrastructure;

public sealed class DataProtectionInstagramTokenProtector(IDataProtectionProvider provider)
    : IInstagramTokenProtector
{
    public const string Purpose = "Instagram.AccessToken.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("An access token is required.", nameof(accessToken));
        }

        return _protector.Protect(accessToken);
    }

    public bool TryUnprotect(string encryptedAccessToken, out string? accessToken)
    {
        accessToken = null;
        if (string.IsNullOrWhiteSpace(encryptedAccessToken))
        {
            return false;
        }

        try
        {
            accessToken = _protector.Unprotect(encryptedAccessToken);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
