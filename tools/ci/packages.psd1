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
            # The compiler, the SPIR-V optimiser (slang-glslang carries spirv-opt) and Slang's
            # licence texts, for every RID: publish.yml's check, with the licences moved out of
            # runtimes/ to licenses/<rid>/ (NuGet copies runtimes/<rid>/native/ flat, and ignores
            # the rest of runtimes/<rid>/).
            PerRid = @(
                'runtimes/{rid}/native/{lib}slang-compiler{ext}'
                'runtimes/{rid}/native/{lib}slang-glslang*{ext}'
                'licenses/{rid}/Slang/LICENSE.txt'
                'licenses/{rid}/Slang/LICENSES/Apache-2.0.txt'
            )
            Forbidden = @('^runtimes/[^/]+/(?!native/)')
            Dependencies = @{ 'SharpSlang.Descs' = '[{DescsVersion}]'; 'SharpSlang.Pipeline' = '[{PipelineVersion}]' }
            Props = @{ 'buildTransitive/SharpSlang.props' = @{ SharpSlangPackedVersion = '{Version}'; SharpSlangDescsDependency = '{DescsVersion}'; SharpSlangPipelineDependency = '{PipelineVersion}' } }
            # SHARPSLANG005: the restored Descs held to the one SharpSlang depends on
            Contains = @{ 'buildTransitive/SharpSlang.targets' = @('SHARPSLANG005') }
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
            # The targets' source is dxc/build/SharpSlang.Dxc.targets, packed today as
            # build/SharpSlang.Dxc.targets; a targets-only package carries them under
            # buildTransitive/<tfm>/ with lib/<tfm>/_._.
            Id = 'SharpSlang.Dxc'; Project = 'dxc/SharpSlang.Dxc.csproj'; Version = 'Version'
            Files = @('buildTransitive/net11.0/SharpSlang.Dxc.targets', 'lib/net11.0/_._', 'README.md', 'THIRD-PARTY-NOTICES.md')
            Forbidden = @('^runtimes/', '\.(dll|so|dylib)$', '^licenses/')
            Dependencies = @{ 'Microsoft.Direct3D.DXC' = '[{Dxc}]' }
            Contains = @{ 'buildTransitive/net11.0/SharpSlang.Dxc.targets' = @('>{Dxc}<'); 'build/SharpSlang.Dxc.targets' = @('>{Dxc}<') }
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
            # Not split out yet: no project, so nothing is checked until one is named here.
            Id = 'SharpSlang.Pipeline.Bake'; Project = $null; Version = 'Version'
            Files = @('lib/net11.0/SharpSlang.Pipeline.Bake.dll', 'lib/net11.0/SharpSlang.Pipeline.Bake.xml', 'lib/net11.0/SharpSlang.Pipeline.Bake.pdb', 'README.md')
            Forbidden = @('^runtimes/', '^licenses/')
            Dependencies = @{ 'SharpSlang' = '[{Version}]'; 'SharpSlang.Pipeline' = '[{PipelineVersion}]' }
            Optional = $true
            Assemblies = $true
        }
    )

    # tools/consume (consume.ps1): what each kind of run references (Optional: when packed), the
    # namespaces its Program.cs names, and its mode define. core runs portable, `run -r`,
    # `publish -r` and Native AOT; reader and dxc are the optional packages alone.
    Consume = @{
        Runs = @{
            core   = @{ Packages = @('SharpSlang', 'SharpSlang.Dxc'); Optional = @('SharpSlang.Pipeline.Bake')
                        Usings = @('SharpSlang', 'SharpSlang.Native', 'SharpSlang.Baking'); Defines = @('CONSUME_CORE', 'CONSUME_BAKE') }
            reader = @{ Packages = @('SharpSlang.Pipeline'); Usings = @('SharpSlang.Baking'); Defines = @('CONSUME_READER') }
            dxc    = @{ Packages = @('SharpSlang.Dxc'); Usings = @(); Defines = @('CONSUME_DXC') }
        }
        Binding      = 'SharpSlang'                      # the assembly that must declare IsAotCompatible
        Compiler     = 'slang-compiler'                  # the native library the reader's output must not hold
        DxcFiles     = @('dxcompiler.dll', 'dxil.dll')   # what the dxc package copies on win-x64
        DxcPackage   = 'Microsoft.Direct3D.DXC'
        DxcPin       = 'Dxc'
        ErrorCode    = 'SHARPSLANG\d{3}'                 # the build-time error codes (Directory.Build.targets)
    }
}
