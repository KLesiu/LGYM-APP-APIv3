param(
    [string]$ResultsDirectory = "TestResults/Final",
    [string]$PostgreSqlConnectionString = "",
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "VerificationEvidence.psm1") -Force

function Assert-PathUnderRepository {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory)]
        [string]$Path
    )

    $root = ConvertTo-AbsolutePath -Path $RepositoryRoot
    $candidate = ConvertTo-AbsolutePath -Path $Path
    $rootPrefix = $root.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The verification results directory must be inside the repository."
    }

    return $candidate
}

function Invoke-RecordedCommand {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [Parameter(Mandatory)]
        [string]$LogPath
    )

    $startedUtc = [System.DateTimeOffset]::UtcNow
    $originalErrorActionPreference = $ErrorActionPreference
    $originalConsoleOutputEncoding = [Console]::OutputEncoding
    $originalOutputEncoding = $OutputEncoding
    try {
        $ErrorActionPreference = "Continue"
        $utf8 = [System.Text.UTF8Encoding]::new($false)
        [Console]::OutputEncoding = $utf8
        $OutputEncoding = $utf8
        $output = @(& $FilePath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $originalErrorActionPreference
        [Console]::OutputEncoding = $originalConsoleOutputEncoding
        $OutputEncoding = $originalOutputEncoding
    }

    $completedUtc = [System.DateTimeOffset]::UtcNow
    $redactedOutput = @($output | ForEach-Object { Protect-EvidenceString -Value ([string]$_) })
    [System.IO.File]::WriteAllLines($LogPath, $redactedOutput, [System.Text.UTF8Encoding]::new($false))
    foreach ($line in $redactedOutput) {
        Write-Host $line
    }

    return [pscustomobject]@{
        arguments = @($FilePath) + @($Arguments)
        exitCode = $exitCode
        startedUtc = $startedUtc.ToString("O")
        completedUtc = $completedUtc.ToString("O")
        logPath = $LogPath
    }
}

function Assert-CommandSucceeded {
    param(
        [Parameter(Mandatory)]
        [object]$Record,
        [Parameter(Mandatory)]
        [string]$Description
    )

    if ($Record.exitCode -ne 0) {
        throw "$Description exited with code $($Record.exitCode)."
    }
}

function New-DiscoveryManifest {
    param(
        [Parameter(Mandatory)]
        [object]$Suite,
        [Parameter(Mandatory)]
        [object]$Snapshot,
        [Parameter(Mandatory)]
        [string]$BuildHead,
        [Parameter(Mandatory)]
        [object]$CommandRecord,
        [Parameter(Mandatory)]
        [string[]]$Tests,
        [Parameter(Mandatory)]
        [string]$Path
    )

    $manifest = [pscustomobject]@{
        schemaVersion = 1
        kind = "test-discovery"
        suite = $Suite.name
        repository = $Snapshot
        buildHead = $BuildHead
        startedUtc = $CommandRecord.startedUtc
        completedUtc = $CommandRecord.completedUtc
        commands = @($CommandRecord)
        exitCode = $CommandRecord.exitCode
        declaredFilters = @($Suite.filters)
        identityScheme = "vstest-display-name-multiset-utf8-v1"
        tests = @($Tests)
        testCount = $Tests.Count
        testListSha256 = Get-StringListSha256 -Values $Tests
    }

    Write-RedactedJsonFile -Path $Path -Value $manifest -NoClobber
    return $manifest
}

