// The one way a test here waits for the GPU.
//
// Not NriFence.Wait. NRI's wait gives up after its own NRI_TIMEOUT_FENCE (5 s), reports
// "WaitSemaphores() timed out!" as an error message and returns as if the work were done, so a
// readback after it reads memory the queue has not written yet (measured by SharpNRI, whose
// GpuWork.Wait this follows). lavapipe JIT-compiles shaders while it executes a command buffer,
// and on a cold Mesa shader cache on a loaded runner that passes five seconds. So: poll the fence,
// for two minutes, and record every wait long enough to be worth knowing about, so a run that is
// slowing towards the bound says so before it reaches it.
//
// The NONE backend never comes here: its GetFenceValue returns 0 whatever was signalled
// (nri/Source/NONE/ImplNONE.cpp, NRI v191), so a poll would run to the bound, and its Wait is an
// empty function, which is why AllocationTests' frame on NONE keeps it.

using System.Diagnostics;
using SharpNRI;

namespace SharpSlang.Tests;

internal static class GpuWait
{
    /// <summary>The longest a test waits for its GPU work before failing.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>A wait at least this long is written to the test's output.</summary>
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Returns once <paramref name="fence"/> has reached <paramref name="value"/>; fails the test
    /// after <see cref="Timeout"/>, naming the value the fence did reach.
    /// </summary>
    /// <remarks>
    /// <see cref="Stopwatch.GetTimestamp"/> rather than a <see cref="Stopwatch"/>, so the fast path
    /// allocates nothing.
    /// </remarks>
    public static void For(NriFence fence, ulong value, string what = "GPU work")
    {
        ArgumentNullException.ThrowIfNull(fence);

        var start = Stopwatch.GetTimestamp();
        while (fence.Value < value)
        {
            if (Stopwatch.GetElapsedTime(start) > Timeout)
            {
                Assert.Fail($"{what} did not reach fence value {value} in {Timeout.TotalSeconds:0} s "
                          + $"(the fence is at {fence.Value})");
            }

            Thread.Sleep(1);
        }

        var took = Stopwatch.GetElapsedTime(start);
        if (took >= Slow)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"slow GPU wait: {what} took {took.TotalMilliseconds:0} ms to reach fence value {value}");
        }
    }
}
