Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "VerificationEvidence.psm1") -Force

function Write-FixtureJson {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [object]$Value
    )

    [System.IO.File]::WriteAllText(
        $Path,
        (($Value | ConvertTo-Json -Depth 16) + [Environment]::NewLine),
        [System.Text.UTF8Encoding]::new($false))
}

function Initialize-FixtureRepository {
    param(
        [Parameter(Mandatory)]
        [string]$Root
    )

    $repository = New-Item -ItemType Directory -Path (Join-Path $Root "repository") -Force
    [System.IO.File]::WriteAllText((Join-Path $repository.FullName "fixture.txt"), "fixture`n", [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText((Join-Path $repository.FullName "fixture-two.txt"), "fixture two`n", [System.Text.UTF8Encoding]::new($false))
    $fixtureProjectDirectory = New-Item -ItemType Directory -Path (Join-Path $repository.FullName "Fixture") -Force
    [System.IO.File]::WriteAllText((Join-Path $fixtureProjectDirectory.FullName "Fixture.csproj"), "<Project Sdk=`"Microsoft.NET.Sdk`" />`n", [System.Text.UTF8Encoding]::new($false))
    $null = & git -C $repository.FullName init --quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Could not initialize the temporary fixture repository."
    }

    $null = & git -C $repository.FullName add fixture.txt fixture-two.txt Fixture/Fixture.csproj
    if ($LASTEXITCODE -ne 0) {
        throw "Could not stage the fixture repository file."
    }

    $null = & git -C $repository.FullName -c user.name=fixture -c user.email=fixture@example.invalid commit --quiet -m fixture
    if ($LASTEXITCODE -ne 0) {
        throw "Could not commit the fixture repository file."
    }

    $head = (& git -C $repository.FullName rev-parse HEAD).Trim()
    $branch = (& git -C $repository.FullName branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$' -or [string]::IsNullOrWhiteSpace($branch)) {
        throw "The fixture repository state is invalid."
    }

    return [pscustomobject]@{
        path = $repository.FullName
        head = $head
        branch = $branch
    }
}

function New-ManifestPair {
    param(
        [Parameter(Mandatory)]
        [object]$Repository,
        [Parameter(Mandatory)]
        [string]$Directory,
        [Parameter(Mandatory)]
        [string[]]$Tests,
        [string[]]$Filters = @(),
        [string]$SuiteName = "Fixture"
    )

    $timestamps = [pscustomobject]@{
        buildStarted = [System.DateTimeOffset]::UtcNow.AddMinutes(-2).ToString("O")
        buildCompleted = [System.DateTimeOffset]::UtcNow.AddMinutes(-1).ToString("O")
        discoveryStarted = [System.DateTimeOffset]::UtcNow.AddSeconds(-30).ToString("O")
        discoveryCompleted = [System.DateTimeOffset]::UtcNow.AddSeconds(-20).ToString("O")
    }
    $repositoryManifest = [pscustomobject]@{
        path = $Repository.path
        branch = $Repository.branch
        head = $Repository.head
        worktree = [pscustomobject]@{
            isClean = $true
            status = @()
        }
    }
    $buildPath = Join-Path $Directory "release-build.json"
    $discoveryPath = Join-Path $Directory "discovery.json"
    $buildManifest = [pscustomobject]@{
        schemaVersion = 1
        kind = "release-build"
        repository = $repositoryManifest
        startedUtc = $timestamps.buildStarted
        completedUtc = $timestamps.buildCompleted
        configuration = "Release"
        commands = @([pscustomobject]@{
                arguments = @("dotnet", "build", "fixture.sln", "--configuration", "Release")
                exitCode = 0
            })
        exitCode = 0
    }
    $discoveryManifest = [pscustomobject]@{
        schemaVersion = 1
        kind = "test-discovery"
        suite = $SuiteName
        repository = $repositoryManifest
        buildHead = $Repository.head
        startedUtc = $timestamps.discoveryStarted
        completedUtc = $timestamps.discoveryCompleted
        commands = @([pscustomobject]@{
                arguments = @("dotnet", "test", "fixture.csproj", "--list-tests")
                exitCode = 0
            })
        exitCode = 0
        declaredFilters = @($Filters)
        identityScheme = "vstest-display-name-multiset-utf8-v1"
        tests = @($Tests)
        testCount = $Tests.Count
        testListSha256 = Get-StringListSha256 -Values $Tests
    }
    Write-FixtureJson -Path $buildPath -Value $buildManifest
    Write-FixtureJson -Path $discoveryPath -Value $discoveryManifest

    return [pscustomobject]@{
        buildPath = $buildPath
        discoveryPath = $discoveryPath
    }
}

function New-Case {
    param(
        [Parameter(Mandatory)]
        [object]$Fixture,
        [Parameter(Mandatory)]
        [string]$Name,
        [string[]]$Tests = @("Fixture.Pass"),
        [string[]]$Filters = @(),
        [string]$SuiteName = "Fixture"
    )

    $directory = New-Item -ItemType Directory -Path (Join-Path $Fixture.artifactRoot $Name) -Force
    $manifests = New-ManifestPair -Repository $Fixture.repository -Directory $directory.FullName -Tests $Tests -Filters $Filters -SuiteName $SuiteName
    $trxDirectory = New-Item -ItemType Directory -Path (Join-Path $directory.FullName "trx") -Force
    return [pscustomobject]@{
        name = $Name
        suiteName = $SuiteName
        buildPath = $manifests.buildPath
        discoveryPath = $manifests.discoveryPath
        trxDirectory = $trxDirectory.FullName
        expectedTrxFileName = "$Name.trx"
        evidenceDirectory = Join-Path $directory.FullName "summary"
        notBeforeUtc = [System.DateTimeOffset]::UtcNow.AddSeconds(-2)
        command = @("dotnet", "test", "fixture.csproj")
    }
}

function Write-TrxFixture {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$TestNames,
        [int]$Total,
        [int]$Executed,
        [int]$Passed,
        [int]$Failed,
        [int]$NotExecuted,
        [string]$Outcome = "Passed",
        [string[]]$ExecutionIds = @()
    )

    if ($ExecutionIds.Count -eq 0) {
        $ExecutionIds = @($TestNames | ForEach-Object { [Guid]::NewGuid().ToString() })
    }

    if ($ExecutionIds.Count -ne $TestNames.Count) {
        throw "The TRX fixture execution identity count must equal its test-name count."
    }

    $results = [System.Text.StringBuilder]::new()
    for ($index = 0; $index -lt $TestNames.Count; $index++) {
        [void]$results.AppendLine("    <UnitTestResult testName=`"$($TestNames[$index])`" executionId=`"$($ExecutionIds[$index])`" outcome=`"$Outcome`" />")
    }

    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
$results  </Results>
  <ResultSummary outcome="Completed">
    <Counters total="$Total" executed="$Executed" passed="$Passed" failed="$Failed" notExecuted="$NotExecuted" />
  </ResultSummary>
</TestRun>
"@
    [System.IO.File]::WriteAllText($Path, $xml, [System.Text.UTF8Encoding]::new($false))
}

function Write-OpenCoverFixture {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [int]$ModuleCount = 1,
        [string[]]$SourcePaths = @(),
        [string]$RootName = "CoverageSession",
        [switch]$Skipped,
        [switch]$WithDtd
    )

    $modules = [System.Text.StringBuilder]::new()
    for ($moduleIndex = 0; $moduleIndex -lt $ModuleCount; $moduleIndex++) {
        $skippedValue = if ($Skipped) { "true" } else { "false" }
        [void]$modules.AppendLine("    <Module skipped=`"$skippedValue`">")
        [void]$modules.AppendLine('      <Files>')
        for ($fileIndex = 0; $fileIndex -lt $SourcePaths.Count; $fileIndex++) {
            $sourcePath = [System.Security.SecurityElement]::Escape($SourcePaths[$fileIndex])
            [void]$modules.AppendLine("        <File uid=`"$($fileIndex + 1)`" fullPath=`"$sourcePath`" />")
        }

        [void]$modules.AppendLine('      </Files>')
        [void]$modules.AppendLine('    </Module>')
    }

    $dtd = if ($WithDtd) { '<!DOCTYPE CoverageSession [<!ENTITY blocked "blocked">]>' + [Environment]::NewLine } else { '' }
    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
