using System.Security.Cryptography;
using System.Text.Json;

namespace WhosHome.Server.Notifications;

public sealed record VapidKeys(string PublicKey, string PrivateKey);

/// <summary>
/// Web push requires a stable VAPID keypair: rotating it invalidates every existing
/// subscription. Rather than making it a setup step, the keypair is generated on first run and
/// kept next to the database on the mounted volume, the same as the Data Protection keys.
/// </summary>
public static class VapidKeyStore
{
    private sealed record StoredKeys(string PublicKey, string PrivateKey);

    public static VapidKeys LoadOrCreate(string directory, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(logger);

        string path = Path.Combine(directory, "vapid.json");

        if (File.Exists(path))
        {
            StoredKeys? stored = JsonSerializer.Deserialize<StoredKeys>(File.ReadAllText(path));
            if (stored is not null
                && !string.IsNullOrWhiteSpace(stored.PublicKey)
                && !string.IsNullOrWhiteSpace(stored.PrivateKey))
            {
                return new VapidKeys(stored.PublicKey, stored.PrivateKey);
            }

            logger.LogWarning("Ignoring unreadable VAPID key file at {Path} and generating a new pair.", path);
        }

        VapidKeys generated = Generate();
        File.WriteAllText(path, JsonSerializer.Serialize(new StoredKeys(generated.PublicKey, generated.PrivateKey)));
        logger.LogInformation("Generated a new VAPID keypair at {Path}.", path);

        return generated;
    }

    private static VapidKeys Generate()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ECParameters parameters = key.ExportParameters(includePrivateParameters: true);

        // The public key travels to the browser as an uncompressed EC point: 0x04 then X then Y.
        byte[] publicKey = new byte[65];
        publicKey[0] = 0x04;
        parameters.Q.X!.CopyTo(publicKey, 1);
        parameters.Q.Y!.CopyTo(publicKey, 33);

        return new VapidKeys(Base64Url(publicKey), Base64Url(parameters.D!));
    }

    private static string Base64Url(byte[] value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
