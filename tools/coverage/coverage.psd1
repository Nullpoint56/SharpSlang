# What tools/coverage.ps1 measures, and from where. A project renamed, split or added is a change
# here, not in the script. Paths are relative to the repository root.
@{
    # Built with -p:<CoverageProperty>=true, which brings Microsoft.Testing.Extensions.CodeCoverage
    # into the test project (off otherwise: test-only, and no collector on every RID).
    Solution         = 'SharpSlang.slnx'
    CoverageProperty = 'SharpSlangCoverage'
    Configuration    = 'Release'
    TargetFramework  = 'net11.0'
    TestProject      = 'tests/SharpSlang.Tests'
    TestAssembly     = 'SharpSlang.Tests'

    # The shipped assemblies, where the binding's build puts them with their PDBs
    Bin = 'dotnet/bin/Release/net11.0'

    # The bound surface (tools/coverage/BoundSurface): the assemblies that hold the generated layer
    # and the binding code: L0 is split by file (NativeSplit.props), SlangApi into SharpSlang.dll and
    # the rest, the vtable structs among it, into SharpSlang.Descs.dll, which the build copies beside it.
    # The pipeline packages are not here: they call no Slang function, only the binding's public
    # API (AGENTS.md#layers; AssemblySplitTests reads the cooker's metadata for it).
    BoundSurface = @{
        Assemblies  = @('SharpSlang.dll', 'SharpSlang.Descs.dll')
        Namespace   = 'SharpSlang.Native'
        Api         = 'SlangApi'          # the one DllImport class
        Generated   = 'dotnet/Native'     # L0's source folder: methods from here are not binding code
        VtableField = 'lpVtbl'
        # Interfaces the host implements and Slang calls: not Slang's functions to bind
        HostImplemented = @('ISlangFileSystem', 'ISlangFileSystemExt', 'ISlangMutableFileSystem', 'ISlangWriter',
                            'ISlangSharedLibraryLoader', 'ISlangSharedLibrary', 'ISlangSharedLibrary_Dep1')
        # Interfaces that add no slot to their base, which the IL cannot order (slang.h:
        # struct ITypeConformance : public IComponentType)
        Bases = @{ ITypeConformance = 'IComponentType' }
    }

    # Test coverage: the modules measured (file names, no extension)
    Modules = @('SharpSlang', 'SharpSlang.Descs', 'SharpSlang.Pipeline', 'SharpSlang.Pipeline.Bake', 'SharpSlang.Pipeline.NRI')

    # Report areas by source path, first match wins; $null = not counted (generated L0)
    Areas = @(
        @{ Path = 'dotnet/Native/';      Area = $null }
        @{ Path = 'dotnet/Interop/';     Area = 'L1 dotnet/Interop' }
        @{ Path = 'dotnet/Core/';        Area = 'L2 dotnet/Core' }
        @{ Path = 'dotnet/Extensions/';  Area = 'L3 dotnet/Extensions' }
        @{ Path = 'dotnet/';             Area = 'dotnet/ (other)' }
        @{ Path = 'descs/';              Area = 'SharpSlang.Descs (descs/)' }
        @{ Path = 'pipeline/';           Area = 'SharpSlang.Pipeline (pipeline/)' }
        @{ Path = 'bake/';               Area = 'SharpSlang.Pipeline.Bake (bake/)' }
        @{ Path = 'pipeline-nri/';       Area = 'SharpSlang.Pipeline.NRI (pipeline-nri/)' }
    )

    # The measured inputs: these entries of the measured commit's tree are recorded, and -Check
    # compares them with HEAD's. A path that does not exist (patches/, before the first patch) is
    # recorded as absent, and its appearing counts as a change.
    Inputs = @('dotnet', 'descs', 'pipeline', 'bake', 'pipeline-nri', 'dxc', 'tests', 'patches', 'slang', 'NativeSplit.props')

    # The pin, for the report's upstream ref; the payload's payload.json says whether it is that
    # release's binaries or a source build, and which patches it carries.
    Upstream = @{ Pin = 'SlangVersion'; Name = 'Slang' }

    # The payload the tests load, identified by content: the library beside the test assembly,
    # compared with each staged variant
    Payload = @{
        Library = @{ Windows = 'slang-compiler.dll'; Linux = 'libslang-compiler.so'; macOS = 'libslang-compiler.dylib' }
        Variants = @(@{ Name = 'staged'; Path = 'runtimes' })
    }

    # The adapter each GPU backend's tests run on: the test executable started with Variable=Value
    # prints "ADAPTER <backend> <name>" or "UNAVAILABLE <backend> <reason>" lines and exits
    Adapters = @{ Variable = 'SHARPSLANG_TEST_CHILD'; Value = 'test-adapters' }

    Badges = @{ Surface = 'Slang surface bound'; Tests = 'test coverage' }
}
