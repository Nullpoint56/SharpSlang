# nupkg-feed

A local NuGet folder feed, registered as `prismnri-local` in [`nuget.config`](../nuget.config). It
holds the three PrismNRI packages this repository consumes — `PrismNRI`, `PrismNRI.Descs` and
`PrismNRI.Testing` — at the version pinned as `PrismNriVersion` in
[`Directory.Build.props`](../Directory.Build.props).

The `*.nupkg` files are gitignored (by this folder's own `.gitignore`) and never committed. They
come from the GitHub release `v<PrismNriVersion>` of the private PrismNRI repository, so they are
reproducible from the pin:

```
pwsh tools/fetch-nri.ps1
```

The script needs a token that can read PrismNRI: `-Token`, or `PRISMNRI_TOKEN`, or `GITHUB_TOKEN`.
It skips packages already present; `-Force` re-downloads them.

The folder itself is kept in git, empty but for this file and the `.gitignore`, so that
`dotnet restore` never fails on a source that does not exist. A missing package is a clear NU1101 or
NU1102 naming the package and its version; a missing source directory would be NU1301, an error
about the source rather than the package, and it would point nowhere near the fix.
