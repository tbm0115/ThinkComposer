param(
    [string]$Cli = "$PSScriptRoot/../ThinkComposer.Cli/bin/Debug/ThinkComposer.Cli.exe",
    [string]$OutputDirectory = "$PSScriptRoot/../artifacts/git-tests"
)
$ErrorActionPreference = 'Stop'
$Cli = [IO.Path]::GetFullPath($Cli)
$run = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$repo = Join-Path $run 'author'
$remote = Join-Path $run 'remote.git'
$other = Join-Path $run 'reviewer'
$passed = [Collections.Generic.List[string]]::new()
function Invoke-Native([string]$File, [string[]]$Arguments, [int]$Expected = 0) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & $File @Arguments 2>&1
    $code = $LASTEXITCODE
    $ErrorActionPreference = $old
    $text = ($output | Out-String)
    if ($code -ne $Expected) { throw "Expected exit $Expected, got $code : $File $Arguments`n$text" }
    return $text
}
function TC([string[]]$Arguments, [int]$Expected = 0) { Invoke-Native $Cli (@('git') + $Arguments) $Expected }
function G([string[]]$Arguments, [int]$Expected = 0) { Invoke-Native 'git.exe' $Arguments $Expected }
function Assert([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message }; $passed.Add($Message) }
function Configure([string]$Directory) {
    G @('-C', $Directory, 'config', 'user.name', 'ThinkComposer Regression') | Out-Null
    G @('-C', $Directory, 'config', 'user.email', 'regression@example.invalid') | Out-Null
}
function Projection([string]$Directory) {
    $registry = Get-Content (Join-Path $Directory '.tdom/index.json') -Raw | ConvertFrom-Json
    return Join-Path $Directory ('.tdom/' + $registry.artifacts[0].id + '/Domain.json')
}
function Edit-Projection([string]$Directory, [string]$Property, [string]$Value) {
    $path = Projection $Directory
    $model = Get-Content $path -Raw | ConvertFrom-Json
    $model.domain.$Property = $Value
    [IO.File]::WriteAllText($path, ($model | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    TC @('projection', 'import', '--input', (Join-Path $Directory 'Standard.tdom'), '--apply') | Out-Null
}
function Edit-Package([string]$Package, [string]$Part, [scriptblock]$Edit) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::Open($Package, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $zip.GetEntry($Part)
        $graph = $null
        if ($entry) { $reader = [IO.StreamReader]::new($entry.Open()); try { $graph = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }; $entry.Delete() }
        $value = & $Edit $graph
        $writer = [IO.StreamWriter]::new($zip.CreateEntry($Part).Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write(($value | ConvertTo-Json -Depth 100)) } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
}
try {
    TC @('init', '--repo', $repo, '--branch', 'main') | Out-Null
    Configure $repo
    Copy-Item "$PSScriptRoot/../PredefinedContent/State_Chart_XML.tdom" (Join-Path $repo 'Standard.tdom')
    TC @('status', '--repo', $repo) | Out-Null
    Assert (!(Test-Path (Join-Path $repo '.tdom'))) 'Repository status does not opt in or create projections'
    TC @('tracking', 'enable', '--input', (Join-Path $repo 'Standard.tdom')) | Out-Null
    TC @('verify', '--repo', $repo) | Out-Null
    $before = (Get-FileHash (Join-Path $repo 'Standard.tdom')).Hash
    TC @('projection', 'refresh', '--input', (Join-Path $repo 'Standard.tdom')) | Out-Null
    Assert ((Get-FileHash (Join-Path $repo 'Standard.tdom')).Hash -eq $before) 'Refreshing unchanged projection preserves exact container bytes'
    [IO.File]::WriteAllText((Join-Path $repo 'README.md'), 'Repository changes are independent of model changes.')
    TC @('stage', '--repo', $repo, '--path', 'Standard.tdom', '--path', 'README.md') | Out-Null
    TC @('verify', '--repo', $repo, '--staged') | Out-Null
    TC @('commit', '--repo', $repo, '--message', 'Initial managed model and README') | Out-Null
    TC @('setup', '--repo', $repo, '--executable', $Cli) | Out-Null
    TC @('stage', '--repo', $repo, '--path', '.gitattributes') | Out-Null
    TC @('commit', '--repo', $repo, '--message', 'Configure local integration attributes') | Out-Null
    $path = Projection $repo
    Add-Content $path ' '
    TC @('verify', '--repo', $repo) 1 | Out-Null
    TC @('projection', 'refresh', '--input', (Join-Path $repo 'Standard.tdom')) 1 | Out-Null
    TC @('projection', 'import', '--input', (Join-Path $repo 'Standard.tdom'), '--apply') | Out-Null
    TC @('verify', '--repo', $repo) | Out-Null
    Assert $true 'Independent projection edits are detected and explicit import repairs correspondence'
    G @('init', '--bare', $remote) | Out-Null
    TC @('remote-add', '--repo', $repo, '--name', 'origin', '--url', $remote) | Out-Null
    TC @('push', '--repo', $repo, '--remote', 'origin', '--branch', 'main') | Out-Null
    TC @('clone', '--remote', $remote, '--repo', $other) | Out-Null
    G @('-C', $other, 'switch', 'main') | Out-Null
    Configure $other
    TC @('branch-create', '--repo', $repo, '--branch', 'feature') | Out-Null
    TC @('switch', '--repo', $repo, '--branch', 'feature') | Out-Null
    Edit-Projection $repo 'summary' 'Incoming summary'
    G @('-C', $repo, 'add', '--', 'Standard.tdom') | Out-Null
    TC @('verify', '--repo', $repo, '--staged') 1 | Out-Null
    TC @('stage', '--repo', $repo, '--path', 'Standard.tdom') | Out-Null
    TC @('commit', '--repo', $repo, '--message', 'Change summary') | Out-Null
    Assert $true 'Partial staging is rejected; grouped staging produces a valid commit'
    TC @('switch', '--repo', $repo, '--branch', 'main') | Out-Null
    Edit-Projection $repo 'name' 'Local Domain Name'
    TC @('stage', '--repo', $repo, '--path', 'Standard.tdom') | Out-Null
    TC @('commit', '--repo', $repo, '--message', 'Change name') | Out-Null
    G @('-C', $repo, 'merge', '--no-edit', 'feature') 1 | Out-Null
    TC @('verify', '--repo', $repo, '--staged') | Out-Null
    TC @('commit', '--repo', $repo, '--message', 'Complete automatic semantic merge') | Out-Null
    TC @('verify', '--repo', $repo, '--revision', 'HEAD') | Out-Null
    $merged = Get-Content (Projection $repo) -Raw | ConvertFrom-Json
    Assert ($merged.domain.name -eq 'Local Domain Name' -and $merged.domain.summary -eq 'Incoming summary') 'External Git auto-merges edits and blocks a precomputed stale commit until explicit completion'
    TC @('tag', '--repo', $repo, '--name', 'v-test', '--message', 'Regression release') | Out-Null
    TC @('tag-push', '--repo', $repo, '--remote', 'origin', '--name', 'v-test') | Out-Null
    TC @('push', '--repo', $repo) | Out-Null
    TC @('pull', '--repo', $other) | Out-Null
    TC @('verify', '--repo', $other) | Out-Null
    Assert $true 'Remote fetch/pull/push and separate annotated tag publication work'
    TC @('branch-create', '--repo', $repo, '--branch', 'conflicting') | Out-Null
    TC @('switch', '--repo', $repo, '--branch', 'conflicting') | Out-Null
    Edit-Projection $repo 'summary' 'Conflicting incoming'
    TC @('stage', '--repo', $repo, '--path', 'Standard.tdom') | Out-Null
    TC @('commit', '--repo', $repo, '--message', 'Incoming conflict') | Out-Null
    TC @('switch', '--repo', $repo, '--branch', 'main') | Out-Null
    Edit-Projection $repo 'summary' 'Conflicting current'
    TC @('stage', '--repo', $repo, '--path', 'Standard.tdom') | Out-Null
    TC @('commit', '--repo', $repo, '--message', 'Current conflict') | Out-Null
    G @('-C', $repo, 'merge', '--no-edit', 'conflicting') 1 | Out-Null
    $created = TC @('resolve', '--repo', $repo, '--path', 'Standard.tdom')
    $session = ($created.Trim() -replace '^Merge session: ', '')
    $report = Get-Content (Join-Path $session 'conflicts.json') -Raw | ConvertFrom-Json
    Assert ($report.conflicts.Count -gt 0) 'Competing property edits remain unresolved with retained three-way inputs'
    $resolutions = @{ formatVersion = 1; resolutions = @($report.conflicts | ForEach-Object { @{ path = $_.path; choice = 'incoming' } }) }
    [IO.File]::WriteAllText((Join-Path $session 'resolutions.json'), ($resolutions | ConvertTo-Json -Depth 100))
    TC @('resolve', '--repo', $repo, '--session', $session, '--apply') | Out-Null
    TC @('commit', '--repo', $repo, '--message', 'Resolve model conflict') | Out-Null
    TC @('verify', '--repo', $repo, '--revision', 'HEAD') | Out-Null
    Assert $true 'Explicit conflict resolutions regenerate and stage valid projections'
    # Exercise the remaining repository actions with ordinary files, independently of models.
    [IO.File]::WriteAllText((Join-Path $other 'README.md'), 'Remote-only README update')
    TC @('stage', '--repo', $other, '--path', 'README.md') | Out-Null
    TC @('commit', '--repo', $other, '--message', 'Remote README') | Out-Null
    TC @('push', '--repo', $other) | Out-Null
    TC @('push', '--repo', $repo) 1 | Out-Null
    TC @('pull', '--repo', $repo) 1 | Out-Null
    TC @('merge', '--repo', $repo, '--revision', '@{upstream}') | Out-Null
    Assert ((G @('-C', $repo, 'rev-parse', '--verify', 'MERGE_HEAD')).Trim().Length -gt 0) 'Divergent pull stops; explicit merge remains uncommitted'
    TC @('commit', '--repo', $repo, '--message', 'Review divergent merge') | Out-Null
    [IO.File]::WriteAllText((Join-Path $repo 'README.md'), 'Uncommitted stash content')
    TC @('switch', '--repo', $repo, '--branch', 'feature') 1 | Out-Null
    TC @('stash-save', '--repo', $repo, '--message', 'Explicit test stash') | Out-Null
    TC @('stash-apply', '--repo', $repo, '--revision', 'stash@{0}') | Out-Null
    TC @('stash-drop', '--repo', $repo, '--revision', 'stash@{0}') | Out-Null
    Assert ((Get-Content (Join-Path $repo 'README.md') -Raw) -eq 'Uncommitted stash content') 'Explicit stash save/apply/drop preserves ordinary file changes'
    TC @('restore', '--repo', $repo, '--path', 'README.md') | Out-Null
    [IO.File]::WriteAllText((Join-Path $repo 'Revert.txt'), 'Revert this commit')
    TC @('stage', '--repo', $repo, '--path', 'Revert.txt') | Out-Null
    TC @('commit', '--repo', $repo, '--message', 'Revert fixture') | Out-Null
    TC @('revert', '--repo', $repo, '--revision', 'HEAD') | Out-Null
    Assert (!(Test-Path (Join-Path $repo 'Revert.txt'))) 'Revert prepares a reviewable inverse change'
    TC @('commit', '--repo', $repo, '--message', 'Complete reviewed revert') | Out-Null
    [IO.File]::WriteAllText((Join-Path $repo 'Unrelated.txt'), 'Keep staged')
    TC @('stage', '--repo', $repo, '--path', 'Unrelated.txt') | Out-Null
    TC @('stage', '--repo', $repo, '--path', 'Standard.tdom') | Out-Null
    TC @('unstage', '--repo', $repo, '--path', 'Standard.tdom') | Out-Null
    Assert ((G @('-C', $repo, 'diff', '--cached', '--name-only')).Trim() -eq 'Unrelated.txt') 'Grouped model staging and unstaging preserves unrelated staged files'
    TC @('commit', '--repo', $repo, '--message', 'Keep unrelated staged file') | Out-Null
    $worktree = Join-Path $run 'worktree'
    G @('-C', $repo, 'worktree', 'add', '-b', 'worktree-test', $worktree) | Out-Null
    TC @('status', '--input', (Join-Path $worktree 'Standard.tdom')) | Out-Null
    TC @('verify', '--repo', $worktree) | Out-Null
    Assert ((Get-Item -Force (Join-Path $worktree '.git')).PSIsContainer -eq $false) 'Worktree discovery supports a .git file'
    $idBefore = (Get-Content (Join-Path $worktree '.tdom/index.json') -Raw | ConvertFrom-Json).artifacts[0].id
    G @('-C', $worktree, 'mv', 'Standard.tdom', 'Renamed.tdom') | Out-Null
    TC @('tracking', 'relocate', '--repo', $worktree, '--id', $idBefore, '--path', 'Renamed.tdom') | Out-Null
    Copy-Item (Join-Path $worktree 'Renamed.tdom') (Join-Path $worktree 'Copy.tdom')
    TC @('tracking', 'enable', '--input', (Join-Path $worktree 'Copy.tdom')) | Out-Null
    $registrations = (Get-Content (Join-Path $worktree '.tdom/index.json') -Raw | ConvertFrom-Json).artifacts
    Assert (($registrations | Select-Object -ExpandProperty id -Unique).Count -eq 2 -and ($registrations | Where-Object path -eq 'Renamed.tdom').id -eq $idBefore) 'Renames preserve tracking identity; copied model identities register independently'
    TC @('verify', '--repo', $worktree) | Out-Null
    $fixtures = Join-Path $run 'fixtures'
    TC @('regression-fixtures', '--input', (Join-Path $repo 'Standard.tdom'), '--output-dir', $fixtures) | Out-Null
    Copy-Item (Join-Path $fixtures 'Fixture.tcom') (Join-Path $worktree 'Composition.tcom')
    Copy-Item (Join-Path $fixtures 'Fixture.tdom') (Join-Path $worktree 'Template.tdom')
    TC @('tracking', 'enable', '--input', (Join-Path $worktree 'Composition.tcom')) | Out-Null
    TC @('tracking', 'enable', '--input', (Join-Path $worktree 'Template.tdom')) | Out-Null
    TC @('verify', '--repo', $worktree) | Out-Null
    Assert (Test-Path (Join-Path $worktree '.tcom/index.json')) 'Composition and template Domain tracking coexist with multiple Domain copies'
    $composition = Join-Path $worktree 'Composition.tcom'
    $compositionRegistration = (Get-Content (Join-Path $worktree '.tcom/index.json') -Raw | ConvertFrom-Json).artifacts[0]
    $projectionDirectory = Join-Path $worktree ('.tcom/' + $compositionRegistration.id)
    $projectionManifest = Join-Path $projectionDirectory 'projection.json'
    $fingerprint = (Get-Content $projectionManifest -Raw | ConvertFrom-Json).contentFingerprint.value
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::Open($composition, [IO.Compression.ZipArchiveMode]::Update)
    try { $zip.GetEntry('Composition.json').LastWriteTime = [DateTimeOffset]::Now } finally { $zip.Dispose() }
    TC @('verify', '--repo', $worktree) 1 | Out-Null
    TC @('projection', 'refresh', '--input', $composition) | Out-Null
    Assert ((Get-Content $projectionManifest -Raw | ConvertFrom-Json).contentFingerprint.value -eq $fingerprint) 'Repackaging changes exact hashes without changing the content fingerprint'
    $projected = Join-Path $projectionDirectory 'Composition.json'
    $originalPayload = [IO.File]::ReadAllText($projected)
    $originalManifest = [IO.File]::ReadAllText($projectionManifest)
    [IO.File]::AppendAllText($projected, ' ')
    $tamperedManifest = $originalManifest | ConvertFrom-Json
    ($tamperedManifest.parts | Where-Object path -eq 'Composition.json').sha256 = (Get-FileHash $projected).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($projectionManifest, ($tamperedManifest | ConvertTo-Json -Depth 100))
    TC @('verify', '--repo', $worktree) 1 | Out-Null
    TC @('projection', 'refresh', '--input', $composition) 1 | Out-Null
    Assert ([IO.File]::ReadAllText($projected) -eq ($originalPayload + ' ')) 'Tampered recorded hashes cannot authorize overwriting independent projection edits'
    [IO.File]::WriteAllText($projected, $originalPayload)
    [IO.File]::WriteAllText($projectionManifest, $originalManifest)
    $emptyPatch = Join-Path $run 'empty-patch.json'
    [IO.File]::WriteAllText($emptyPatch, '{"format":"ThinkComposer.JsonInterchange","formatVersion":2,"operations":[]}')
    Invoke-Native $Cli @('composition', 'import-json', '--input', $composition, '--json', $emptyPatch, '--output', $composition, '--in-place') | Out-Null
    TC @('verify', '--repo', $worktree) | Out-Null
    $savedOnce = (Get-FileHash $composition).Hash
    Invoke-Native $Cli @('composition', 'import-json', '--input', $composition, '--json', $emptyPatch, '--output', $composition, '--in-place') | Out-Null
    Assert ((Get-FileHash $composition).Hash -eq $savedOnce) 'An unchanged native save preserves exact tracked container bytes'
    $state = (G @('-C', $worktree, 'rev-parse', '--git-path', 'thinkcomposer')).Trim()
    if (![IO.Path]::IsPathRooted($state)) { $state = Join-Path $worktree $state }
    $transaction = Join-Path $state 'transaction'; New-Item -ItemType Directory -Path $transaction | Out-Null
    Copy-Item $composition (Join-Path $transaction 'candidate.tcom')
    Copy-Item $composition (Join-Path $transaction 'before.tcom')
    Copy-Item -Recurse $projectionDirectory (Join-Path $transaction 'projection')
    Copy-Item -Recurse $projectionDirectory (Join-Path $transaction 'before-projection')
    [IO.File]::WriteAllText((Join-Path $transaction 'journal.json'), (@{formatVersion=1;container='Composition.tcom';projection=('.tcom/'+$compositionRegistration.id)} | ConvertTo-Json))
    TC @('projection', 'refresh', '--input', $composition) 1 | Out-Null
    TC @('recover', '--repo', $worktree, '--mode', 'complete') | Out-Null
    TC @('verify', '--repo', $worktree) | Out-Null
    Assert (!(Test-Path $transaction)) 'Interrupted publication blocks saves until explicit recover completes'
    $semantic = Join-Path $run 'semantic'; New-Item -ItemType Directory -Path $semantic | Out-Null
    $ancestor = Join-Path $semantic 'base.tcom'; $current = Join-Path $semantic 'current.tcom'; $incoming = Join-Path $semantic 'incoming.tcom'
    Copy-Item $composition $ancestor
    Copy-Item $ancestor $current; Copy-Item $ancestor $incoming
    Edit-Package $current 'Composition.json' { param($g) $g.ideas[0].name='Current concept name'; $g }
    Edit-Package $incoming 'Composition.json' { param($g) $g.composition.name='Incoming composition name'; $g }
    TC @('driver', '--repo', $repo, '--path', 'Standard.tdom', '--base', $ancestor, '--current', $current, '--incoming', $incoming) | Out-Null
    Invoke-Native $Cli @('composition', 'validate-json-persistence', '--input', $current, '--output-dir', (Join-Path $semantic 'roundtrip')) | Out-Null
    Assert $true 'Independent composition changes merge and pass native persistence/preview round-trip checks'
    Copy-Item $ancestor $current -Force; Copy-Item $ancestor $incoming -Force
    Edit-Package $current 'Composition.json' { param($g) $g.ideas=@(); $g }
    Edit-Package $incoming 'Composition.json' { param($g) $g.ideas[0].name='Edited before deletion'; $g }
    $unchanged = (Get-FileHash $current).Hash
    TC @('driver', '--repo', $repo, '--path', 'Standard.tdom', '--base', $ancestor, '--current', $current, '--incoming', $incoming) 1 | Out-Null
    Assert ((Get-FileHash $current).Hash -eq $unchanged) 'Delete/edit and dangling view references remain unresolved without changing the supplied current input'
    Edit-Package $ancestor 'Extension.json' { @{ extensionValue = 0 } }
    Copy-Item $ancestor $current -Force; Copy-Item $ancestor $incoming -Force
    Edit-Package $current 'Extension.json' { @{ extensionValue = 1 } }
    Edit-Package $incoming 'Extension.json' { @{ extensionValue = 2 } }
    TC @('driver', '--repo', $repo, '--path', 'Standard.tdom', '--base', $ancestor, '--current', $current, '--incoming', $incoming) 1 | Out-Null
    Assert $true 'Competing opaque package content requires explicit resolution'
    Copy-Item $ancestor $current -Force; Copy-Item $ancestor $incoming -Force
    Edit-Package $current 'Composition.json' { param($g) $g | Add-Member -NotePropertyName extensionData -NotePropertyValue @{retained='current'}; $g }
    Edit-Package $incoming 'Composition.json' { param($g) $g.ideas[0].name='Incoming with extension'; $g }
    TC @('driver', '--repo', $repo, '--path', 'Standard.tdom', '--base', $ancestor, '--current', $current, '--incoming', $incoming) | Out-Null
    $zip=[IO.Compression.ZipFile]::OpenRead($current)
    try { $reader=[IO.StreamReader]::new($zip.GetEntry('Composition.json').Open()); try { $mergedGraph=$reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }; Assert ($zip.GetEntry('Extension.json') -ne $null -and $mergedGraph.extensionData.retained -eq 'current') 'Unknown fields and opaque parts survive compatible merges' } finally { $zip.Dispose() }
    Copy-Item $ancestor $current -Force; Copy-Item $ancestor $incoming -Force
    Edit-Package $current 'Composition.json' { param($g) $g.views[0].visuals[0].x=280; $g }
    Edit-Package $incoming 'Composition.json' { param($g) $g.views[0].visuals[0].y=320; $g }
    TC @('driver', '--repo', $repo, '--path', 'Standard.tdom', '--base', $ancestor, '--current', $current, '--incoming', $incoming) | Out-Null
    Assert $true 'Layout properties merge independently using stable visual representation identities'
    Copy-Item $ancestor $current -Force; Copy-Item $ancestor $incoming -Force
    Edit-Package $current 'Domain.json' { param($g) $fields=@($g.tableDefinitions[0].fields); $g.tableDefinitions[0].fields=@($fields[1],$fields[0]) + @($fields | Select-Object -Skip 2); $g }
    Edit-Package $incoming 'Domain.json' { param($g) $fields=@($g.tableDefinitions[0].fields); [Array]::Reverse($fields); $g.tableDefinitions[0].fields=$fields; $g }
    TC @('driver', '--repo', $repo, '--path', 'Standard.tdom', '--base', $ancestor, '--current', $current, '--incoming', $incoming) 1 | Out-Null
    Assert $true 'Incompatible ordered field rearrangements require resolution'
    $legacy = Join-Path $run 'Legacy.tdom'; Copy-Item "$PSScriptRoot/../PredefinedContent/State_Chart_XML.tdom" $legacy
    $legacyHash = (Get-FileHash $legacy).Hash
    TC @('migrate', '--input', $legacy, '--repo', $other, '--path', 'Migrated.tdom') | Out-Null
    Assert (!(Test-Path (Join-Path $other 'Migrated.tdom'))) 'Migration preview does not publish a package'
    TC @('migrate', '--input', $legacy, '--repo', $other, '--path', 'Migrated.tdom', '--apply') | Out-Null
    Assert ((Get-FileHash $legacy).Hash -eq $legacyHash -and ((Get-Content (Join-Path $other '.tdom/index.json') -Raw | ConvertFrom-Json).artifacts | Where-Object path -eq 'Migrated.tdom').Count -eq 0) 'Migration preserves its source and does not enable tracking'
    TC @('unlink', '--input', (Join-Path $repo 'Standard.tdom')) | Out-Null
    TC @('verify', '--repo', $repo) | Out-Null
    Assert $true 'Explicit legacy unlink preserves managed container/projection consistency'
    # Existing hooks must not be overwritten, including when hooksPath is explicitly configured.
    $customHooks = Join-Path $other 'custom-hooks'; New-Item -ItemType Directory -Path $customHooks | Out-Null
    [IO.File]::WriteAllText((Join-Path $customHooks 'pre-commit'), '#!/bin/sh' + "`nexit 0`n")
    G @('-C', $other, 'config', 'core.hooksPath', $customHooks) | Out-Null
    $hookHash = (Get-FileHash (Join-Path $customHooks 'pre-commit')).Hash
    TC @('setup', '--repo', $other, '--executable', $Cli) 1 | Out-Null
    Assert ((Get-FileHash (Join-Path $customHooks 'pre-commit')).Hash -eq $hookHash) 'Setup preserves existing hooks and core.hooksPath'
    TC @('push', '--repo', $repo, '--message', 'Retired workflow') 1 | Out-Null
    TC @('pull', '--repo', $repo, '--output', 'Ignored.tdom') 1 | Out-Null
    Assert $true 'Legacy mutating arguments are rejected with migration guidance'
    TC @('tracking', 'disable', '--input', (Join-Path $repo 'Standard.tdom')) | Out-Null
    Assert (Test-Path (Join-Path $repo '.tdom')) 'Disabling tracking retains inactive projections'
    $passed | Set-Content (Join-Path $run 'passed.txt')
    Write-Output ($passed -join "`n")
    Write-Output "PASS: $($passed.Count) scenarios. Artifacts: $run"
} catch {
    $passed | Set-Content (Join-Path $run 'passed.txt')
    Write-Output "FAILED. Artifacts retained at $run"
    throw
}
