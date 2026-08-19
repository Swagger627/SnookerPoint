using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using SnookerPoint.Licensing;

namespace SnookerPoint.App.Licensing;

/// <summary>
/// Derives a stable, versioned machine fingerprint from Windows signals that survive normal
/// restarts and business-data restores: the OS MachineGuid (primary), the Windows ProductId, the
/// CPU architecture and processor count. Inputs are normalised, combined and hashed with SHA-256;
/// only the hash and a short installation code leave this class — never a raw hardware identifier.
/// Major hardware or Windows changes may change the fingerprint and require a licence reissue.
/// </summary>
public sealed class WindowsMachineFingerprintProvider : IMachineFingerprintProvider
{
    public MachineFingerprint GetFingerprint()
    {
        var parts = new List<string>
        {
            "v" + ProductInfo.FingerprintVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Normalise(ReadRegistry(@"SOFTWARE\Microsoft\Cryptography", "MachineGuid")),
            Normalise(ReadRegistry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductId")),
            Normalise(RuntimeInformation.OSArchitecture.ToString()),
            Normalise(Environment.ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };

        // A machine with no readable MachineGuid still gets a deterministic (weaker) fingerprint.
        if (string.IsNullOrEmpty(parts[1]))
        {
            parts[1] = Normalise(SafeMachineName());
        }

        var canonical = string.Join("|", parts);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        var hash = Convert.ToHexString(hashBytes);
        var code = InstallationCodec.Encode(hash);
        return new MachineFingerprint(ProductInfo.FingerprintVersion, hash, code);
    }

    private static string Normalise(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string ReadRegistry(string subKey, string name)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(subKey);
            return key?.GetValue(name)?.ToString() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string SafeMachineName()
    {
        try { return Environment.MachineName; }
        catch { return "UNKNOWN"; }
    }
}
