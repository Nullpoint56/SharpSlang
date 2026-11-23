// L2 — Slang's record/replay layer: the slang_*Record* and slang_*Replay* exports.
//
// The layer records every API call to a folder so a compiler bug can be replayed outside the
// application that hit it, which is what Slang's bug reports ask for. It is process-wide state:
// slang.h allows changing it only while no other thread is inside a Slang call.

using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang;

/// <summary>
/// Slang's record/replay layer: records the Slang calls a process makes into a replay folder,
/// and plays one back.
/// </summary>
/// <remarks>
/// Process-wide. Set <see cref="Directory"/> first, then enable it at startup before any
/// concurrent Slang use (<c>slang.h</c>). <c>SLANG_RECORD_LAYER=1</c> in the environment enables it
/// at load. A Slang build without the layer answers false, null and <c>SLANG_E_NOT_AVAILABLE</c>.
/// </remarks>
public static unsafe class SlangRecordReplay
{
    /// <summary><c>slang_isRecordLayerEnabled</c> and <c>slang_enableRecordLayer</c>.</summary>
    public static bool IsEnabled
    {
        get => NativeLibraryHint.Explained(() => SlangApi.slang_isRecordLayerEnabled() != 0);
        set => NativeLibraryHint.Explained(() =>
        {
            SlangApi.slang_enableRecordLayer(value ? (byte)1 : (byte)0);
            return 0;
        });
    }

    /// <summary>
    /// <c>slang_getReplayDirectory</c> and <c>slang_setReplayDirectory</c>: the folder recordings go
    /// under (<c>.slang-replays</c> by default). Set it before enabling the layer.
    /// </summary>
    public static string Directory
    {
        get => NativeLibraryHint.Explained(() => Utf8.Read(SlangApi.slang_getReplayDirectory()) ?? "");
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            NativeLibraryHint.Explained(() =>
            {
                using Utf8.Scoped p = new(value);
                SlangApi.slang_setReplayDirectory(p);
                return 0;
            });
        }
    }

    /// <summary><c>slang_getCurrentReplayPath</c>: the folder being recorded into, or null when not recording.</summary>
    public static string? CurrentReplayPath => NativeLibraryHint.Explained(() => Utf8.Read(SlangApi.slang_getCurrentReplayPath()));

    /// <summary><c>slang_loadReplay</c>: loads a recording and switches the layer to playback.</summary>
    /// <exception cref="SlangException">No recording there (<c>SLANG_E_NOT_FOUND</c>), or no layer in this build.</exception>
    public static void LoadReplay(string folderPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderPath);
        NativeLibraryHint.Explained(() =>
        {
            using Utf8.Scoped p = new(folderPath);
            Diagnostics.ThrowIfFailed(SlangApi.slang_loadReplay(p), $"slang_loadReplay('{folderPath}')", null);
            return 0;
        });
    }

    /// <summary><c>slang_loadLatestReplay</c>: loads the newest recording under <see cref="Directory"/>.</summary>
    /// <exception cref="SlangException">No recording there, or no layer in this build.</exception>
    public static void LoadLatestReplay() => NativeLibraryHint.Explained(() =>
    {
        Diagnostics.ThrowIfFailed(SlangApi.slang_loadLatestReplay(), "slang_loadLatestReplay", null);
        return 0;
    });

    /// <summary><c>slang_replayMarker</c>: a labelled point in the recording; nothing when not recording.</summary>
    public static void Marker(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        NativeLibraryHint.Explained(() =>
        {
            using Utf8.Scoped p = new(label);
            SlangApi.slang_replayMarker(p);
            return 0;
        });
    }
}
