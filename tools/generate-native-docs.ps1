<#
.SYNOPSIS
    A step of tools/generate-native.ps1: Slang's header comments as XML docs on the generated L0.

.DESCRIPTION
    ClangSharp carries no comments. Slang documents its API in the headers, in every style C allows:
    a /** */, /*! */, /* */ or // block above a declaration, and a trailing /**< */, ///<, //< or //
    after one. This reads them from the traversed headers and writes them as XML docs on the
    matching declaration in dotnet/Native, matched by Slang's own names, which L0 keeps:

      types         struct, interface and enum, nested ones by their enclosing type
      members       fields, enum values, the constants of anonymous enums (on SlangApi), flat
                    functions (on SlangApi)
      vtable slots  by position: an interface's generated [VtblIndex] methods are its bases' slots
                    then its own, in declaration order (VtableOrderTests holds that), so overloads
                    and inherited slots are matched without guessing by name

    A comment attaches to the declaration it touches: a block ending on the line before it (a blank
    line or a preprocessor line between them detaches it), or a trailing comment on its line, and a
    // line aligned under a trailing comment continues it. Doxygen's @brief is dropped; @param and
    @return become <param> and <returns> when the names are exactly the generated parameters', and
    otherwise stay in <remarks>; `code` becomes <c>code</c>. The text is Slang's, unchanged except
    whitespace folded and XML-escaped. Not carried: comments inside function bodies, on members of
    anonymous structs and unions, on declarations ClangSharp does not generate (the C++ reflection
    veneer's methods, the excluded functions), separators and section banners.

        pwsh tools/generate-native-docs.ps1 -Headers <slang.h>,<slang-deprecated.h>,... -Output dotnet/Native
#>
param(
    [Parameter(Mandatory)] [string[]]$Headers,
    [Parameter(Mandatory)] [string]$Output
)

$ErrorActionPreference = "Stop"

if (-not ('SharpSlangDocs.Carrier' -as [type])) {
    Add-Type -TypeDefinition (Get-Content (Join-Path $PSScriptRoot "generate-native-docs.cs") -Raw) -Language CSharp
}

$headerText = @($Headers | ForEach-Object { [System.IO.File]::ReadAllText($_) })
$files = @(Get-ChildItem (Join-Path $Output "*.cs") | ForEach-Object { $_.FullName })
$report = [SharpSlangDocs.Carrier]::Run([string[]]$headerText, [string[]]$files)
Write-Host "    Slang header comments as XML docs: $($report.Types) types, $($report.Members) members, $($report.Slots) vtable slots (of $($report.TypeComments) type, $($report.MemberComments) member and $($report.SlotComments) slot comments read)"
foreach ($w in $report.Warnings) { Write-Warning $w }
# The comments read that matched no generated declaration (the veneer's methods, excluded functions)
Write-Verbose "not carried ($($report.NotCarried.Count)):`n  $($report.NotCarried -join "`n  ")"
if ($report.Types -eq 0 -or $report.Members -eq 0 -or $report.Slots -eq 0) { throw "no header comment was carried into L0: the header or the generated layout changed" }
