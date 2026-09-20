namespace Seiri.Core;

public static class ExecutionProviders
{
    public static bool WantsDirectMl(string? provider) =>
        !string.Equals(provider, "CPU", StringComparison.OrdinalIgnoreCase);

    public static string Describe(bool directMl) => directMl ? "DirectML" : "CPU";
}
