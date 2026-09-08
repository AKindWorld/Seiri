namespace Seiri.Core.Contracts;

public interface IAppHome
{
    string Root { get; }
    string SettingsPath { get; }
    string LibrariesPath { get; }
    string ModelsDirectory { get; }
    string LogsDirectory { get; }
    void EnsureCreated();
}
