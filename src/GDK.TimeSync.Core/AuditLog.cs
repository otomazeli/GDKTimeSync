namespace GDK.TimeSync.Core;

public enum AuditLevel { Info, Warning, Error }

// Deliberately synchronous and non-throwing: this is called from ~90 sites across UI and
// background threads, and a logger that can fail, block, or need awaiting would be a worse
// problem than the one it solves.
public interface IAuditLog
{
    void Write(AuditLevel level, string category, string message);
}
