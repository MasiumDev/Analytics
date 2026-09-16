using Analytics.Api.InstagramCredentials.Infrastructure;
using Microsoft.AspNetCore.DataProtection;

namespace Analytics.Api.UnitTests;

public sealed class InstagramTokenProtectorTests
{
    [Fact]
    public void PersistedKeyRing_RoundTripsAcrossProviderInstances()
    {
        var keyRingPath = CreateTemporaryDirectory();
        const string accessToken = "unit-test-access-credential";

        try
        {
            var firstProvider = CreateProvider(keyRingPath, "test-application");
            var firstProtector = new DataProtectionInstagramTokenProtector(firstProvider);
            var encrypted = firstProtector.Protect(accessToken);

            var restartedProvider = CreateProvider(keyRingPath, "test-application");
            var restartedProtector = new DataProtectionInstagramTokenProtector(
                restartedProvider);

            Assert.NotEqual(accessToken, encrypted);
            Assert.True(restartedProtector.TryUnprotect(encrypted, out var decrypted));
            Assert.Equal(accessToken, decrypted);
        }
        finally
        {
            Directory.Delete(keyRingPath, recursive: true);
        }
    }

    [Fact]
    public void InvalidCiphertextOrDifferentKeyRing_FailsWithoutReturningToken()
    {
        var firstPath = CreateTemporaryDirectory();
        var secondPath = CreateTemporaryDirectory();

        try
        {
            var firstProtector = new DataProtectionInstagramTokenProtector(
                CreateProvider(firstPath, "first-application"));
            var unrelatedProtector = new DataProtectionInstagramTokenProtector(
                CreateProvider(secondPath, "second-application"));
            var encrypted = firstProtector.Protect("unit-test-access-credential");

            Assert.False(unrelatedProtector.TryUnprotect(encrypted, out var wrongKeyResult));
            Assert.Null(wrongKeyResult);
            Assert.False(unrelatedProtector.TryUnprotect("not-ciphertext", out var invalidResult));
            Assert.Null(invalidResult);
        }
        finally
        {
            Directory.Delete(firstPath, recursive: true);
            Directory.Delete(secondPath, recursive: true);
        }
    }

    private static IDataProtectionProvider CreateProvider(
        string keyRingPath,
        string applicationName) =>
        DataProtectionProvider.Create(
            new DirectoryInfo(keyRingPath),
            builder => builder.SetApplicationName(applicationName));

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"Analytics_Protection_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