function Invoke-TrxAssertion {
    param(
        [Parameter(Mandatory)]
        [string]$AssertTrxScript,
        [Parameter(Mandatory)]
        [object]$Suite,
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory)]
        [string]$Head,
        [Parameter(Mandatory)]
        [string]$BuildManifestPath,
        [Parameter(Mandatory)]
        [string]$DiscoveryManifestPath,
        [Parameter(Mandatory)]
        [string]$TrxDirectory,
        [Parameter(Mandatory)]
        [string]$NotBeforeUtc,
        [Parameter(Mandatory)]
        [object]$TestCommand,
        [Parameter(Mandatory)]
        [string]$EvidenceDirectory,
        [string]$ExpectedTrxFileName = ""
    )

    $arguments = @(
        "-NoProfile",
        "-File", $AssertTrxScript,
        "-SuiteName", $Suite.name,
        "-RepositoryRoot", $RepositoryRoot,
        "-ExpectedHead", $Head,
        "-BuildManifestPath", $BuildManifestPath,
        "-DiscoveryManifestPath", $DiscoveryManifestPath,
        "-TrxDirectory", $TrxDirectory,
        "-NotBeforeUtc", $NotBeforeUtc,
        "-CommandExitCode", [string]$TestCommand.exitCode,
        "-EvidenceDirectory", $EvidenceDirectory,
        "-CommandJson", (@($TestCommand.arguments | ForEach-Object { Protect-EvidenceString -Value $_ }) | ConvertTo-Json -Compress)
    )

    if (-not [string]::IsNullOrWhiteSpace($ExpectedTrxFileName)) {
        $arguments += @("-ExpectedTrxFileName", $ExpectedTrxFileName)
    }

    $assertLogPath = Join-Path (Split-Path -Parent $EvidenceDirectory) "$($Suite.name)-assert-trx.log"
    $assertRecord = Invoke-RecordedCommand -FilePath "pwsh" -Arguments $arguments -LogPath $assertLogPath
    Assert-CommandSucceeded -Record $assertRecord -Description "TRX assertion for $($Suite.name)"
    return (Join-Path $EvidenceDirectory "trx-summary.json")
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$resultsRoot = if ([System.IO.Path]::IsPathRooted($ResultsDirectory)) {
    Assert-PathUnderRepository -RepositoryRoot $repositoryRoot -Path $ResultsDirectory
}
else {
    Assert-PathUnderRepository -RepositoryRoot $repositoryRoot -Path (Join-Path $repositoryRoot $ResultsDirectory)
}

