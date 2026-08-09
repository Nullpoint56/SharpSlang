// HAND-WRITTEN. Not generated — and not generatable. See docs/design.md §4.5.
//
// Every Slang entry point that can fail returns a SlangResult (an int32 in HRESULT layout). The
// codes and the success/failure test are *function-like preprocessor macros*:
//
//     #define SLANG_MAKE_ERROR(fac, code) ((((int32_t)(fac)) << 16) | ((int32_t)(code)) | int(0x80000000))
//     #define SLANG_E_NOT_FOUND           SLANG_MAKE_CORE_ERROR(5)
//     #define SLANG_FAILED(status)        ((status) < 0)
//
// ClangSharp cannot evaluate those — it reports "could not be resolved to a supported expression"
// and emits nothing. The generated layer therefore contains zero result codes, which means it
// cannot express "did that call succeed?" at all. This file closes that gap by hand.
//
// The values are transcribed from slang.h lines 1326-1411. PrismSlangResultTests re-derives them
// from the pinned header and fails on drift.

namespace PrismSlang;

/// <summary>
/// <c>SlangResult</c> codes and predicates, transcribed from the preprocessor macros in slang.h.
/// Layout matches COM <c>HRESULT</c>: negative is failure, bit 31 is the severity flag,
/// bits 16-30 are the facility, bits 0-15 are the code.
/// </summary>
public static class SlangResult
{
    // ---- facilities (slang.h:1343-1361) ----
    private const int FacilityWinGeneral = 0;
    private const int FacilityWinInterface = 4;
    private const int FacilityWinApi = 7;
    private const int FacilityBase = 0x200;
    private const int FacilityCore = FacilityBase;

    private static int MakeError(int facility, int code)
        => (facility << 16) | code | unchecked((int)0x80000000);

    // ---- predicates (slang.h:1326-1334) ----

    /// <summary><c>SLANG_FAILED</c> — a result is a failure iff it is negative.</summary>
    public static bool Failed(int status) => status < 0;

    /// <summary><c>SLANG_SUCCEEDED</c> — note that success codes other than zero exist.</summary>
    public static bool Succeeded(int status) => status >= 0;

    /// <summary><c>SLANG_GET_RESULT_FACILITY</c></summary>
    public static int Facility(int r) => (r >> 16) & 0x7fff;

    /// <summary><c>SLANG_GET_RESULT_CODE</c></summary>
    public static int Code(int r) => r & 0xffff;

    // ---- codes (slang.h:1368-1411) ----

    /// <summary><c>SLANG_OK</c></summary>
    public const int Ok = 0;

    /// <summary><c>SLANG_FAIL</c> — unspecified failure.</summary>
    public static readonly int Fail = MakeError(FacilityWinGeneral, 0x4005);

    /// <summary><c>SLANG_E_NOT_IMPLEMENTED</c></summary>
    public static readonly int NotImplemented = MakeError(FacilityWinGeneral, 0x4001);

    /// <summary><c>SLANG_E_NO_INTERFACE</c> — returned by <c>queryInterface</c> for an unsupported IID.</summary>
    public static readonly int NoInterface = MakeError(FacilityWinGeneral, 0x4002);

    /// <summary><c>SLANG_E_ABORT</c></summary>
    public static readonly int Abort = MakeError(FacilityWinGeneral, 0x4004);

    /// <summary><c>SLANG_E_INVALID_HANDLE</c></summary>
    public static readonly int InvalidHandle = MakeError(FacilityWinApi, 6);

    /// <summary><c>SLANG_E_INVALID_ARG</c></summary>
    public static readonly int InvalidArg = MakeError(FacilityWinApi, 0x57);

    /// <summary><c>SLANG_E_OUT_OF_MEMORY</c></summary>
    public static readonly int OutOfMemory = MakeError(FacilityWinApi, 0xe);

    /// <summary><c>SLANG_E_BUFFER_TOO_SMALL</c></summary>
    public static readonly int BufferTooSmall = MakeError(FacilityCore, 1);

    /// <summary><c>SLANG_E_UNINITIALIZED</c></summary>
    public static readonly int Uninitialized = MakeError(FacilityCore, 2);

    /// <summary><c>SLANG_E_PENDING</c></summary>
    public static readonly int Pending = MakeError(FacilityCore, 3);

    /// <summary><c>SLANG_E_CANNOT_OPEN</c></summary>
    public static readonly int CannotOpen = MakeError(FacilityCore, 4);

    /// <summary><c>SLANG_E_NOT_FOUND</c> — e.g. an entry point name that is not in the module.</summary>
    public static readonly int NotFound = MakeError(FacilityCore, 5);

    /// <summary><c>SLANG_E_INTERNAL_FAIL</c></summary>
    public static readonly int InternalFail = MakeError(FacilityCore, 6);

    /// <summary><c>SLANG_E_NOT_AVAILABLE</c></summary>
    public static readonly int NotAvailable = MakeError(FacilityCore, 7);

    /// <summary><c>SLANG_E_TIME_OUT</c></summary>
    public static readonly int TimeOut = MakeError(FacilityCore, 8);

    /// <summary>Human-readable name for a known result code; the raw hex otherwise.</summary>
    public static string Describe(int r)
    {
        if (r == Ok) return "SLANG_OK";
        if (r == Fail) return "SLANG_FAIL";
        if (r == NotImplemented) return "SLANG_E_NOT_IMPLEMENTED";
        if (r == NoInterface) return "SLANG_E_NO_INTERFACE";
        if (r == Abort) return "SLANG_E_ABORT";
        if (r == InvalidHandle) return "SLANG_E_INVALID_HANDLE";
        if (r == InvalidArg) return "SLANG_E_INVALID_ARG";
        if (r == OutOfMemory) return "SLANG_E_OUT_OF_MEMORY";
        if (r == BufferTooSmall) return "SLANG_E_BUFFER_TOO_SMALL";
        if (r == Uninitialized) return "SLANG_E_UNINITIALIZED";
        if (r == Pending) return "SLANG_E_PENDING";
        if (r == CannotOpen) return "SLANG_E_CANNOT_OPEN";
        if (r == NotFound) return "SLANG_E_NOT_FOUND";
        if (r == InternalFail) return "SLANG_E_INTERNAL_FAIL";
        if (r == NotAvailable) return "SLANG_E_NOT_AVAILABLE";
        if (r == TimeOut) return "SLANG_E_TIME_OUT";
        return $"0x{r:X8} (facility {Facility(r)}, code {Code(r)})";
    }
}
