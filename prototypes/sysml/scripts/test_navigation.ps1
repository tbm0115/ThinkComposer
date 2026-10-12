param([string]$BuildDirectory = 'artifacts/sysml-navigation-build')
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$buildPath = (Resolve-Path (Join-Path $repoRoot $BuildDirectory)).Path
$referencePath = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.6.1'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$testPath = Join-Path $buildPath 'NavigationRegression.exe'
$arguments = @('/nologo', '/target:exe', '/platform:x86', "/out:$testPath")
foreach ($assembly in @('Instrumind.ThinkComposer.exe', 'Instrumind.Common.dll', 'DotLiquid.dll')) {
    $arguments += '/r:' + (Join-Path $buildPath $assembly)
}
foreach ($assembly in @('PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll', 'System.Xaml.dll')) {
    $arguments += '/r:' + (Join-Path $referencePath $assembly)
}
$arguments += Join-Path $PSScriptRoot 'NavigationRegression.cs'
& $compilerPath @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $testPath (Join-Path $repoRoot 'prototypes/sysml/examples/SysML_v1_Starter.tcom')
exit $LASTEXITCODE
