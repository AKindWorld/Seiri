using Seiri.Core.Models;

namespace Seiri.Core.Contracts;

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
