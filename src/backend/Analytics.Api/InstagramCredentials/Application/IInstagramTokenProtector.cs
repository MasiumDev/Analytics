namespace Analytics.Api.InstagramCredentials.Application;

public interface IInstagramTokenProtector
{
    string Protect(string accessToken);

    bool TryUnprotect(string encryptedAccessToken, out string? accessToken);
}
