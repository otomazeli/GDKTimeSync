using System.Runtime.InteropServices;
using System.Text;

namespace GDK.TimeSync.Desktop.Services;

public sealed class WindowsCredentialStore : ICredentialStore
{
    public const string TogglTokenTarget = CredentialKeys.TogglApiToken;
    public const string JiraPatTarget = CredentialKeys.JiraPat;

    private const uint GenericCredentialType = 1;
    private const uint LocalMachinePersistence = 2;
    private const int ElementNotFound = 1168;

    public bool HasSecret(string target) => ExistsAsync(target).GetAwaiter().GetResult();

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var exists = CredRead(key, GenericCredentialType, 0, out var credentialPointer);
        if (exists) CredFree(credentialPointer);
        return Task.FromResult(exists);
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadSecret(key));
    }

    public Task SaveAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveSecret(key, secret);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredDelete(key, GenericCredentialType, 0) && Marshal.GetLastWin32Error() != ElementNotFound)
            throw new InvalidOperationException("Windows Credential Manager could not remove the credential.");
        return Task.CompletedTask;
    }

    private static string? ReadSecret(string target)
    {
        if (!CredRead(target, GenericCredentialType, 0, out var credentialPointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPointer);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero) return string.Empty;
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally { CredFree(credentialPointer); }
    }

    private static void SaveSecret(string target, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential { Type = GenericCredentialType, TargetName = target, CredentialBlobSize = (uint)bytes.Length, CredentialBlob = blob, Persist = LocalMachinePersistence, UserName = "GDK TimeSync" };
            if (!CredWrite(ref credential, 0)) throw new InvalidOperationException("Windows Credential Manager could not save the credential.");
        }
        finally { Marshal.FreeCoTaskMem(blob); }
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr credential);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
}