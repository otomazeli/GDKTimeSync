namespace GDK.TimeSync.Desktop.Services;

public interface IUserSettingsStore
{
    UserSettings Load();
    void Save(UserSettings settings);
}