$dtd<$RootName>
  <Modules>
$modules  </Modules>
</$RootName>
"@
    [System.IO.File]::WriteAllText($Path, $xml, [System.Text.UTF8Encoding]::new($false))
}

function Assert-CoverageSummaryMatchesReport {
    param(
        [Parameter(Mandatory)]
        [object]$Summary,
        [Parameter(Mandatory)]
        [string]$CoveragePath,
        [Parameter(Mandatory)]
        [System.DateTimeOffset]$NotBeforeUtc,
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $report = Get-ValidatedOpenCoverReport -CoveragePath $CoveragePath -NotBeforeUtc $NotBeforeUtc -RepositoryRoot $RepositoryRoot
    $summaryLastWriteUtc = ([System.DateTimeOffset]$Summary.coverage.lastWriteUtc).UtcDateTime
    if ($Summary.coverage.fileName -cne $report.file.Name -or
        $Summary.coverage.sha256 -cne (Get-FileSha256 -Path $report.file.FullName) -or
        $Summary.coverage.bytes -ne $report.file.Length -or
        $summaryLastWriteUtc -ne $report.file.LastWriteTimeUtc -or
        $Summary.coverage.moduleCount -ne $report.moduleCount -or
        $Summary.coverage.fileCount -ne $report.fileCount -or
        $Summary.coverage.localPathMode -cne $report.localPathMode) {
        throw "The coverage summary metadata does not match its validated OpenCover report."
    }
}

function Invoke-AssertFixture {
    param(
        [Parameter(Mandatory)]
        [object]$Fixture,
        [Parameter(Mandatory)]
        [object]$Case,
        [int]$CommandExitCode = 0,
        [string[]]$Command = @(),
        [switch]$Supplementary,
        [switch]$OmitExpectedTrxFileName,
        [string]$CoveragePath = ""
    )

    if ($Command.Count -eq 0) {
        $Command = $Case.command
    }

    $arguments = @(
        "-NoProfile",
        "-File", $Fixture.assertScript,
        "-SuiteName", $Case.suiteName,
        "-RepositoryRoot", $Fixture.repository.path,
        "-ExpectedHead", $Fixture.repository.head,
        "-BuildManifestPath", $Case.buildPath,
        "-DiscoveryManifestPath", $Case.discoveryPath,
        "-TrxDirectory", $Case.trxDirectory,
        "-NotBeforeUtc", $Case.notBeforeUtc.ToString("O"),
        "-CommandExitCode", [string]$CommandExitCode,
        "-EvidenceDirectory", $Case.evidenceDirectory,
        "-CommandJson", ($Command | ConvertTo-Json -Compress)
    )
    if (-not $OmitExpectedTrxFileName) {
        $arguments += @("-ExpectedTrxFileName", $Case.expectedTrxFileName)
    }

    if ($Supplementary) {
        $arguments += "-Supplementary"
    }

    if (-not [string]::IsNullOrWhiteSpace($CoveragePath)) {
        $arguments += @("-CoveragePath", $CoveragePath)
    }

    $originalErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = @(& pwsh @arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $originalErrorActionPreference
    }

    return [pscustomobject]@{
        exitCode = $exitCode
        output = (($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine)
    }
}

function Assert-NonZeroFixture {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [object]$Result
    )

    if ($Result.exitCode -eq 0) {
        throw "Fixture '$Name' unexpectedly succeeded."
    }
}

function Assert-ZeroFixture {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [object]$Result
    )

    if ($Result.exitCode -ne 0) {
        throw "Fixture '$Name' unexpectedly failed: $($Result.output)"
    }
}

function Assert-StringArrayEquals {
    param(
        [Parameter(Mandatory)]
        [string[]]$Expected,
        [Parameter(Mandatory)]
        [string[]]$Actual,
        [Parameter(Mandatory)]
        [string]$Description
    )

    if (-not (Test-StringArraysEqualOrdinal -Left $Expected -Right $Actual)) {
        throw "$Description did not match the expected ordered test set."
    }
}

