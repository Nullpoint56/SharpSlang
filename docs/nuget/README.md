# SharpSlang

C# bindings over the [Slang](https://github.com/shader-slang/slang) shader compiler: compilation
and full reflection, plus opt-in packages that bake a shader's code and reflection into one file and
read it back without the compiler. Pure managed P/Invoke over a pinned Slang release. MIT licensed.
**Beta**: the API may still change between releases. Each release pins one Slang release, and every
package's major is that release as `YYYYRRP` (Slang v2026.21 is `2026210`).

```csharp
using SharpSlang;

using var global  = SlangGlobalSession.Create();
using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
using var module  = session.LoadModule("standard_surface");
using var vertex  = module.FindEntryPoint("vertexMain");
using var pixel   = module.FindEntryPoint("fragmentMain");
using var program = session.Link(module, vertex, pixel);

byte[] spirv = program.GetEntryPointCodeBytes(0, targetIndex: 0);
```

| Package | What it is |
|---|---|
| `SharpSlang` | the binding and Slang's native library, built from source with patches (Windows x64, Linux x64, Linux arm64; macOS arm64 as a zip on each GitHub release), with Slang's standard modules |
| `SharpSlang.Descs` | Slang's enums, descriptor structs and interface layouts as plain types, generated from `slang.h`; no native code |
| `SharpSlang.Pipeline` | reads baked shader packages and their reflection snapshot; no native code, so an application that loads baked shaders carries no compiler |
| `SharpSlang.Pipeline.Bake` | the cooker: reflects a linked program into the snapshot and bakes code and layout into a package |
| `SharpSlang.Pipeline.NRI` | projects the snapshot onto [NRI](https://github.com/NVIDIA-RTX/NRI)'s descs through `SharpNRI.Descs`, stored in the package at bake time |
| `SharpSlang.Dxc` | build targets that copy `dxcompiler.dll` and `dxil.dll` from Microsoft's `Microsoft.Direct3D.DXC` at the pinned version, for DXIL on win-x64, and fail the build on another version; your restore takes Microsoft's package |

**The native library is not Slang's release binary.** It is built from the pinned release's tag on
GitHub's runners with the [patches](https://github.com/Nullpoint56/SharpSlang/blob/master/patches/README.md)
applied, each fixing one Slang defect and carrying Slang's own regression test; every defect, its
report text and whether a patch fixes it are in
[docs/upstream.md](https://github.com/Nullpoint56/SharpSlang/blob/master/docs/upstream.md). Where a
patch applies, the library behaves differently from Slang's release. Each release attests the
libraries it built: `gh attestation verify <file> --repo Nullpoint56/SharpSlang` on a library from
the package, or on a `.nupkg` or zip from the GitHub release (nuget.org repository-signs the
`.nupkg` it serves, which changes its digest).

An application that references `SharpSlang` gets Slang's licence texts and SharpSlang's own in
`licenses/SharpSlang/<rid>/` beside it (`SharpSlangCopyLicenses=false` turns that off). The packages
declare trimming and Native AOT compatibility. .NET 11.

[The guide](https://github.com/Nullpoint56/SharpSlang/blob/master/docs/guide.md) covers compiling,
reflecting, baking, the NRI projection, lifetime, threads, and the traps.

[Repository](https://github.com/Nullpoint56/SharpSlang) ·
[Releases](https://github.com/Nullpoint56/SharpSlang/releases) ·
[Third-party notices](https://github.com/Nullpoint56/SharpSlang/blob/master/THIRD-PARTY-NOTICES.md)
