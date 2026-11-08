// The header facts our patches add to slang.h, read from the patched submodule: they change no code,
// so only the header can show whether a Slang release carries them (AGENTS.md#patches).

namespace SharpSlang.Tests;

public sealed class HeaderDocumentationTests
{
    [PatchedFact("U12")]
    public void The_header_says_a_loaded_module_is_owned_by_the_session()
    {
        // U12, patched (0007): each of the four loadModule declarations says the session owns the
        // returned module, which LifetimeTests measures (a borrowed reference).
        var header = File.ReadAllText(Path.Combine(SlangHeader.RepoRoot(), "slang", "include", "slang.h"));
        var sentence = "The returned module is owned by the session and stays valid while the session lives";
        var count = 0;
        for (var at = header.IndexOf(sentence, StringComparison.Ordinal); at >= 0;
             at = header.IndexOf(sentence, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        Assert.Equal(4, count);
    }
}
