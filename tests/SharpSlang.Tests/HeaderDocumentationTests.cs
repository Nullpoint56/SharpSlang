// The header facts our patches add to slang.h. They change no code, so only the header can show
// them: Slang's slang.h at the pin (SlangHeader, fetched and cached) with the patch applied in a
// temporary folder, as build-native.ps1 applies it to slang/ (AGENTS.md#patches). Not slang/ itself,
// which CI's test legs do not check out: they restore the payload built from it.

using System.Diagnostics;

namespace SharpSlang.Tests;

public sealed class HeaderDocumentationTests
{
    [PatchedFact("U12")]
    public void The_header_says_a_loaded_module_is_owned_by_the_session()
    {
        // U12, patched (0007): each of the four loadModule declarations says the session owns the
        // returned module, which LifetimeTests measures (a borrowed reference).
        var header = PatchedHeader("U12");
        var sentence = "The returned module is owned by the session and stays valid while the session lives";
        var count = 0;
        for (var at = header.IndexOf(sentence, StringComparison.Ordinal); at >= 0;
             at = header.IndexOf(sentence, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        Assert.Equal(4, count);
    }

    /// <summary>
    /// The pinned slang.h as the staged payload was built from it: with the patch the registry names
    /// for <paramref name="defect"/> when the payload carries it, as Slang ships it otherwise, so a
    /// payload built without the patch fails the test, and a Slang that documents it passes.
    /// </summary>
    private static string PatchedHeader(string defect)
    {
        var version = SlangHeader.Pinned.Version;   // fetches the header into the cache
        var pinned = Path.Combine(SlangHeader.RepoRoot(), "obj", "slang-headers", version, "slang.h");
        var patchFile = PatchRegistry.PatchFor(defect)!;
        if (!StagedPayload.Carries(patchFile)) return File.ReadAllText(pinned);
        var patch = Path.Combine(SlangHeader.RepoRoot(), "patches", patchFile);
        var dir = Directory.CreateTempSubdirectory("sharpslang-header-");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir.FullName, "include"));
            File.Copy(pinned, Path.Combine(dir.FullName, "include", "slang.h"));
            var git = new ProcessStartInfo("git")
            {
                WorkingDirectory = dir.FullName,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            foreach (var a in new[] { "apply", "--include=include/slang.h", patch }) git.ArgumentList.Add(a);
            using var process = Process.Start(git)!;
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, $"git apply {Path.GetFileName(patch)} to slang.h {version}: {error}");
            return File.ReadAllText(Path.Combine(dir.FullName, "include", "slang.h"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
