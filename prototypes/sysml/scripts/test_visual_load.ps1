param(
    [string]$InputPackage = 'prototypes/sysml/fixtures/hidden-namesake-visible.tcom',
    [int]$ExpectedCount = 1,
    [string]$Screenshot = '',
    [string]$ViewId = '',
    [string]$BuildDirectory = 'artifacts/sysml-navigation-build'
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$buildPath = (Resolve-Path (Join-Path $repoRoot $BuildDirectory)).Path
$referencePath = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.6.1'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
# Different name from the older, potentially still-running full-load probe.
$testPath = Join-Path $buildPath 'DesktopVisualRegression.exe'
$arguments = @('/nologo', '/target:exe', '/platform:x86', "/out:$testPath")
foreach ($assembly in @('Instrumind.ThinkComposer.exe', 'Instrumind.Common.dll', 'DotLiquid.dll')) {
    $arguments += '/r:' + (Join-Path $buildPath $assembly)
}
foreach ($assembly in @('PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll', 'System.Xaml.dll')) {
    $arguments += '/r:' + (Join-Path $referencePath $assembly)
}
$arguments += Join-Path $PSScriptRoot 'VisualLoadRegression.cs'
& $compilerPath @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$testArguments = @((Join-Path $repoRoot $InputPackage), $ExpectedCount)
if ($Screenshot) { $testArguments += Join-Path $repoRoot $Screenshot }
if ($ViewId) {
    if (!$Screenshot) { throw 'ViewId requires a Screenshot path.' }
    $testArguments += $ViewId
}
& $testPath @testArguments
exit $LASTEXITCODE
