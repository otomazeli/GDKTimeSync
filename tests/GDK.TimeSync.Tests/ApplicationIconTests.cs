using Xunit;

namespace GDK.TimeSync.Tests;

public sealed class ApplicationIconTests
{
    [Fact]
    public void Desktop_project_declares_a_real_application_icon()
    {
        var projectPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "GDK.TimeSync.Desktop", "GDK.TimeSync.Desktop.csproj"));
        var project = File.ReadAllText(projectPath);

        Assert.Contains("<ApplicationIcon>Assets\\GDK.TimeSync.ico</ApplicationIcon>", project, StringComparison.Ordinal);

        var iconPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "Assets", "GDK.TimeSync.ico");
        Assert.True(File.Exists(iconPath), $"Expected application icon at {iconPath}");
        Assert.True(new FileInfo(iconPath).Length > 100, "Application icon should contain image data.");
    }
}
