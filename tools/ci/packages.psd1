# The packages this repository ships, and what each one must contain: the data behind
# tools/ci/check-packages.ps1, descs-version.ps1, release-body.ps1, tools/apidiff (api-changes.ps1)
# and tools/consume. A package added, renamed or split is a change here, not in those scripts.
#
# Placeholders, expanded by tools/ci/Packages.psm1:
#   {Version} {DescsVersion} {PipelineVersion} ...   a pin, read from Directory.Build.props by the
#                                                    property Pins names (a script's -Pins overrides)
#   {rid} {lib} {ext} {platform}                     per RID, from Rids, in PerRid entries
#   {Commit}                                         the commit the packages were built from
# A file pattern may use * and ?, matched against the package's entries (-like); the rest are exact.
@{
    # Every pin a package version or dependency is spelled with, as the Directory.Build.props
    # property that holds it. A pin missing there falls back to the <Version> of a project whose
    # package uses it.
    Pins = @{
        Version         = 'SharpSlangVersion'               # the release: tag v<it>
        DescsVersion    = 'SharpSlangDescsVersion'          # SharpSlang.Descs, moved only when the descs change
        PipelineVersion = 'SharpSlangPipelineVersion'       # SharpSlang.Pipeline, moved only when the reader changes
        Slang           = 'SlangVersion'                    # the pinned Slang release tag
        Dxc             = 'DxcPackageVersion'               # Microsoft.Direct3D.DXC
        SharpNri        = 'SharpNriPackageVersion'          # SharpNRI and SharpNRI.Testing
        SharpNriDescs   = 'SharpNriDescsPackageVersion'     # SharpNRI.Descs
    }

    # The platforms a native package carries, in description order. lib/ext spell the library file.
    Rids = @(
        @{ Rid = 'win-x64';     Platform = 'Windows x64'; Lib = '';    Ext = '.dll' }
        @{ Rid = 'linux-x64';   Platform = 'Linux x64';   Lib = 'lib'; Ext = '.so' }
        @{ Rid = 'linux-arm64'; Platform = 'Linux arm64'; Lib = 'lib'; Ext = '.so' }
        @{ Rid = 'osx-arm64';   Platform = 'macOS arm64'; Lib = 'lib'; Ext = '.dylib' }
    )

    # Third-party libraries bound or depended on, for the release body's "Bound libraries".
    Libraries = @(
        @{ Name = 'Slang'; Pin = 'Slang'; Url = 'https://github.com/shader-slang/slang/releases/tag/{Slang}' }
        @{ Name = 'DirectX Shader Compiler (Microsoft.Direct3D.DXC)'; Pin = 'Dxc'; Url = 'https://www.nuget.org/packages/Microsoft.Direct3D.DXC/{Dxc}'; Via = 'SharpSlang.Dxc' }
        @{ Name = 'SharpNRI.Descs'; Pin = 'SharpNriDescs'; Url = 'https://www.nuget.org/packages/SharpNRI.Descs/{SharpNriDescs}'; Via = 'SharpSlang.Pipeline.NRI' }
    )

    # Fields per package:
    #   Id, Project       package id and the project that packs it
    #   Version           the pin its version is (Pins)
    #   Sources           independently versioned packages only: the paths whose change since the
    #                     published version's <repository commit> requires a new version
    #                     (descs-version.ps1; git pathspecs, :(exclude) allowed)
    #   Files             entries it must carry
    #   PerRid            entries it must carry for every RID of the pack
    #   Forbidden         regexes no entry may match
    #   SourcesFromProject  with Sources: the project file and every file it compiles count too
    #                     (dotnet msbuild -getItem:Compile at HEAD), so files it links from
    #                     elsewhere (the reader enums under dotnet/Native) are not missed
    #   Dependencies      id = exact version range the nuspec must declare (every group)
    #   DependenciesWhenPacked  the same, required only when that package is in the pack (an
    #                     Optional package once it is split out)
    #   Props             entry = @{ element = value }: MSBuild properties a packed props file must set
    #   Contains          entry = @( text ... ): literal text a packed file must contain
    #   Description       'Native': the description ends with "Native library inside for: <platforms>."
    #   Commit            $false when <repository commit> is not required (a package with no build)
    #   Optional          checked when packed, not required to be (a package not split out yet,
    #                     whose Project is $null until it exists)
    #   Assemblies        whether it carries lib/ assemblies: in the API diff and the consume runs
    Packages = @(
        @{
            Id = 'SharpSlang'; Project = 'dotnet/SharpSlang.csproj'; Version = 'Version'
            Files = @(
                'lib/net11.0/SharpSlang.dll', 'lib/net11.0/SharpSlang.xml', 'lib/net11.0/SharpSlang.pdb'
                'buildTransitive/SharpSlang.props', 'buildTransitive/SharpSlang.targets'
                'README.md', 'THIRD-PARTY-NOTICES.md'
            )
            # For every RID: the compiler and the SPIR-V optimiser (slang-glslang carries
            # spirv-opt) under runtimes/<rid>/native/, nothing in a subfolder there (NuGet copies
            # it flat); the standard modules in standard-modules/<rid>/, folders kept, which the
            # buildTransitive targets put beside the compiler; Slang's licence texts and
            # SharpSlang's own in licenses/<rid>/, which they copy beside the application
            # (AGENTS.md#native-packages).
            PerRid = @(
                'runtimes/{rid}/native/{lib}slang-compiler{ext}'
                'runtimes/{rid}/native/{lib}slang-glslang*{ext}'
                'standard-modules/{rid}/slang-standard-module-{SlangNumeric}/slang/neural.slang-module'
                'standard-modules/{rid}/slang-standard-module-{SlangNumeric}/slang/numerics.slang-module'
                'standard-modules/{rid}/slang-standard-module-{SlangNumeric}/slang/functional.slang-module'
                'standard-modules/{rid}/slang-standard-module-{SlangNumeric}/experimental/workgraph.slang-module'
                'licenses/{rid}/Slang/LICENSE.txt'
                'licenses/{rid}/Slang/LICENSES/Apache-2.0.txt'
                'licenses/{rid}/Slang/third-party-notices/glslang-LICENSE.txt'
                'licenses/{rid}/SharpSlang/LICENSE.txt'
                'licenses/{rid}/SharpSlang/THIRD-PARTY-NOTICES.md'
            )
            Forbidden = @('^runtimes/[^/]+/(?!native/)', '^runtimes/.*(LICENSE|NOTICE|COPYING)', '^standard-modules/[^/]+/(?!slang-standard-module-)')
            # The binding names no pipeline package (AGENTS.md#layers)
            Dependencies = @{ 'SharpSlang.Descs' = '[{DescsVersion}]' }
            Props = @{ 'buildTransitive/SharpSlang.props' = @{ SharpSlangPackedVersion = '{Version}'; SharpSlangDescsDependency = '{DescsVersion}' } }
            # SHARPSLANG005: the restored Descs held to the one SharpSlang depends on; the copies
            # of the standard modules and the notices, and the notices' opt-out
            Contains = @{ 'buildTransitive/SharpSlang.targets' = @('SHARPSLANG005', 'standard-modules\', 'SharpSlangCopyLicenses') }
            Description = 'Native'
            Assemblies = $true
        }
        @{
            # The reader: baked packages, the reflection snapshot, the SPIR-V header. No compiler.
            Id = 'SharpSlang.Pipeline'; Project = 'pipeline/SharpSlang.Pipeline.csproj'; Version = 'PipelineVersion'
            Sources = @('pipeline'); SourcesFromProject = $true
            Files = @('lib/net11.0/SharpSlang.Pipeline.dll', 'lib/net11.0/SharpSlang.Pipeline.xml', 'lib/net11.0/SharpSlang.Pipeline.pdb', 'README.md')
            Forbidden = @('^runtimes/', '^licenses/', 'slang-compiler')
            Dependencies = @{ 'SharpSlang.Descs' = '[{DescsVersion}]' }
            Assemblies = $true
        }
        @{
            Id = 'SharpSlang.Pipeline.NRI'; Project = 'pipeline-nri/SharpSlang.Pipeline.NRI.csproj'; Version = 'Version'
            Files = @('lib/net11.0/SharpSlang.Pipeline.NRI.dll', 'lib/net11.0/SharpSlang.Pipeline.NRI.xml', 'lib/net11.0/SharpSlang.Pipeline.NRI.pdb', 'README.md')
            Forbidden = @('^runtimes/', '^licenses/')
            Dependencies = @{ 'SharpSlang.Pipeline' = '[{PipelineVersion}]'; 'SharpNRI.Descs' = '[{SharpNriDescs}]' }
            Assemblies = $true
        }
        @{
            # Targets only: Microsoft's own package at the pinned version, and the targets that
            # copy dxcompiler.dll and dxil.dll out of it. Redistributes no binary (Microsoft's
            # terms forbid a stand-alone offering; THIRD-PARTY-NOTICES.md).
            # A targets-only package: the targets under buildTransitive/<tfm>/ with lib/<tfm>/_._,
            # nothing under build/. SHARPSLANG011 fails a restore of another Microsoft.Direct3D.DXC,
            # read from project.assets.json.
            Id = 'SharpSlang.Dxc'; Project = 'dxc/SharpSlang.Dxc.csproj'; Version = 'Version'
            Files = @('buildTransitive/net11.0/SharpSlang.Dxc.targets', 'lib/net11.0/_._', 'README.md', 'THIRD-PARTY-NOTICES.md')
            Forbidden = @('^runtimes/', '\.(dll|so|dylib)$', '^licenses/', '^build/')
            Dependencies = @{ 'Microsoft.Direct3D.DXC' = '[{Dxc}]' }
            Contains = @{ 'buildTransitive/net11.0/SharpSlang.Dxc.targets' = @('>{Dxc}<', 'SHARPSLANG011', 'project.assets.json') }
            Commit = $false
            Assemblies = $false
        }
        @{
            # L0 without the DllImport file, and the managed desc forms: no native dependency.
            Id = 'SharpSlang.Descs'; Project = 'descs/SharpSlang.Descs.csproj'; Version = 'DescsVersion'
            Sources = @('dotnet/Native', ':(exclude)dotnet/Native/SlangApi.cs', 'descs', 'NativeSplit.props'); SourcesFromProject = $true
            Files = @('lib/net11.0/SharpSlang.Descs.dll', 'lib/net11.0/SharpSlang.Descs.xml', 'lib/net11.0/SharpSlang.Descs.pdb', 'README.md')
            Forbidden = @('^runtimes/', '^licenses/', '\.(dll|so|dylib)$(?<!SharpSlang\.Descs\.dll)', '^build')
            Assemblies = $true
        }
        @{
            # The cooker: the reflection walk and the baker, on the binding's public API. The
            # compiler comes with its SharpSlang dependency, not inside it.
            Id = 'SharpSlang.Pipeline.Bake'; Project = 'bake/SharpSlang.Pipeline.Bake.csproj'; Version = 'Version'
            Files = @('lib/net11.0/SharpSlang.Pipeline.Bake.dll', 'lib/net11.0/SharpSlang.Pipeline.Bake.xml', 'lib/net11.0/SharpSlang.Pipeline.Bake.pdb', 'README.md')
            Forbidden = @('^runtimes/', '^licenses/', 'slang-compiler')
            Dependencies = @{ 'SharpSlang' = '[{Version}]'; 'SharpSlang.Pipeline' = '[{PipelineVersion}]' }
            Assemblies = $true
        }
    )

    # tools/consume (consume.ps1): what each kind of run references (Optional: when packed), the
    # namespaces its Program.cs names, and its mode define. core, the cooker (the binding, dxc, the
    # baker and the NRI projection stored at bake time), runs portable, `run -r`, `publish -r` and
    # Native AOT; reader, nri and dxc are the optional packages alone.
    Consume = @{
        Runs = @{
            core   = @{ Packages = @('SharpSlang', 'SharpSlang.Dxc', 'SharpSlang.Pipeline.Bake', 'SharpSlang.Pipeline.NRI')
                        Usings = @('SharpSlang', 'SharpSlang.Native', 'SharpSlang.Baking', 'SharpSlang.Pipeline.NRI'); Defines = @('CONSUME_CORE', 'CONSUME_BAKE', 'CONSUME_NRI') }
            reader = @{ Packages = @('SharpSlang.Pipeline'); Usings = @('SharpSlang.Baking'); Defines = @('CONSUME_READER') }
            nri    = @{ Packages = @('SharpSlang.Pipeline.NRI'); Usings = @('SharpSlang.Baking', 'SharpSlang.Pipeline.NRI'); Defines = @('CONSUME_READER', 'CONSUME_NRI') }
            dxc    = @{ Packages = @('SharpSlang.Dxc'); Usings = @(); Defines = @('CONSUME_DXC') }
        }
        Binding      = 'SharpSlang'                      # the assembly whose packed layout the runs check
        CopyLicenses = 'SharpSlangCopyLicenses'          # the notices' opt-out property
        Compiler     = 'slang-compiler'                  # the native library the reader's output must not hold
        DxcFiles     = @('dxcompiler.dll', 'dxil.dll')   # what the dxc package copies on win-x64
        DxcPackage   = 'Microsoft.Direct3D.DXC'
        DxcPin       = 'Dxc'
        ErrorCode    = 'SHARPSLANG011'                   # a restored vendor runtime of another version (SharpSlang.Dxc.targets)
    }
}
