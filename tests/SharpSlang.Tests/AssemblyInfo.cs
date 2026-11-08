// Tests run one at a time.
//
// Not a performance oversight. Two kinds of state in this suite are process-global and cannot be
// scoped to a test:
//
//   * SlangDiagnostics.Policy and .Handler. Slang's diagnostics are per-call, with no session
//     object to hang a policy off, so redirecting them is necessarily a static. Two collections
//     running concurrently would see each other's handlers: a test asserting "exactly one warning"
//     would intermittently see zero or two, depending on what else happened to compile.
//
//   * The Slang compiler itself. Several tests measure emitted byte sizes to prove an option took
//     effect, and Slang caches downstream-compiler state per process.
//
// Serialising costs a couple of seconds on a suite that already spends most of its time in the
// native compiler. A flaky test costs more than that the first time someone has to decide whether
// to believe it.

[assembly: CollectionBehavior(DisableTestParallelization = true)]
