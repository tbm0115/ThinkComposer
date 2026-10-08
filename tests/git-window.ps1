param([string]$Bin = "$PSScriptRoot/../ThinkComposer/bin/Debug", [string]$OutputDirectory = "$PSScriptRoot/../artifacts/git-window")
# Run with 32-bit Windows PowerShell -STA (the desktop build targets x86).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
$Bin = [IO.Path]::GetFullPath($Bin)
[Reflection.Assembly]::LoadFrom("$Bin/Instrumind.Common.dll") | Out-Null
$assembly = [Reflection.Assembly]::LoadFrom("$Bin/Instrumind.ThinkComposer.exe")
$run = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
& git init --initial-branch=main $run 2>&1 | Out-Null
$app = New-Object Windows.Application
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$type = $assembly.GetType('Instrumind.ThinkComposer.Composer.GitSync.RepositoryWindow')
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$window = $type.GetConstructors()[0].Invoke([object[]]@($null, [string](Join-Path $run 'Example.tcom')))
function Field($name) { return $type.GetField($name, $flags).GetValue($window) }
function Invoke-Private($name) { $type.GetMethod($name, $flags).Invoke($window, @()) | Out-Null }
function Assert($condition, $message) { if (!$condition) { throw $message }; Write-Output "PASS: $message" }
function Set-Status {
    $status = [Instrumind.ThinkComposer.Composer.GitSync.RepositoryGit]::Status($run)
    (Field 'location').Text = $status.Root
    $type.GetField('repositoryStatus', $flags).SetValue($window, $status)
    Invoke-Private 'UpdateFileList'
    Invoke-Private 'UpdateActions'
    return $status
}
try {
    [IO.File]::WriteAllText((Join-Path $run 'Example.tcom'), 'UI-only container placeholder')
    [IO.File]::WriteAllText((Join-Path $run 'README.md'), "initial`n")
    $status = Set-Status
    Assert (!(Field 'commitButton').IsEnabled) 'Commit disabled before staging'
    Assert ((Field 'changes').Items.Count -eq 2) 'Untracked repository files appear in Changes'
    Assert ($status.Changes[0].DisplayLabel.StartsWith('New')) 'Untracked files have readable labels'
    $method = $type.GetMethod('FilePreview', [Reflection.BindingFlags]'Static,NonPublic')
    $model = $status.Changes | Where-Object Path -eq 'Example.tcom'
    $modelPreview = $method.Invoke($null, @([string]$run, $model.PSObject.BaseObject, $false))
    Assert ($modelPreview.Contains('Stage selected files')) 'New model preview explains the next action'
    [Instrumind.ThinkComposer.Composer.GitSync.RepositoryGit]::Execute($run, 'stage', $null, $null, [string[]]@('README.md'), [Threading.CancellationToken]::None) | Out-Null
    [IO.File]::WriteAllText((Join-Path $run 'README.md'), "initial`nunstaged addition`n")
    $status = Set-Status
    (Field 'commitSummary').Text = 'Add documentation'
    Assert ((Field 'commitButton').IsEnabled) 'Commit enabled with staged files and a summary'
    Assert ((Field 'commitButton').Content -eq 'Commit 1 staged file') 'Commit shows the actual index count'
    (Field 'location').Text = $run + '-different'
    Assert (!(Field 'commitButton').IsEnabled) 'Changing repository location disables commit until loaded'
    (Field 'location').Text = $run
    $readme = $status.Changes | Where-Object Path -eq 'README.md'
    $staged = $method.Invoke($null, @([string]$run, $readme.PSObject.BaseObject, $true))
    $working = $method.Invoke($null, @([string]$run, $readme.PSObject.BaseObject, $false))
    Assert (!$staged.Contains('unstaged addition') -and $working.Contains('unstaged addition')) 'Staged preview excludes unstaged changes'
    Assert (!(Test-Path (Join-Path $run '.tcom'))) 'Window does not enable optional tracking'
    $type.GetField('stagedView', $flags).SetValue($window, $true)
    Invoke-Private 'UpdateFileList'
    Assert ((Field 'changes').Items.Count -eq 1) 'Staged view shows only indexed files'
    $type.GetField('stagedView', $flags).SetValue($window, $false)
    Invoke-Private 'UpdateFileList'
    Invoke-Private 'UpdateActions'
    (Field 'changes').SelectedItem = ((Field 'changes').Items | Where-Object Path -eq 'Example.tcom')
    (Field 'repositoryName').Text = 'Example C# project'
    (Field 'branchButton').Content = 'Branch: main'
    (Field 'location').Text = $run
    (Field 'summary').Text = 'origin/main - 0 to push - 0 to pull'
    (Field 'trackingInfo').Text = 'Example.tcom - Model tracking is off (optional). Use Git normally, or enable it in Model tracking.'
    (Field 'fileHeading').Text = 'Example.tcom - Working changes'
    (Field 'preview').TextWrapping = 'Wrap'
    (Field 'preview').Text = $modelPreview
    foreach ($dark in @($false, $true)) {
        [Instrumind.ThinkComposer.ApplicationShell.ApplicationThemeManager]::SetDarkTheme($dark, $false)
        $content = $window.Content
        $content.Measure([Windows.Size]::new(1140, 750))
        $content.Arrange([Windows.Rect]::new(0, 0, 1140, 750))
        $content.UpdateLayout()
        $container = (Field 'changes').ItemContainerGenerator.ContainerFromItem((Field 'changes').SelectedItem)
        Assert ($container -ne $null -and $container.ActualHeight -ge 25) 'File rows have readable height'
        $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(1140, 750, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
        $bitmap.Render($content)
        $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $name = if ($dark) { 'dark.png' } else { 'light.png' }
        $stream = [IO.File]::Create((Join-Path $run $name))
        try { $encoder.Save($stream) } finally { $stream.Dispose() }
        Assert ((Field 'stageButton').Template -ne $null) "Theme button template renders ($name)"
    }
    Write-Output "Rendered output: $run"
} finally { (Field 'timer').Stop() }
exit 0







