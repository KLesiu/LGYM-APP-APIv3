param(
    [Parameter(Mandatory = $false)]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

if ($Configuration -cnotin @('Debug', 'Release')) {
    throw 'Configuration must be Debug or Release.'
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$repositoryRoot = Split-Path -Parent $projectRoot
$playwrightScript = Join-Path $projectRoot "bin/$Configuration/net10.0/playwright.ps1"

if (-not (Test-Path -LiteralPath $playwrightScript -PathType Leaf)) {
    throw 'Build the E2E project before installing Chromium.'
}

$browserRoot = Join-Path $repositoryRoot '.e2e-private/browsers'
$originalBrowserPath = $env:PLAYWRIGHT_BROWSERS_PATH

try {
    $env:PLAYWRIGHT_BROWSERS_PATH = $browserRoot
    & $playwrightScript install chromium *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Playwright Chromium installation failed.'
    }
}
finally {
    $env:PLAYWRIGHT_BROWSERS_PATH = $originalBrowserPath
}
