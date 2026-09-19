namespace CESDK.Lua.Tests.Support;

/// <summary>
///     The zero-allocation merge gate: runs a body until it is warm, then asserts that a number of further runs allocate
///     exactly zero bytes on the current thread (<see cref="GC.GetAllocatedBytesForCurrentThread" /> is exact: it
///     accounts for the unused part of the thread's allocation context).
/// </summary>
internal static class AllocationGate
{
    public static void AssertZero(Action body, int iterations = 2_000, int warmUp = 64)
    {
        for (var i = 0; i < warmUp; i++) body();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++) body();

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0,
            $"{allocated} bytes were allocated over {iterations} iterations ({(double)allocated / iterations:F1} per call).");
    }
}
