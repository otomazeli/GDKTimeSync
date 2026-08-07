using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Tests;

public sealed class WindowsCredentialStoreTests
{
    [Fact]
    public async Task Saved_credential_is_reported_as_existing_without_returning_its_value()
    {
        var key = "GDK.TimeSync.Tests." + Guid.NewGuid().ToString("N");
        var store = new WindowsCredentialStore();

        try
        {
            await store.SaveAsync(key, "test-only-secret");

            Assert.True(await store.ExistsAsync(key));
        }
        finally
        {
            await store.DeleteAsync(key);
        }
    }
}
