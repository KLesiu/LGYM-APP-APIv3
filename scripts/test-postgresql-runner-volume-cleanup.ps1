param(
    [Parameter(Mandatory)]
    [string]$WorkingDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$runner = Join-Path $PSScriptRoot "run-postgresql-integration-tests.ps1"
if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) {
    throw "Could not find the PostgreSQL integration test runner."
}

if (Test-Path -LiteralPath $WorkingDirectory) {
    throw "The cleanup probe working directory must not already exist."
}

$workingParent = Split-Path -Parent $WorkingDirectory
if (-not (Test-Path -LiteralPath $workingParent -PathType Container)) {
    throw "The cleanup probe working directory parent does not exist."
}

function Get-DockerVolumeNames {
    $names = @(& docker volume ls --quiet)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list Docker volumes."
    }

    return $names
}

function Get-RunnerContainerNames {
    $names = @(& docker ps -a --filter "name=lgym-postgres-tests-" --format "{{.Names}}")
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list Docker containers."
    }

    return $names
}

$originalPath = $env:PATH
$beforeVolumes = @(Get-DockerVolumeNames)
$runnerExitCode = $null

try {
    $fakeBin = New-Item -ItemType Directory -Path (Join-Path $WorkingDirectory "fake-bin") -Force
    $resultsDirectory = New-Item -ItemType Directory -Path (Join-Path $WorkingDirectory "results") -Force
    $fakeDotnetPath = Join-Path $fakeBin.FullName $(if ($IsWindows) { "dotnet.cmd" } else { "dotnet" })
    if ($IsWindows) {
        [System.IO.File]::WriteAllText($fakeDotnetPath, "@echo Simulated dotnet test failure.`r`n@exit /b 23`r`n")
    }
    else {
        [System.IO.File]::WriteAllText($fakeDotnetPath, "#!/usr/bin/env sh`nexit 23`n")
        [System.IO.File]::SetUnixFileMode(
            $fakeDotnetPath,
            [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite -bor [System.IO.UnixFileMode]::UserExecute)
    }
    $env:PATH = "$($fakeBin.FullName)$([System.IO.Path]::PathSeparator)$originalPath"

    $originalErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & pwsh -NoProfile -File $runner -ResultsDirectory $resultsDirectory.FullName 2>&1 |
            ForEach-Object { Write-Host ([string]$_) }
        $runnerExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $originalErrorActionPreference
    }

    $afterVolumes = @(Get-DockerVolumeNames)
    $newVolumes = @($afterVolumes | Where-Object { $_ -notin $beforeVolumes })
    $runnerContainers = @(Get-RunnerContainerNames)

    if ($runnerExitCode -eq 0) {
        throw "The runner unexpectedly succeeded despite the simulated dotnet failure."
    }

    if ($runnerContainers.Count -ne 0) {
        throw "The runner left Docker containers after the simulated test failure: $($runnerContainers -join ', ')."
    }

    if ($newVolumes.Count -ne 0) {
        throw "The runner left Docker volumes after the simulated test failure: $($newVolumes -join ', ')."
    }

    Write-Host "PostgreSQL runner failure-path cleanup passed: exit=$runnerExitCode, containers=0, volumes=0."
}
finally {
    $env:PATH = $originalPath
    if (Test-Path -LiteralPath $WorkingDirectory) {
        Remove-Item -LiteralPath $WorkingDirectory -Recurse -Force
    }
}
