param(
    [ValidateSet("add", "remove", "status")]
    [string] $Action = "add",

    [switch] $User
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$installDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$target = if ($User) { [EnvironmentVariableTarget]::User } else { [EnvironmentVariableTarget]::Machine }
$targetName = if ($User) { "user" } else { "machine" }

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Normalize-PathEntry([string] $pathEntry) {
    if ([string]::IsNullOrWhiteSpace($pathEntry)) {
        return ""
    }

    $normalized = [IO.Path]::GetFullPath($pathEntry.Trim().Trim('"'))
    $root = [IO.Path]::GetPathRoot($normalized)

    while ($normalized.Length -gt $root.Length -and ($normalized.EndsWith("\") -or $normalized.EndsWith("/"))) {
        $normalized = $normalized.Substring(0, $normalized.Length - 1)
    }

    return $normalized
}

function Split-PathValue([string] $pathValue) {
    if ([string]::IsNullOrWhiteSpace($pathValue)) {
        return @()
    }

    return @($pathValue.Split(";") | ForEach-Object { $_.Trim().Trim('"') } | Where-Object { $_ })
}

function Contains-PathEntry([string[]] $entries, [string] $pathEntry) {
    $normalizedTarget = Normalize-PathEntry $pathEntry
    foreach ($entry in $entries) {
        if ([string]::Equals((Normalize-PathEntry $entry), $normalizedTarget, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Remove-PathEntry([string[]] $entries, [string] $pathEntry) {
    $normalizedTarget = Normalize-PathEntry $pathEntry
    return @($entries | Where-Object {
        -not [string]::Equals((Normalize-PathEntry $_), $normalizedTarget, [StringComparison]::OrdinalIgnoreCase)
    })
}

function Send-EnvironmentChanged {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class ThinkComposerEnvironmentBroadcast
{
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, int Msg, UIntPtr wParam, string lParam, int fuFlags, int uTimeout, out UIntPtr lpdwResult);
}
"@

    $result = [UIntPtr]::Zero
    [void] [ThinkComposerEnvironmentBroadcast]::SendMessageTimeout([IntPtr]0xffff, 0x001A, [UIntPtr]::Zero, "Environment", 0x0002, 5000, [ref] $result)
}

if (-not $User -and $Action -ne "status" -and -not (Test-Administrator)) {
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        $Action
    )

    try {
        $process = Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -Verb RunAs -Wait -PassThru
        exit $process.ExitCode
    }
    catch {
        Write-Error "Administrator elevation is required to update the machine PATH. Run this command from an elevated Command Prompt, or pass -User to update only the current user's PATH."
        exit 1
    }
}

$currentPath = [Environment]::GetEnvironmentVariable("Path", $target)
$entries = Split-PathValue $currentPath
$isPresent = Contains-PathEntry $entries $installDir

if ($Action -eq "status") {
    if ($isPresent) {
        Write-Host "ThinkComposer install folder is present in the $targetName PATH:"
        Write-Host "  $installDir"
        exit 0
    }

    Write-Host "ThinkComposer install folder is not present in the $targetName PATH:"
    Write-Host "  $installDir"
    exit 1
}

if ($Action -eq "add") {
    if ($isPresent) {
        Write-Host "ThinkComposer install folder is already present in the $targetName PATH:"
        Write-Host "  $installDir"
        exit 0
    }

    $updatedEntries = @($entries + $installDir)
    [Environment]::SetEnvironmentVariable("Path", [string]::Join(";", $updatedEntries), $target)
    Send-EnvironmentChanged
    Write-Host "Added ThinkComposer install folder to the $targetName PATH:"
    Write-Host "  $installDir"
    Write-Host "Open a new Command Prompt and run: thinkcomposer --help"
    exit 0
}

if ($Action -eq "remove") {
    if (-not $isPresent) {
        Write-Host "ThinkComposer install folder is not present in the $targetName PATH:"
        Write-Host "  $installDir"
        exit 0
    }

    $updatedEntries = Remove-PathEntry $entries $installDir
    [Environment]::SetEnvironmentVariable("Path", [string]::Join(";", $updatedEntries), $target)
    Send-EnvironmentChanged
    Write-Host "Removed ThinkComposer install folder from the $targetName PATH:"
    Write-Host "  $installDir"
    exit 0
}
