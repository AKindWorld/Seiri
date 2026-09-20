namespace Seiri.Services;

/// <summary>
/// While DirectML is tagging, the gallery must not also upload hardware
/// textures. That GPU clash is a native death with an empty crash.log.
/// Tagging itself still runs on DirectML when Settings ask for GPU.
/// </summary>
public static class GpuWork
{
    private static int _yield;

    public static bool YieldGpuToOnnx => Volatile.Read(ref _yield) > 0;

    public static void BeginUiYield() => Interlocked.Increment(ref _yield);

    public static void EndUiYield()
    {
        if (Interlocked.Decrement(ref _yield) < 0)
        {
            Interlocked.Exchange(ref _yield, 0);
        }
    }
}
