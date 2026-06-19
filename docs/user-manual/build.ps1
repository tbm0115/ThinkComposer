param(
    [string]$OutputPath = "output/ThinkComposer_User_Manual.pdf"
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $ScriptDir

try {
    $pandoc = Get-Command pandoc -ErrorAction SilentlyContinue
    if (-not $pandoc) {
        Write-Error "Pandoc is required to build the manual. Install Pandoc 3.x and try again."
    }

    $xelatex = Get-Command xelatex -ErrorAction SilentlyContinue
    if (-not $xelatex) {
        Write-Host "Cannot build PDF: xelatex is not installed or not on PATH." -ForegroundColor Yellow
        Write-Host "Install a TeX distribution such as MiKTeX or TeX Live, then rerun this script." -ForegroundColor Yellow
        Write-Host "Pandoc is available, so Markdown sources are ready for PDF generation once xelatex is installed." -ForegroundColor Yellow
        exit 2
    }

    New-Item -ItemType Directory -Force (Split-Path -Parent $OutputPath) | Out-Null

    $sources = @(
        "01-overview.md",
        "02-base-model.md",
        "03-application-guide.md",
        "04-current-features.md",
        "05-template-language.md",
        "06-information-model.md"
    )

    & $pandoc.Source `
        --from markdown+yaml_metadata_block+pipe_tables+fenced_code_blocks+backtick_code_blocks `
        --metadata-file "pandoc/metadata.yaml" `
        --include-in-header "pandoc/thinkcomposer.tex" `
        --pdf-engine xelatex `
        --resource-path "." `
        --toc `
        --number-sections `
        --top-level-division=chapter `
        --standalone `
        --output $OutputPath `
        @sources

    Write-Host "Built $OutputPath" -ForegroundColor Green
}
finally {
    Pop-Location
}
