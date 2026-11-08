namespace SharpSlang.Native;

[NativeTypeName("uint32_t")]
public enum CoverageCounterMode : uint
{
    /// <summary>The counter holds the number of times the entry executed</summary>
    /// <remarks>
    /// <para>(atomic add per execution).</para>
    /// </remarks>
    Count = 0,
    /// <summary>The counter is a boolean flag: <c>0</c> if the entry never executed,</summary>
    /// <remarks>
    /// <para>non-zero if it executed at least once. Written with a plain</para>
    /// <para>(non-atomic) store of <c>1</c>, so it carries no execution count but</para>
    /// <para>avoids all atomic contention. Selected by</para>
    /// <para><c>-trace-coverage-boolean</c>.</para>
    /// </remarks>
    Boolean = 1,
}