function Assert-MatrixConfigurationContract {
    param(
        [Parameter(Mandatory)]
        [string]$MatrixScript
    )

    $repositoryRoot = Split-Path -Parent (Split-Path -Parent $MatrixScript)
    $sentinelName = "fixture-matrix-dirty-$([Guid]::NewGuid().ToString('N')).txt"
    $sentinelPath = Join-Path $repositoryRoot $sentinelName
    try {
        [System.IO.File]::WriteAllText($sentinelPath, "fixture`n", [System.Text.UTF8Encoding]::new($false))
        $status = @(& git -C $repositoryRoot status --porcelain=v1 --untracked-files=all)
        if ($LASTEXITCODE -ne 0 -or -not (($status | ForEach-Object { [string]$_ }) -contains "?? $sentinelName")) {
            throw "The matrix configuration fixture sentinel was not visible to git status."
        }

        $releaseOutput = @(& pwsh -NoProfile -File $MatrixScript -Configuration Release 2>&1)
        $releaseExitCode = $LASTEXITCODE
        $releaseText = ($releaseOutput | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
        if ($releaseExitCode -eq 0 -or $releaseText -notmatch 'Same-SHA evidence requires a clean worktree' -or $releaseText -match 'parameter cannot be found|Cannot find a parameter') {
            throw "The documented -Configuration Release invocation did not bind before the expected clean-worktree gate."
        }
    }
    finally {
        if (Test-Path -LiteralPath $sentinelPath) {
            Remove-Item -LiteralPath $sentinelPath -Force
        }

        $remainingStatus = @(& git -C $repositoryRoot status --porcelain=v1 --untracked-files=all)
        if ($LASTEXITCODE -ne 0 -or (($remainingStatus | ForEach-Object { [string]$_ }) -contains "?? $sentinelName")) {
            throw "The matrix configuration fixture sentinel was not removed."
        }
    }

    $unsupportedOutput = @(& pwsh -NoProfile -File $MatrixScript -Configuration Debug 2>&1)
    $unsupportedExitCode = $LASTEXITCODE
    $unsupportedText = ($unsupportedOutput | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    if ($unsupportedExitCode -eq 0 -or $unsupportedText -notmatch 'Only the Release configuration is supported') {
        throw "The matrix runner did not clearly reject an unsupported configuration."
    }
}

function Assert-EvidenceSerializationContract {
    param(
        [Parameter(Mandatory)]
        [object]$Repository,
        [Parameter(Mandatory)]
        [string]$ArtifactRoot
    )

    $snapshot = Get-RepositorySnapshot -RepositoryRoot $Repository.path
    if (-not $snapshot.worktree.isClean) {
        throw "The fixture repository must be clean before serializing its snapshot."
    }

    $cleanSnapshotPath = Join-Path $ArtifactRoot "clean-snapshot.json"
    Write-RedactedJsonFile -Path $cleanSnapshotPath -Value $snapshot
    $cleanSnapshotJson = [System.IO.File]::ReadAllText($cleanSnapshotPath)
    if ($cleanSnapshotJson -notmatch '"status"\s*:\s*\[\s*\]') {
        throw "A clean repository snapshot did not serialize worktree status as an empty JSON array."
    }

    $sequencePath = Join-Path $ArtifactRoot "sequence.json"
    Write-RedactedJsonFile -Path $sequencePath -Value ([pscustomobject]@{
            nullable = $null
            values = @("first", "second")
            password = "fixture-password"
        })
    $sequence = [System.IO.File]::ReadAllText($sequencePath) | ConvertFrom-Json -Depth 8
    if ($null -ne $sequence.nullable -or $sequence.values.Count -ne 2 -or $sequence.values[0] -cne "first" -or $sequence.values[1] -cne "second" -or $sequence.password -cne "[redacted]") {
        throw "Evidence serialization did not preserve nulls, ordered sequences, and credential redaction."
    }
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("lgym-assert-trx-" + [Guid]::NewGuid().ToString("N"))
try {
    $null = New-Item -ItemType Directory -Path $temporaryRoot -Force
    $repository = Initialize-FixtureRepository -Root $temporaryRoot
    $fixture = [pscustomobject]@{
        repository = $repository
        artifactRoot = (New-Item -ItemType Directory -Path (Join-Path $temporaryRoot "artifacts") -Force).FullName
        assertScript = Join-Path $PSScriptRoot "assert-trx.ps1"
    }

    Assert-EvidenceSerializationContract -Repository $repository -ArtifactRoot $fixture.artifactRoot

    $polishVstestListing = @(
        "Ostrzeżenie: diagnostyka kompilacji nie jest listą testów.",
        "Przebieg testu dla: fixture.dll (.NETCoreApp,Version=v10.0)",
        "Wersja 18.0.1 (x64) VSTest",
        "    warning CS0000: an indented diagnostic before the list is not a test name.",
        "",
        "Dostępne są następujące testy:",
        "    Zeta.FixtureTest",
        "    Alpha.FixtureTest",
        "    Parametr.FixtureTest(`"x y`")",
        "",
        "Wynik testu: powodzenie.",
        "warning CS0000: diagnostics after the listing are not test names."
    )
    $listedPolishTests = Get-ListedTestNames -Lines $polishVstestListing
    Assert-StringArrayEquals -Expected @("Alpha.FixtureTest", "Parametr.FixtureTest(`"x y`")", "Zeta.FixtureTest") -Actual $listedPolishTests -Description "Polish VSTest listing"
    if ($listedPolishTests -contains "Ostrzeżenie: diagnostyka kompilacji nie jest listą testów." -or $listedPolishTests -contains "warning CS0000: an indented diagnostic before the list is not a test name." -or $listedPolishTests -contains "Wynik testu: powodzenie." -or $listedPolishTests -contains "warning CS0000: diagnostics after the listing are not test names.") {
        throw "The localized VSTest parser treated a diagnostic or localized message as a test name."
    }

    $mojibakeVstestListing = @(
        "Dostępne są następujące testy:",
        "    Parametr(`"Podci─ůganie: im mniejszy ci─Ö┼╝ar, tym lepiej`")"
    )
    $mojibakeDisplayTests = Get-ListedTestNames -Lines $mojibakeVstestListing
    Assert-StringArrayEquals -Expected @("Parametr(`"Podciąganie: im mniejszy ciężar, tym lepiej`")") -Actual $mojibakeDisplayTests -Description "OEM-852-mojibake VSTest listing"

    $collidingDisplayListing = @(
        "Dostępne są następujące testy:",
        "    SharedDisplayName",
        "    SharedDisplayName",
        "    DistinctDisplayName"
    )
    $collidingDisplayTests = Get-ListedTestNames -Lines $collidingDisplayListing
    Assert-StringArrayEquals -Expected @("DistinctDisplayName", "SharedDisplayName", "SharedDisplayName") -Actual $collidingDisplayTests -Description "Colliding VSTest display listing"

    Assert-MatrixConfigurationContract -MatrixScript (Join-Path $PSScriptRoot "run-verification-matrix.ps1")

    $collidingDisplay = New-Case -Fixture $fixture -Name "colliding-display" -Tests @("Fixture.Shared", "Fixture.Shared")
    Write-TrxFixture -Path (Join-Path $collidingDisplay.trxDirectory $collidingDisplay.expectedTrxFileName) -TestNames @("Fixture.Shared", "Fixture.Shared") -Total 2 -Executed 2 -Passed 2 -Failed 0 -NotExecuted 0
    Assert-ZeroFixture -Name "valid colliding display names" -Result (Invoke-AssertFixture -Fixture $fixture -Case $collidingDisplay)

    $valid = New-Case -Fixture $fixture -Name "valid"
    Write-TrxFixture -Path (Join-Path $valid.trxDirectory $valid.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $validResult = Invoke-AssertFixture -Fixture $fixture -Case $valid -Command @("dotnet", "test", "fixture.csproj", "-ConnectionString", "Host=localhost;Username=fixture;Password=fixture-password;Token=fixture-token")
    Assert-ZeroFixture -Name "valid full suite" -Result $validResult
    if ($validResult.output.Contains("fixture-password") -or $validResult.output.Contains("fixture-token") -or -not $validResult.output.Contains("[redacted]")) {
        throw "The valid evidence output did not redact credential-shaped values."
    }

    $validJson = $validResult.output | ConvertFrom-Json -Depth 32
    if ($validJson.kind -cne "trx-summary" -or -not $validJson.completeSuite -or $validJson.trx.testCount -ne 1 -or @($validJson.PSObject.Properties | Where-Object { $_.Name -ceq "coverage" }).Count -ne 0) {
        throw "The valid evidence output did not have the expected JSON summary schema."
    }

    $summaryJson = [System.IO.File]::ReadAllText((Join-Path $valid.evidenceDirectory "trx-summary.json")) | ConvertFrom-Json -Depth 32
    if ($summaryJson.repository.head -cne $repository.head -or $summaryJson.command.exitCode -ne 0) {
        throw "The saved valid evidence summary is not valid JSON with the expected same-SHA fields."
    }

    $validCoverage = New-Case -Fixture $fixture -Name "valid-coverage"
    Write-TrxFixture -Path (Join-Path $validCoverage.trxDirectory $validCoverage.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $validCoveragePath = Join-Path (Split-Path -Parent $validCoverage.trxDirectory) "coverage.opencover.xml"
    Write-OpenCoverFixture -Path $validCoveragePath -SourcePaths @((Join-Path $repository.path "fixture.txt"))
    $validCoverageResult = Invoke-AssertFixture -Fixture $fixture -Case $validCoverage -CoveragePath $validCoveragePath
    Assert-ZeroFixture -Name "valid OpenCover report" -Result $validCoverageResult
    $validCoverageSummary = [System.IO.File]::ReadAllText((Join-Path $validCoverage.evidenceDirectory "trx-summary.json")) | ConvertFrom-Json -Depth 32
    Assert-CoverageSummaryMatchesReport -Summary $validCoverageSummary -CoveragePath $validCoveragePath -NotBeforeUtc $validCoverage.notBeforeUtc -RepositoryRoot $repository.path
    if ($validCoverageSummary.coverage.path -cne [System.IO.Path]::GetFullPath($validCoveragePath)) {
        throw "The saved coverage summary does not record the validated OpenCover path."
    }

    $missingRepositoryContainedGeneratedReleaseSource = New-Case -Fixture $fixture -Name "missing-repository-contained-generated-release-source"
    Write-TrxFixture -Path (Join-Path $missingRepositoryContainedGeneratedReleaseSource.trxDirectory $missingRepositoryContainedGeneratedReleaseSource.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $missingRepositoryContainedGeneratedReleaseCoveragePath = Join-Path (Split-Path -Parent $missingRepositoryContainedGeneratedReleaseSource.trxDirectory) "coverage.opencover.xml"
    $missingRepositoryContainedGeneratedReleasePath = Join-Path $repository.path "Fixture\obj\Release\net10.0\Generator\Generated.g.cs"
    Write-OpenCoverFixture -Path $missingRepositoryContainedGeneratedReleaseCoveragePath -SourcePaths @((Join-Path $repository.path "fixture.txt"), $missingRepositoryContainedGeneratedReleasePath)
    $missingRepositoryContainedGeneratedReleaseReport = Get-ValidatedOpenCoverReport -CoveragePath $missingRepositoryContainedGeneratedReleaseCoveragePath -NotBeforeUtc $missingRepositoryContainedGeneratedReleaseSource.notBeforeUtc -RepositoryRoot $repository.path
    if ($missingRepositoryContainedGeneratedReleaseReport.moduleCount -ne 1 -or
        $missingRepositoryContainedGeneratedReleaseReport.fileCount -ne 2 -or
        $missingRepositoryContainedGeneratedReleaseReport.sourcePaths -isnot [string[]] -or
        $missingRepositoryContainedGeneratedReleaseReport.sourcePaths.Count -ne 1 -or
        $missingRepositoryContainedGeneratedReleaseReport.sourcePaths[0] -cne (Join-Path $repository.path "fixture.txt")) {
        throw "The missing repository-contained generated Release source was not safely omitted from materialized OpenCover source paths."
    }

    $onlyMissingRepositoryContainedGeneratedReleaseSource = New-Case -Fixture $fixture -Name "only-missing-repository-contained-generated-release-source"
    Write-TrxFixture -Path (Join-Path $onlyMissingRepositoryContainedGeneratedReleaseSource.trxDirectory $onlyMissingRepositoryContainedGeneratedReleaseSource.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $onlyMissingRepositoryContainedGeneratedReleaseCoveragePath = Join-Path (Split-Path -Parent $onlyMissingRepositoryContainedGeneratedReleaseSource.trxDirectory) "coverage.opencover.xml"
    Write-OpenCoverFixture -Path $onlyMissingRepositoryContainedGeneratedReleaseCoveragePath -SourcePaths @($missingRepositoryContainedGeneratedReleasePath)
    $onlyMissingRepositoryContainedGeneratedReleaseReport = Get-ValidatedOpenCoverReport -CoveragePath $onlyMissingRepositoryContainedGeneratedReleaseCoveragePath -NotBeforeUtc $onlyMissingRepositoryContainedGeneratedReleaseSource.notBeforeUtc -RepositoryRoot $repository.path
    if ($onlyMissingRepositoryContainedGeneratedReleaseReport.fileCount -ne 1 -or
        $onlyMissingRepositoryContainedGeneratedReleaseReport.sourcePaths -isnot [string[]] -or
        $onlyMissingRepositoryContainedGeneratedReleaseReport.sourcePaths.Count -ne 0) {
        throw "A report containing only a missing repository-contained generated Release source did not return an empty materialized source path set."
    }

    foreach ($missingSourceCase in @(
            [pscustomobject]@{ name = "missing-ordinary-repository-source"; path = (Join-Path $repository.path "Fixture\Missing.cs") },
            [pscustomobject]@{ name = "missing-non-generated-release-source"; path = (Join-Path $repository.path "Fixture\obj\Release\net10.0\Generator\Generated.cs") },
            [pscustomobject]@{ name = "missing-generated-debug-source"; path = (Join-Path $repository.path "Fixture\obj\Debug\net10.0\Generator\Generated.g.cs") },
            [pscustomobject]@{ name = "missing-release-non-csharp-source"; path = (Join-Path $repository.path "Fixture\obj\Release\net10.0\Generator\Generated.g.txt") })) {
        $missingSource = New-Case -Fixture $fixture -Name $missingSourceCase.name
        Write-TrxFixture -Path (Join-Path $missingSource.trxDirectory $missingSource.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
        $missingSourceCoveragePath = Join-Path (Split-Path -Parent $missingSource.trxDirectory) "coverage.opencover.xml"
        Write-OpenCoverFixture -Path $missingSourceCoveragePath -SourcePaths @($missingSourceCase.path)
        Assert-NonZeroFixture -Name $missingSourceCase.name -Result (Invoke-AssertFixture -Fixture $fixture -Case $missingSource -CoveragePath $missingSourceCoveragePath)
    }

    $multiFileCoverage = New-Case -Fixture $fixture -Name "multi-file-coverage"
    Write-TrxFixture -Path (Join-Path $multiFileCoverage.trxDirectory $multiFileCoverage.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $multiFileCoveragePath = Join-Path (Split-Path -Parent $multiFileCoverage.trxDirectory) "coverage.opencover.xml"
    $multiFileSourcePaths = @((Join-Path $repository.path "fixture-two.txt"), (Join-Path $repository.path "fixture.txt"))
    Write-OpenCoverFixture -Path $multiFileCoveragePath -ModuleCount 2 -SourcePaths $multiFileSourcePaths
    $multiFileReport = Get-ValidatedOpenCoverReport -CoveragePath $multiFileCoveragePath -NotBeforeUtc $multiFileCoverage.notBeforeUtc -RepositoryRoot $repository.path
    $expectedMultiFileSourcePaths = @(($multiFileSourcePaths + $multiFileSourcePaths) | Sort-Object)
    if ($multiFileReport.sourcePaths -isnot [string[]] -or $multiFileReport.sourcePaths.Count -ne 4 -or -not (Test-StringArraysEqualOrdinal -Left $multiFileReport.sourcePaths -Right $expectedMultiFileSourcePaths)) {
        throw "The multi-module OpenCover report did not return a flat ordinally sorted string array."
    }

    $validCoverageSummary.coverage.sha256 = "0" * 64
    Write-FixtureJson -Path (Join-Path $validCoverage.evidenceDirectory "trx-summary.json") -Value $validCoverageSummary
    $tamperedSummaryResult = Invoke-AssertFixture -Fixture $fixture -Case $validCoverage -CoveragePath $validCoveragePath
    Assert-NonZeroFixture -Name "tampered coverage summary cannot be overwritten" -Result $tamperedSummaryResult
    $tamperedSummaryRejected = $false
    try {
        Assert-CoverageSummaryMatchesReport -Summary $validCoverageSummary -CoveragePath $validCoveragePath -NotBeforeUtc $validCoverage.notBeforeUtc -RepositoryRoot $repository.path
    }
    catch {
        $tamperedSummaryRejected = $true
    }

    if (-not $tamperedSummaryRejected) {
        throw "Tampered coverage summary metadata was accepted."
    }

    $validCoverageSummary.coverage.sha256 = Get-FileSha256 -Path $validCoveragePath
    Write-FixtureJson -Path (Join-Path $validCoverage.evidenceDirectory "trx-summary.json") -Value $validCoverageSummary

    $missingCoverage = New-Case -Fixture $fixture -Name "missing-coverage"
    Write-TrxFixture -Path (Join-Path $missingCoverage.trxDirectory $missingCoverage.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    Assert-NonZeroFixture -Name "missing OpenCover report" -Result (Invoke-AssertFixture -Fixture $fixture -Case $missingCoverage -CoveragePath (Join-Path (Split-Path -Parent $missingCoverage.trxDirectory) "coverage.opencover.xml"))

    $zeroByteCoverage = New-Case -Fixture $fixture -Name "zero-byte-coverage"
    Write-TrxFixture -Path (Join-Path $zeroByteCoverage.trxDirectory $zeroByteCoverage.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $zeroByteCoveragePath = Join-Path (Split-Path -Parent $zeroByteCoverage.trxDirectory) "coverage.opencover.xml"
    [System.IO.File]::WriteAllBytes($zeroByteCoveragePath, [byte[]]@())
    Assert-NonZeroFixture -Name "zero-byte OpenCover report" -Result (Invoke-AssertFixture -Fixture $fixture -Case $zeroByteCoverage -CoveragePath $zeroByteCoveragePath)

    $wrongNameCoverage = New-Case -Fixture $fixture -Name "wrong-name-coverage"
    Write-TrxFixture -Path (Join-Path $wrongNameCoverage.trxDirectory $wrongNameCoverage.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $wrongNameCoveragePath = Join-Path (Split-Path -Parent $wrongNameCoverage.trxDirectory) "coverage.xml"
    Write-OpenCoverFixture -Path $wrongNameCoveragePath -SourcePaths @((Join-Path $repository.path "fixture.txt"))
    Assert-NonZeroFixture -Name "wrong OpenCover report name" -Result (Invoke-AssertFixture -Fixture $fixture -Case $wrongNameCoverage -CoveragePath $wrongNameCoveragePath)

    $staleCoverage = New-Case -Fixture $fixture -Name "stale-coverage"
    Write-TrxFixture -Path (Join-Path $staleCoverage.trxDirectory $staleCoverage.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $staleCoveragePath = Join-Path (Split-Path -Parent $staleCoverage.trxDirectory) "coverage.opencover.xml"
    Write-OpenCoverFixture -Path $staleCoveragePath -SourcePaths @((Join-Path $repository.path "fixture.txt"))
    (Get-Item -LiteralPath $staleCoveragePath).LastWriteTimeUtc = $staleCoverage.notBeforeUtc.UtcDateTime.AddSeconds(-1)
    Assert-NonZeroFixture -Name "stale OpenCover report" -Result (Invoke-AssertFixture -Fixture $fixture -Case $staleCoverage -CoveragePath $staleCoveragePath)

    $malformedCoverage = New-Case -Fixture $fixture -Name "malformed-coverage"
    Write-TrxFixture -Path (Join-Path $malformedCoverage.trxDirectory $malformedCoverage.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $malformedCoveragePath = Join-Path (Split-Path -Parent $malformedCoverage.trxDirectory) "coverage.opencover.xml"
    [System.IO.File]::WriteAllText($malformedCoveragePath, "<CoverageSession", [System.Text.UTF8Encoding]::new($false))
    Assert-NonZeroFixture -Name "malformed OpenCover report" -Result (Invoke-AssertFixture -Fixture $fixture -Case $malformedCoverage -CoveragePath $malformedCoveragePath)

    $dtdCoverage = New-Case -Fixture $fixture -Name "dtd-coverage"
    Write-TrxFixture -Path (Join-Path $dtdCoverage.trxDirectory $dtdCoverage.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $dtdCoveragePath = Join-Path (Split-Path -Parent $dtdCoverage.trxDirectory) "coverage.opencover.xml"
    Write-OpenCoverFixture -Path $dtdCoveragePath -SourcePaths @((Join-Path $repository.path "fixture.txt")) -WithDtd
    Assert-NonZeroFixture -Name "DTD OpenCover report" -Result (Invoke-AssertFixture -Fixture $fixture -Case $dtdCoverage -CoveragePath $dtdCoveragePath)

    $wrongRootCoverage = New-Case -Fixture $fixture -Name "wrong-root-coverage"
    Write-TrxFixture -Path (Join-Path $wrongRootCoverage.trxDirectory $wrongRootCoverage.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $wrongRootCoveragePath = Join-Path (Split-Path -Parent $wrongRootCoverage.trxDirectory) "coverage.opencover.xml"
    Write-OpenCoverFixture -Path $wrongRootCoveragePath -SourcePaths @((Join-Path $repository.path "fixture.txt")) -RootName "NotCoverageSession"
    Assert-NonZeroFixture -Name "wrong OpenCover root" -Result (Invoke-AssertFixture -Fixture $fixture -Case $wrongRootCoverage -CoveragePath $wrongRootCoveragePath)

    $zeroModuleCoverage = New-Case -Fixture $fixture -Name "zero-module-coverage"
    Write-TrxFixture -Path (Join-Path $zeroModuleCoverage.trxDirectory $zeroModuleCoverage.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $zeroModuleCoveragePath = Join-Path (Split-Path -Parent $zeroModuleCoverage.trxDirectory) "coverage.opencover.xml"
    Write-OpenCoverFixture -Path $zeroModuleCoveragePath -ModuleCount 0
    Assert-NonZeroFixture -Name "zero-module OpenCover report" -Result (Invoke-AssertFixture -Fixture $fixture -Case $zeroModuleCoverage -CoveragePath $zeroModuleCoveragePath)

    $skippedModuleCoverage = New-Case -Fixture $fixture -Name "skipped-module-coverage"
    Write-TrxFixture -Path (Join-Path $skippedModuleCoverage.trxDirectory $skippedModuleCoverage.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $skippedModuleCoveragePath = Join-Path (Split-Path -Parent $skippedModuleCoverage.trxDirectory) "coverage.opencover.xml"
    Write-OpenCoverFixture -Path $skippedModuleCoveragePath -SourcePaths @((Join-Path $repository.path "fixture.txt")) -Skipped
    Assert-NonZeroFixture -Name "skipped-only OpenCover report" -Result (Invoke-AssertFixture -Fixture $fixture -Case $skippedModuleCoverage -CoveragePath $skippedModuleCoveragePath)

    $zeroFileCoverage = New-Case -Fixture $fixture -Name "zero-file-coverage"
    Write-TrxFixture -Path (Join-Path $zeroFileCoverage.trxDirectory $zeroFileCoverage.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $zeroFileCoveragePath = Join-Path (Split-Path -Parent $zeroFileCoverage.trxDirectory) "coverage.opencover.xml"
    Write-OpenCoverFixture -Path $zeroFileCoveragePath
    Assert-NonZeroFixture -Name "zero-file OpenCover report" -Result (Invoke-AssertFixture -Fixture $fixture -Case $zeroFileCoverage -CoveragePath $zeroFileCoveragePath)

    foreach ($uriScheme in @("http", "https", "git")) {
        $uriCoverage = New-Case -Fixture $fixture -Name "$uriScheme-uri-coverage"
        Write-TrxFixture -Path (Join-Path $uriCoverage.trxDirectory $uriCoverage.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
        $uriCoveragePath = Join-Path (Split-Path -Parent $uriCoverage.trxDirectory) "coverage.opencover.xml"
        Write-OpenCoverFixture -Path $uriCoveragePath -SourcePaths @("$uriScheme`://example.invalid/fixture.cs")
        Assert-NonZeroFixture -Name "$uriScheme URI OpenCover source" -Result (Invoke-AssertFixture -Fixture $fixture -Case $uriCoverage -CoveragePath $uriCoveragePath)
    }

    $outsideRootCoverage = New-Case -Fixture $fixture -Name "outside-root-coverage"
    Write-TrxFixture -Path (Join-Path $outsideRootCoverage.trxDirectory $outsideRootCoverage.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $outsideRootSourcePath = Join-Path $temporaryRoot "outside-root-source.cs"
    [System.IO.File]::WriteAllText($outsideRootSourcePath, "fixture`n", [System.Text.UTF8Encoding]::new($false))
    $outsideRootCoveragePath = Join-Path (Split-Path -Parent $outsideRootCoverage.trxDirectory) "coverage.opencover.xml"
    Write-OpenCoverFixture -Path $outsideRootCoveragePath -SourcePaths @($outsideRootSourcePath)
    Assert-NonZeroFixture -Name "outside-repository OpenCover source" -Result (Invoke-AssertFixture -Fixture $fixture -Case $outsideRootCoverage -CoveragePath $outsideRootCoveragePath)

    $junctionTarget = New-Item -ItemType Directory -Path (Join-Path $temporaryRoot "junction-target") -Force
    $junctionTargetSource = Join-Path $junctionTarget.FullName "outside.cs"
    [System.IO.File]::WriteAllText($junctionTargetSource, "fixture`n", [System.Text.UTF8Encoding]::new($false))
    $junctionPath = Join-Path $repository.path "outside-junction"
    $junctionFixtureOutcome = "skipped"
    $junctionOutput = @(& cmd /c "mklink /J `"$junctionPath`" `"$($junctionTarget.FullName)`"" 2>&1)
    if ($LASTEXITCODE -eq 0) {
        try {
            $junctionCoverage = New-Case -Fixture $fixture -Name "junction-root-escape-coverage"
            Write-TrxFixture -Path (Join-Path $junctionCoverage.trxDirectory $junctionCoverage.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
            $junctionCoveragePath = Join-Path (Split-Path -Parent $junctionCoverage.trxDirectory) "coverage.opencover.xml"
            Write-OpenCoverFixture -Path $junctionCoveragePath -SourcePaths @((Join-Path $junctionPath "outside.cs"))
            Assert-NonZeroFixture -Name "junction OpenCover source outside repository" -Result (Invoke-AssertFixture -Fixture $fixture -Case $junctionCoverage -CoveragePath $junctionCoveragePath)
            $junctionFixtureOutcome = "rejected"
        }
        finally {
            $null = & cmd /c "rmdir `"$junctionPath`""
        }
    }
    else {
        Write-Host "junction OpenCover fixture skipped because Windows could not create a junction."
    }

    $targeted = New-Case -Fixture $fixture -Name "targeted" -Tests @("Fixture.Target") -Filters @("FullyQualifiedName~Fixture.Target")
    Write-TrxFixture -Path (Join-Path $targeted.trxDirectory $targeted.expectedTrxFileName) -TestNames @("Fixture.Target") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $targetedResult = Invoke-AssertFixture -Fixture $fixture -Case $targeted -Supplementary -Command @("dotnet", "test", "fixture.csproj", "--filter", "FullyQualifiedName~Fixture.Target")
    Assert-ZeroFixture -Name "valid targeted supplementary suite" -Result $targetedResult
    $targetedJson = $targetedResult.output | ConvertFrom-Json -Depth 32
    if ($targetedJson.verificationScope -cne "Supplementary" -or $targetedJson.completeSuite) {
        throw "A filtered targeted fixture was allowed to claim full-suite success."
    }

    $missing = New-Case -Fixture $fixture -Name "missing"
    Assert-NonZeroFixture -Name "missing TRX" -Result (Invoke-AssertFixture -Fixture $fixture -Case $missing)

    $stale = New-Case -Fixture $fixture -Name "stale"
    $stalePath = Join-Path $stale.trxDirectory $stale.expectedTrxFileName
    Write-TrxFixture -Path $stalePath -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    (Get-Item -LiteralPath $stalePath).LastWriteTimeUtc = $stale.notBeforeUtc.UtcDateTime.AddSeconds(-1)
    Assert-NonZeroFixture -Name "stale TRX" -Result (Invoke-AssertFixture -Fixture $fixture -Case $stale)

    $zeroByte = New-Case -Fixture $fixture -Name "zero-byte"
    [System.IO.File]::WriteAllBytes((Join-Path $zeroByte.trxDirectory $zeroByte.expectedTrxFileName), [byte[]]@())
    Assert-NonZeroFixture -Name "zero-byte TRX" -Result (Invoke-AssertFixture -Fixture $fixture -Case $zeroByte)

    $malformedTrx = New-Case -Fixture $fixture -Name "malformed-trx"
    [System.IO.File]::WriteAllText((Join-Path $malformedTrx.trxDirectory $malformedTrx.expectedTrxFileName), "<TestRun", [System.Text.UTF8Encoding]::new($false))
    Assert-NonZeroFixture -Name "malformed TRX" -Result (Invoke-AssertFixture -Fixture $fixture -Case $malformedTrx)

    $malformedManifest = New-Case -Fixture $fixture -Name "malformed-manifest"
    Write-TrxFixture -Path (Join-Path $malformedManifest.trxDirectory $malformedManifest.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    [System.IO.File]::WriteAllText($malformedManifest.discoveryPath, "{", [System.Text.UTF8Encoding]::new($false))
    Assert-NonZeroFixture -Name "malformed discovery manifest" -Result (Invoke-AssertFixture -Fixture $fixture -Case $malformedManifest)

    $zeroTotal = New-Case -Fixture $fixture -Name "zero-total"
    Write-TrxFixture -Path (Join-Path $zeroTotal.trxDirectory $zeroTotal.expectedTrxFileName) -TestNames @() -Total 0 -Executed 0 -Passed 0 -Failed 0 -NotExecuted 0
    Assert-NonZeroFixture -Name "zero-total TRX" -Result (Invoke-AssertFixture -Fixture $fixture -Case $zeroTotal)

    $failed = New-Case -Fixture $fixture -Name "failed"
    Write-TrxFixture -Path (Join-Path $failed.trxDirectory $failed.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 0 -Failed 1 -NotExecuted 0 -Outcome "Failed"
    Assert-NonZeroFixture -Name "failed TRX" -Result (Invoke-AssertFixture -Fixture $fixture -Case $failed)

    $notExecuted = New-Case -Fixture $fixture -Name "not-executed"
    Write-TrxFixture -Path (Join-Path $notExecuted.trxDirectory $notExecuted.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 0 -Passed 0 -Failed 0 -NotExecuted 1 -Outcome "NotExecuted"
    Assert-NonZeroFixture -Name "not-executed TRX" -Result (Invoke-AssertFixture -Fixture $fixture -Case $notExecuted)

    $multiple = New-Case -Fixture $fixture -Name "multiple"
    Write-TrxFixture -Path (Join-Path $multiple.trxDirectory $multiple.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    Write-TrxFixture -Path (Join-Path $multiple.trxDirectory "other.trx") -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    Assert-NonZeroFixture -Name "multiple TRX files" -Result (Invoke-AssertFixture -Fixture $fixture -Case $multiple)

    $duplicateExecution = New-Case -Fixture $fixture -Name "duplicate-execution" -Tests @("Fixture.First", "Fixture.Second")
    $sharedExecutionId = [Guid]::NewGuid().ToString()
    Write-TrxFixture -Path (Join-Path $duplicateExecution.trxDirectory $duplicateExecution.expectedTrxFileName) -TestNames @("Fixture.First", "Fixture.Second") -Total 2 -Executed 2 -Passed 2 -Failed 0 -NotExecuted 0 -ExecutionIds @($sharedExecutionId, $sharedExecutionId)
    Assert-NonZeroFixture -Name "duplicate TRX execution identity" -Result (Invoke-AssertFixture -Fixture $fixture -Case $duplicateExecution)

    $wrongName = New-Case -Fixture $fixture -Name "wrong-name"
    Write-TrxFixture -Path (Join-Path $wrongName.trxDirectory "unexpected.trx") -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    Assert-NonZeroFixture -Name "wrong TRX file name" -Result (Invoke-AssertFixture -Fixture $fixture -Case $wrongName)

    $wrongNameSet = New-Case -Fixture $fixture -Name "wrong-name-set"
    Write-TrxFixture -Path (Join-Path $wrongNameSet.trxDirectory $wrongNameSet.expectedTrxFileName) -TestNames @("Fixture.Other") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    Assert-NonZeroFixture -Name "wrong test-name set" -Result (Invoke-AssertFixture -Fixture $fixture -Case $wrongNameSet)

    $wrongSha = New-Case -Fixture $fixture -Name "wrong-sha"
    Write-TrxFixture -Path (Join-Path $wrongSha.trxDirectory $wrongSha.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $wrongShaManifest = [System.IO.File]::ReadAllText($wrongSha.discoveryPath) | ConvertFrom-Json -Depth 32
    $wrongShaManifest.repository.head = "0000000000000000000000000000000000000000"
    Write-FixtureJson -Path $wrongSha.discoveryPath -Value $wrongShaManifest
    Assert-NonZeroFixture -Name "wrong manifest SHA" -Result (Invoke-AssertFixture -Fixture $fixture -Case $wrongSha)

    $nonzero = New-Case -Fixture $fixture -Name "nonzero-command"
    Write-TrxFixture -Path (Join-Path $nonzero.trxDirectory $nonzero.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    Assert-NonZeroFixture -Name "nonzero test command" -Result (Invoke-AssertFixture -Fixture $fixture -Case $nonzero -CommandExitCode 73)

    $undeclaredFilter = New-Case -Fixture $fixture -Name "undeclared-filter"
    Write-TrxFixture -Path (Join-Path $undeclaredFilter.trxDirectory $undeclaredFilter.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    Assert-NonZeroFixture -Name "undeclared full-suite filter" -Result (Invoke-AssertFixture -Fixture $fixture -Case $undeclaredFilter -Command @("dotnet", "test", "fixture.csproj", "--filter", "FullyQualifiedName~Fixture.Pass"))

    $dirty = New-Case -Fixture $fixture -Name "dirty-worktree"
    Write-TrxFixture -Path (Join-Path $dirty.trxDirectory $dirty.expectedTrxFileName) -TestNames @("Fixture.Pass") -Total 1 -Executed 1 -Passed 1 -Failed 0 -NotExecuted 0
    $dirtyPath = Join-Path $repository.path "dirty.txt"
    [System.IO.File]::WriteAllText($dirtyPath, "dirty`n", [System.Text.UTF8Encoding]::new($false))
    try {
        Assert-NonZeroFixture -Name "dirty worktree" -Result (Invoke-AssertFixture -Fixture $fixture -Case $dirty)
    }
    finally {
        Remove-Item -LiteralPath $dirtyPath -Force
    }

    Write-Host "assert-trx fixture matrix passed: full=1, colliding-display=1, targeted=1, coverage=1, missing-generated-release=2, multi-file=flat-4, junction=$junctionFixtureOutcome, failure=16, coverage-failure=19, localized-listing=1, configuration=1."
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
