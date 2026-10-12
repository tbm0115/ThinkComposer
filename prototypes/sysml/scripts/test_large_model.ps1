param(
    [string]$InputPackage = 'prototypes/sysml/reference/mtconnect/MTConnect_Imported.tcom',
    [string]$OutputPackage = 'artifacts/sysml-large-model-build/MTConnect_Saved.tcom',
    [string]$BuildDirectory = 'artifacts/sysml-large-model-build'
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$buildPath = (Resolve-Path (Join-Path $repoRoot $BuildDirectory)).Path
$referencePath = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.6.1'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$testPath = Join-Path $buildPath 'LargeModelPersistenceRegression.exe'
$arguments = @('/nologo', '/target:exe', '/platform:x86', "/out:$testPath")
foreach ($assembly in @('Instrumind.ThinkComposer.exe', 'Instrumind.Common.dll', 'DotLiquid.dll')) {
    $arguments += '/r:' + (Join-Path $buildPath $assembly)
}
foreach ($assembly in @('PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll', 'System.Xaml.dll')) {
    $arguments += '/r:' + (Join-Path $referencePath $assembly)
}
$arguments += Join-Path $PSScriptRoot 'LargeModelPersistenceRegression.cs'
& $compilerPath @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $testPath (Join-Path $repoRoot $InputPackage) (Join-Path $repoRoot $OutputPackage)
exit $LASTEXITCODE