$attemptDirectory = $null
$commandRecords = [System.Collections.Generic.List[object]]::new()
try {
    if (-not [string]::Equals($Configuration, "Release", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Only the Release configuration is supported by the same-SHA verification matrix."
    }

    $Configuration = "Release"
    $initialSnapshot = Get-RepositorySnapshot -RepositoryRoot $repositoryRoot
    Assert-CleanRepositorySnapshot -Snapshot $initialSnapshot
    $attemptName = "{0}-{1}" -f [System.DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssZ"), [Guid]::NewGuid().ToString("N")
    $attemptDirectory = Join-Path $resultsRoot $attemptName
    if (Test-Path -LiteralPath $attemptDirectory) {
        throw "The generated verification attempt directory already exists."
    }

    $null = New-Item -ItemType Directory -Path $attemptDirectory -Force
    $logsDirectory = New-Item -ItemType Directory -Path (Join-Path $attemptDirectory "logs") -Force
    $manifestsDirectory = New-Item -ItemType Directory -Path (Join-Path $attemptDirectory "manifests") -Force
    $suiteRoot = New-Item -ItemType Directory -Path (Join-Path $attemptDirectory "suites") -Force
    $assertionRoot = New-Item -ItemType Directory -Path (Join-Path $attemptDirectory "assertions") -Force
    $snapshot = Get-RepositorySnapshot -RepositoryRoot $repositoryRoot
    Assert-CleanRepositorySnapshot -Snapshot $snapshot

    $buildStartedUtc = [System.DateTimeOffset]::UtcNow
    $restore = Invoke-RecordedCommand -FilePath "dotnet" -Arguments @("restore", "LgymApi.sln") -LogPath (Join-Path $logsDirectory.FullName "restore.log")
    $commandRecords.Add($restore)
    Assert-CommandSucceeded -Record $restore -Description "Restore"
    $build = Invoke-RecordedCommand -FilePath "dotnet" -Arguments @("build", "LgymApi.sln", "--configuration", $Configuration, "--no-restore") -LogPath (Join-Path $logsDirectory.FullName "build-release.log")
    $commandRecords.Add($build)
    Assert-CommandSucceeded -Record $build -Description "Release build"
    $buildCompletedUtc = [System.DateTimeOffset]::UtcNow
    $postBuildSnapshot = Get-RepositorySnapshot -RepositoryRoot $repositoryRoot
    Assert-CleanRepositorySnapshot -Snapshot $postBuildSnapshot
    if ($postBuildSnapshot.head -cne $snapshot.head) {
        throw "The repository HEAD changed during the Release build."
    }

    $buildManifestPath = Join-Path $manifestsDirectory.FullName "release-build.json"
    $buildManifest = [pscustomobject]@{
        schemaVersion = 1
        kind = "release-build"
        repository = $postBuildSnapshot
        startedUtc = $buildStartedUtc.ToString("O")
        completedUtc = $buildCompletedUtc.ToString("O")
        configuration = "Release"
        commands = @($restore, $build)
        exitCode = 0
    }
    Write-RedactedJsonFile -Path $buildManifestPath -Value $buildManifest -NoClobber

    $integrationProject = "LgymApi.IntegrationTests/LgymApi.IntegrationTests.csproj"
    $suites = @(
        [pscustomobject]@{ name = "Unit"; project = "LgymApi.UnitTests/LgymApi.UnitTests.csproj"; filters = @(); usesPostgreSqlRunner = $false },
        [pscustomobject]@{ name = "Architecture"; project = "LgymApi.ArchitectureTests/LgymApi.ArchitectureTests.csproj"; filters = @(); usesPostgreSqlRunner = $false },
        [pscustomobject]@{ name = "InMemoryIntegration"; project = $integrationProject; filters = @("TestCategory!=PostgreSql"); usesPostgreSqlRunner = $false },
        [pscustomobject]@{ name = "PostgreSqlIntegration"; project = $integrationProject; filters = @(); usesPostgreSqlRunner = $true },
        [pscustomobject]@{ name = "DataSeeder"; project = "LgymApi.DataSeeder.Tests/LgymApi.DataSeeder.Tests.csproj"; filters = @(); usesPostgreSqlRunner = $false }
    )

    $suiteArtifacts = [System.Collections.Generic.List[object]]::new()
    foreach ($suite in $suites) {
        $discoveryArguments = @("test", $suite.project, "--configuration", $Configuration, "--no-build", "--list-tests")
        foreach ($filter in $suite.filters) {
            $discoveryArguments += @("--filter", $filter)
        }

        $discoveryRecord = Invoke-RecordedCommand -FilePath "dotnet" -Arguments $discoveryArguments -LogPath (Join-Path $logsDirectory.FullName "$($suite.name)-discovery.log")
        $commandRecords.Add($discoveryRecord)
        Assert-CommandSucceeded -Record $discoveryRecord -Description "$($suite.name) discovery"
        $testNames = Get-ListedTestNames -Lines ([System.IO.File]::ReadAllLines($discoveryRecord.logPath))
        $discoveryPath = Join-Path $manifestsDirectory.FullName "$($suite.name)-discovery.json"
        $null = New-DiscoveryManifest -Suite $suite -Snapshot $postBuildSnapshot -BuildHead $postBuildSnapshot.head -CommandRecord $discoveryRecord -Tests $testNames -Path $discoveryPath

        $suiteDirectory = New-Item -ItemType Directory -Path (Join-Path $suiteRoot.FullName $suite.name) -Force
        $testStartedUtc = [System.DateTimeOffset]::UtcNow
        $expectedTrxFileName = "$($suite.name)-$([Guid]::NewGuid().ToString('N')).trx"
        if ($suite.usesPostgreSqlRunner) {
            $postgreSqlArguments = @("-NoProfile", "-File", (Join-Path $PSScriptRoot "run-postgresql-integration-tests.ps1"), "-ResultsDirectory", $suiteDirectory.FullName, "-NoBuild")
            if (-not [string]::IsNullOrWhiteSpace($PostgreSqlConnectionString)) {
                $postgreSqlArguments += @("-ConnectionString", $PostgreSqlConnectionString)
            }

            $testRecord = Invoke-RecordedCommand -FilePath "pwsh" -Arguments $postgreSqlArguments -LogPath (Join-Path $logsDirectory.FullName "$($suite.name)-test.log")
            $expectedTrxFileName = ""
        }
        else {
            $testArguments = @("test", $suite.project, "--configuration", $Configuration, "--no-build", "--logger", "trx;LogFileName=$expectedTrxFileName", "--results-directory", $suiteDirectory.FullName)
            foreach ($filter in $suite.filters) {
                $testArguments += @("--filter", $filter)
            }

            $testRecord = Invoke-RecordedCommand -FilePath "dotnet" -Arguments $testArguments -LogPath (Join-Path $logsDirectory.FullName "$($suite.name)-test.log")
        }

        $commandRecords.Add($testRecord)
        $assertionDirectory = Join-Path $assertionRoot.FullName $suite.name
        $summaryPath = Invoke-TrxAssertion -AssertTrxScript (Join-Path $PSScriptRoot "assert-trx.ps1") -Suite $suite -RepositoryRoot $repositoryRoot -Head $postBuildSnapshot.head -BuildManifestPath $buildManifestPath -DiscoveryManifestPath $discoveryPath -TrxDirectory $suiteDirectory.FullName -NotBeforeUtc $testStartedUtc.ToString("O") -TestCommand $testRecord -EvidenceDirectory $assertionDirectory -ExpectedTrxFileName $expectedTrxFileName
        $suiteArtifacts.Add([pscustomobject]@{
                suite = $suite.name
                discoveryManifest = $discoveryPath
                discoveryManifestSha256 = Get-FileSha256 -Path $discoveryPath
                summary = $summaryPath
                summarySha256 = Get-FileSha256 -Path $summaryPath
            })
    }

    $finalSnapshot = Get-RepositorySnapshot -RepositoryRoot $repositoryRoot
    Assert-CleanRepositorySnapshot -Snapshot $finalSnapshot
    if ($finalSnapshot.head -cne $postBuildSnapshot.head) {
        throw "The repository HEAD changed while the verification matrix was running."
    }

    $artifactManifest = [pscustomobject]@{
        schemaVersion = 1
        kind = "verification-matrix"
        outcome = "Passed"
        repository = $finalSnapshot
        startedUtc = $buildStartedUtc.ToString("O")
        completedUtc = [System.DateTimeOffset]::UtcNow.ToString("O")
        commands = @($commandRecords.ToArray())
        buildManifest = $buildManifestPath
        buildManifestSha256 = Get-FileSha256 -Path $buildManifestPath
        suites = @($suiteArtifacts.ToArray())
    }
    Write-RedactedJsonFile -Path (Join-Path $attemptDirectory "artifact-manifest.json") -Value $artifactManifest -NoClobber
    Protect-EvidenceValue -Value $artifactManifest | ConvertTo-Json -Depth 32
}
catch {
    if ($null -ne $attemptDirectory -and (Test-Path -LiteralPath $attemptDirectory -PathType Container)) {
        $failureManifest = [pscustomobject]@{
            schemaVersion = 1
            kind = "verification-matrix"
            outcome = "Failed"
            completedUtc = [System.DateTimeOffset]::UtcNow.ToString("O")
            commands = @($commandRecords.ToArray())
            error = $_.Exception.Message
        }
        Write-RedactedJsonFile -Path (Join-Path $attemptDirectory "artifact-manifest.json") -Value $failureManifest -NoClobber
    }

    Write-Error (Protect-EvidenceString -Value $_.Exception.Message)
    exit 1
}
