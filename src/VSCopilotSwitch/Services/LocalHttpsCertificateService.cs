using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace VSCopilotSwitch.Services;

public sealed record LocalHttpsCertificateStatus(
    X509Certificate2 Certificate,
    bool Created,
    bool TrustAdded)
{
    public string Thumbprint => Certificate.Thumbprint;

    public string Subject => Certificate.Subject;

    public DateTime NotAfter => Certificate.NotAfter;
}

public static class LocalHttpsCertificateService
{
    private const string CertificateSubject = "CN=VSCopilotSwitch Local HTTPS";
    private const string CertificateFriendlyName = "VSCopilotSwitch Local HTTPS";
    private const string ThumbprintFileName = "https-certificate.thumbprint";
    private static readonly TimeSpan RenewalWindow = TimeSpan.FromDays(30);

    public static LocalHttpsCertificateStatus? EnsureTrustedForServerUrls(IReadOnlyList<string> serverUrls)
    {
        var hosts = ResolveLoopbackHttpsHosts(serverUrls);
        if (hosts.Count == 0)
        {
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("当前本地 HTTPS 证书自动信任流程只支持 Windows CurrentUser 证书库。");
        }

        var certificate = LoadReusableCertificate();
        var created = false;
        if (certificate is null)
        {
            certificate = CreateCertificate();
            InstallServerCertificate(certificate);
            created = true;
        }

        var trustAdded = EnsureTrusted(certificate);
        SaveThumbprint(certificate.Thumbprint);
        return new LocalHttpsCertificateStatus(certificate, created, trustAdded);
    }

    public static IReadOnlyList<string> ResolveLoopbackHttpsHosts(IEnumerable<string> serverUrls)
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var serverUrl in serverUrls)
        {
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
            {
                throw new InvalidOperationException($"监听地址无效：{serverUrl}。");
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 只为本机回环地址自动签发证书，避免把可被局域网访问的 HTTPS 入口伪装成安全默认值。
            if (!uri.IsLoopback)
            {
                throw new InvalidOperationException("VS2026 本地 HTTPS 自动证书只支持 localhost、127.0.0.1 或 ::1 这类回环地址。");
            }

            hosts.Add(uri.Host.Trim('[', ']'));
        }

        return hosts.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static X509Certificate2? LoadReusableCertificate()
    {
        var storedThumbprint = ReadStoredThumbprint();
        if (!string.IsNullOrWhiteSpace(storedThumbprint))
        {
            var certificate = FindCertificate(StoreName.My, storedThumbprint);
            if (IsUsableServerCertificate(certificate))
            {
                return certificate;
            }
        }

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates
            .OfType<X509Certificate2>()
            .Where(IsUsableServerCertificate)
            .OrderByDescending(certificate => certificate.NotAfter)
            .FirstOrDefault();
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            CertificateSubject,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection
            {
                new("1.3.6.1.5.5.7.3.1")
            },
            false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddYears(2);
        using var temporary = request.CreateSelfSigned(notBefore, notAfter);
        var pfx = temporary.Export(X509ContentType.Pfx);
        var certificate = X509CertificateLoader.LoadPkcs12(
            pfx,
            ReadOnlySpan<char>.Empty,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
        SetFriendlyName(certificate);
        return certificate;
    }

    private static void InstallServerCertificate(X509Certificate2 certificate)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);
    }

    private static bool EnsureTrusted(X509Certificate2 certificate)
    {
        if (FindCertificate(StoreName.Root, certificate.Thumbprint) is not null)
        {
            return false;
        }

        using var publicCertificate = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
        SetFriendlyName(publicCertificate);
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(publicCertificate);
        return true;
    }

    private static X509Certificate2? FindCertificate(StoreName storeName, string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            return null;
        }

        using var store = new X509Store(storeName, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates
            .Find(X509FindType.FindByThumbprint, thumbprint.Trim(), validOnly: false)
            .OfType<X509Certificate2>()
            .FirstOrDefault();
    }

    private static bool IsUsableServerCertificate(X509Certificate2? certificate)
    {
        if (certificate is null
            || !string.Equals(certificate.Subject, CertificateSubject, StringComparison.OrdinalIgnoreCase)
            || !certificate.HasPrivateKey
            || certificate.NotAfter <= DateTime.Now.Add(RenewalWindow))
        {
            return false;
        }

        using var privateKey = certificate.GetRSAPrivateKey();
        return privateKey is not null;
    }

    private static string? ReadStoredThumbprint()
    {
        var path = GetThumbprintPath();
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    private static void SaveThumbprint(string thumbprint)
    {
        var path = GetThumbprintPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, thumbprint);
    }

    private static string GetThumbprintPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VSCopilotSwitch",
            ThumbprintFileName);

    private static void SetFriendlyName(X509Certificate2 certificate)
    {
        try
        {
            certificate.FriendlyName = CertificateFriendlyName;
        }
        catch (PlatformNotSupportedException)
        {
            // 友好名称只用于 Windows 证书管理器里识别来源，不影响 HTTPS 握手。
        }
        catch (CryptographicException)
        {
            // 个别受限证书库策略可能拒绝写入友好名称，证书本身仍可用于 Kestrel。
        }
    }
}
