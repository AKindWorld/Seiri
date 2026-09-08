namespace Seiri.Core.Contracts;

public interface ILibraryRegistry
{
    IReadOnlyList<string> GetRoots();
    void Save(IEnumerable<string> roots);
}
