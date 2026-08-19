using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SnookerPoint.Licensing;

namespace SnookerPoint.App.Licensing;

/// <summary>
/// Persists the licence/trial state to a DPAPI-protected file under the managed License folder
/// (outside the business database and outside business backups). DPAPI (CurrentUser) both
/// encrypts and authenticates the bytes, so casual editing is rejected as corrupt — but trust in
/// a licence always rests on its signature, not on this encryption.
/// </summary>
public sealed class DpapiLicenseStateStore : ILicenseStateStore
{
    private static readonly byte[] StateEntropy = "SnookerPoint.LicenseState.v1"u8.ToArray();
    private static readonly byte[] LicenseEntropy = "SnookerPoint.License.v1"u8.ToArray();
    private readonly string _statePath;
    private readonly string _licensePath;
    private readonly DataProtectionScope _scope;

    public DpapiLicenseStateStore(string licenseFolder, DataProtectionScope scope = DataProtectionScope.CurrentUser)
    {
        Directory.CreateDirectory(licenseFolder);
        _statePath = Path.Combine(licenseFolder, "state.dat");
        _licensePath = Path.Combine(licenseFolder, "license.dat");
        _scope = scope;
    }

    public LicenseState? Load(out bool corrupt)
    {
        corrupt = false;
        if (!File.Exists(_statePath))
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(_statePath), StateEntropy, _scope));
            var state = JsonSerializer.Deserialize<LicenseState>(json);
            if (state is null)
            {
                corrupt = true;
                return null;
            }

            return state;
        }
        catch (Exception)
        {
            // A file that exists but cannot be read/decrypted is treated as corrupt (not "absent"),
            // so a fresh trial is never silently started over a damaged/tampered state file.
            corrupt = true;
            return null;
        }
    }

    public bool Save(LicenseState state)
    {
        try
        {
            var json = JsonSerializer.Serialize(state);
            return WriteProtected(_statePath, Encoding.UTF8.GetBytes(json), StateEntropy, _scope);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string? LoadLicense()
    {
        if (!File.Exists(_licensePath))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(_licensePath), LicenseEntropy, _scope));
        }
        catch (Exception)
        {
            // An unreadable/tampered licence file is treated as "no licence" (fall back to trial).
            return null;
        }
    }

    public bool SaveLicense(string licenseText)
    {
        try
        {
            return WriteProtected(_licensePath, Encoding.UTF8.GetBytes(licenseText), LicenseEntropy, _scope);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool WriteProtected(string path, byte[] data, byte[] entropy, DataProtectionScope scope)
    {
        var protectedBytes = Protect(data, entropy, scope);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, protectedBytes);
        File.Copy(tmp, path, overwrite: true);
        File.Delete(tmp);
        return true;
    }

    private static byte[] Protect(byte[] data, byte[] entropy, DataProtectionScope scope)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Protect(data, entropy, scope);
        }

        // Non-Windows fallback (not used by the shipped app): store as-is.
        return data;
    }

    private static byte[] Unprotect(byte[] data, byte[] entropy, DataProtectionScope scope)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Unprotect(data, entropy, scope);
        }

        return data;
    }
}
