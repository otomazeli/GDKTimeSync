using System.Runtime.InteropServices;
using System.Text;

namespace GDK.TimeSync.Desktop.Services;

public sealed class WindowsCredentialStore
{
    public const string TogglTokenTarget = "GDK.TimeSync/TogglApiToken";
    public const string JiraPatTarget = "GDK.TimeSync/JiraPersonalAccessToken";

    private const uint GenericCredentialType = 1;
    private const uint LocalMachinePersistence = 2;

    public bool HasSecret(string target) => ReadSecret(target) is not null;

    public string? ReadSecret(string target)
    {
        if (!CredRead(target, GenericCredentialType, 0, out var credentialPointer))
            return null;

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPointer);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
                return string.Empty;

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public void SaveSecret(string target, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = GenericCredentialType,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = LocalMachinePersistence,
                UserName = "GDK TimeSync"
            };

            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException("Windows Credential Manager could not save the credential.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(blob);
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

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
