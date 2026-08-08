param(
    [string]$RetainFixtureRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ExpectedSuites = @("Unit", "Architecture", "InMemoryIntegration", "PostgreSqlIntegration", "DataSeeder")
$script:FailureResults = [System.Collections.Generic.List[object]]::new()

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

    $checkout = New-Item -ItemType Directory -Path (Join-Path $Root "checkout") -Force
    $sourcePath = Join-Path $checkout.FullName "FixtureSource.cs"
    [System.IO.File]::WriteAllText($sourcePath, "namespace Fixture;`n", [System.Text.UTF8Encoding]::new($false))
    $fixtureProjectDirectory = New-Item -ItemType Directory -Path (Join-Path $checkout.FullName "Fixture") -Force
    [System.IO.File]::WriteAllText((Join-Path $fixtureProjectDirectory.FullName "Fixture.csproj"), "<Project Sdk=`"Microsoft.NET.Sdk`" />`n", [System.Text.UTF8Encoding]::new($false))
    $null = & git -C $checkout.FullName init --quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Could not initialize the temporary checkout repository."
    }

    $null = & git -C $checkout.FullName add FixtureSource.cs Fixture/Fixture.csproj
    if ($LASTEXITCODE -ne 0) {
        throw "Could not stage the temporary checkout source."
    }

    $null = & git -C $checkout.FullName -c user.name=fixture -c user.email=fixture@example.invalid commit --quiet -m fixture
    if ($LASTEXITCODE -ne 0) {
        throw "Could not commit the temporary checkout source."
    }

    $head = (& git -C $checkout.FullName rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -notmatch "^[0-9a-f]{40}$") {
        throw "The temporary checkout did not produce a full lowercase Git SHA."
    }

    return [pscustomobject]@{
        path = $checkout.FullName
        head = $head
        sourcePath = $sourcePath
    }
}

function Get-ArtifactPath {
    param(
        [Parameter(Mandatory)]
        [string]$DownloadRoot,
        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    $path = $DownloadRoot
    foreach ($segment in ($RelativePath -split "/")) {
        $path = Join-Path $path $segment
    }

    return $path
}

function Write-TrxFixture {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$Suite
    )

    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testName="$Suite.FixturePass" executionId="$([Guid]::NewGuid())" outcome="Passed" />
  </Results>
  <ResultSummary outcome="Completed">
    <Counters total="1" executed="1" passed="1" failed="0" notExecuted="0" />
  </ResultSummary>
</TestRun>
"@
    [System.IO.File]::WriteAllText($Path, $xml, [System.Text.UTF8Encoding]::new($false))
}

function Write-OpenCoverFixture {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string[]]$SourcePaths
    )

    $files = [System.Text.StringBuilder]::new()
    for ($index = 0; $index -lt $SourcePaths.Count; $index++) {
        $encodedSourcePath = [System.Security.SecurityElement]::Escape($SourcePaths[$index])
        [void]$files.AppendLine("        <File uid=`"$($index + 1)`" fullPath=`"$encodedSourcePath`" />")
    }
    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<CoverageSession>
  <Modules>
    <Module skipped="false">
      <Files>
$files
      </Files>
    </Module>
  </Modules>
</CoverageSession>
"@
    [System.IO.File]::WriteAllText($Path, $xml, [System.Text.UTF8Encoding]::new($false))
}

function Assert-ExactPropertyNames {
    param(
        [Parameter(Mandatory)]
        [object]$Object,
        [Parameter(Mandatory)]
        [string[]]$Expected,
        [Parameter(Mandatory)]
        [string]$Description
    )

    $actual = @($Object.PSObject.Properties | ForEach-Object { $_.Name })
    if (($actual -join "`n") -cne ($Expected -join "`n")) {
        throw "$Description does not have the exact producer property schema."
    }
}

function Assert-ProducerSchemaFixture {
    param(
        [Parameter(Mandatory)]
        [object]$Manifest
    )

    Assert-ExactPropertyNames -Object $Manifest -Expected @("schemaVersion", "kind", "repository", "runId", "runAttempt", "event", "ref", "mergeSha", "pullRequestHeadSha", "suites") -Description "Fixture manifest"
    if ($Manifest.runId -isnot [string] -or $Manifest.runAttempt -isnot [string]) {
        throw "Fixture manifest run ID and run attempt must serialize as canonical strings."
    }

    foreach ($suite in @($Manifest.suites)) {
        Assert-ExactPropertyNames -Object $suite -Expected @("suite", "trx", "coverage") -Description "Fixture suite '$($suite.suite)'"
        Assert-ExactPropertyNames -Object $suite.trx -Expected @("checkoutRelativePath", "sha256", "bytes") -Description "Fixture suite '$($suite.suite)' TRX"
        Assert-ExactPropertyNames -Object $suite.coverage -Expected @("checkoutRelativePath", "sha256", "bytes", "moduleCount", "fileCount", "localPathMode") -Description "Fixture suite '$($suite.suite)' OpenCover"
        if ($suite.trx.PSObject.Properties["path"] -or $suite.coverage.PSObject.Properties["path"] -or $suite.coverage.localPathMode -cne "repository-rooted") {
            throw "Fixture suite '$($suite.suite)' does not match the final Task 2 report schema."
        }
    }
}

function Write-ProducerCompatibilityReceipt {
    param(
        [Parameter(Mandatory)]
        [object]$Manifest,
        [Parameter(Mandatory)]
        [string]$Path
    )

    $repositoryRoot = Split-Path -Parent $PSScriptRoot
    $task2ReceiptPath = Join-Path $repositoryRoot ".omo/evidence/issue-440-sonar-artifact-handoff/task-2/happy-staged-tree.json"
    $producerScriptPath = Join-Path $PSScriptRoot "assert-ci-verification-evidence.ps1"
    $task2Receipt = [System.IO.File]::ReadAllText($task2ReceiptPath) | ConvertFrom-Json -Depth 16
    $producerScript = [System.IO.File]::ReadAllText($producerScriptPath)
    $expectedTree = [System.Collections.Generic.List[string]]::new()
    $expectedTree.Add("manifest.json")
    foreach ($suite in $ExpectedSuites) {
        $expectedTree.Add("TestResults/SonarInputs/$suite/$suite-$($task2Receipt.observed.manifest.mergeSha).trx")
        $expectedTree.Add("TestResults/SonarInputs/$suite/coverage.opencover.xml")
    }
    $expectedTreeValues = @($expectedTree | Sort-Object)
    $task2Tree = @($task2Receipt.observed.tree | Sort-Object)
    if (($expectedTreeValues -join "`n") -cne ($task2Tree -join "`n")) {
        throw "Task 2 published tree does not match the required 11-file Sonar handoff layout."
    }

    Assert-ProducerSchemaFixture -Manifest $Manifest
    if ($producerScript -notmatch 'checkoutRelativePath = "TestResults/SonarInputs/\$\(\$entry\.suite\)/\$trxName"' -or
        $producerScript -notmatch 'checkoutRelativePath = "TestResults/SonarInputs/\$\(\$entry\.suite\)/coverage\.opencover\.xml"' -or
        $producerScript -notmatch 'localPathMode = \$entry\.coverage\.localPathMode') {
        throw "Task 2 producer source does not expose the required report schema."
    }

    Write-FixtureJson -Path $Path -Value ([pscustomobject]@{
            task2Receipt = [pscustomobject]@{
                path = ".omo/evidence/issue-440-sonar-artifact-handoff/task-2/happy-staged-tree.json"
                fileCount = $task2Receipt.observed.fileCount
                artifactRootEntries = @($task2Receipt.observed.artifactRootEntries)
                tree = $task2Tree
            }
            expected = [pscustomobject]@{
                fileCount = 11
                artifactRootEntries = @("manifest.json", "TestResults")
                tree = $expectedTreeValues
                trxProperties = @("checkoutRelativePath", "sha256", "bytes")
                coverageProperties = @("checkoutRelativePath", "sha256", "bytes", "moduleCount", "fileCount", "localPathMode")
                coverageLocalPathMode = "repository-rooted"
            }
            fixture = [pscustomobject]@{
                runIdType = $Manifest.runId.GetType().Name
                runAttemptType = $Manifest.runAttempt.GetType().Name
                trxProperties = @($Manifest.suites[0].trx.PSObject.Properties | ForEach-Object { $_.Name })
                coverageProperties = @($Manifest.suites[0].coverage.PSObject.Properties | ForEach-Object { $_.Name })
                coverageLocalPathMode = $Manifest.suites[0].coverage.localPathMode
            }
            matches = [pscustomobject]@{
                task2Tree = $true
                producerReportFields = $true
                fixtureSchema = $true
            }
        })
}

function New-DownloadArtifact {
    param(
        [Parameter(Mandatory)]
        [string]$Root,
        [Parameter(Mandatory)]
        [object]$Fixture
    )

    $download = New-Item -ItemType Directory -Path $Root -Force
    $suiteEntries = [System.Collections.Generic.List[object]]::new()
    foreach ($suite in $ExpectedSuites) {
        $suiteRelativePath = "TestResults/SonarInputs/$suite"
        $suiteDirectory = New-Item -ItemType Directory -Path (Get-ArtifactPath -DownloadRoot $download.FullName -RelativePath $suiteRelativePath) -Force
        $trxRelativePath = "$suiteRelativePath/$suite-$($Fixture.mergeSha).trx"
        $coverageRelativePath = "$suiteRelativePath/coverage.opencover.xml"
        $trxPath = Get-ArtifactPath -DownloadRoot $download.FullName -RelativePath $trxRelativePath
        $coveragePath = Get-ArtifactPath -DownloadRoot $download.FullName -RelativePath $coverageRelativePath
        Write-TrxFixture -Path $trxPath -Suite $suite
        Write-OpenCoverFixture -Path $coveragePath -SourcePaths @($Fixture.sourcePath)
        $trx = Get-Item -LiteralPath $trxPath
        $coverage = Get-Item -LiteralPath $coveragePath
        $suiteEntries.Add([pscustomobject]@{
                suite = $suite
                trx = [pscustomobject]@{
                    checkoutRelativePath = $trxRelativePath
                    sha256 = (Get-FileHash -LiteralPath $trx.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                    bytes = [long]$trx.Length
                }
                coverage = [pscustomobject]@{
                    checkoutRelativePath = $coverageRelativePath
                    sha256 = (Get-FileHash -LiteralPath $coverage.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                    bytes = [long]$coverage.Length
                    moduleCount = 1
                    fileCount = 1
                    localPathMode = "repository-rooted"
                }
            })
    }

    Write-FixtureJson -Path (Join-Path $download.FullName "manifest.json") -Value ([pscustomobject]@{
            schemaVersion = 1
            kind = "sonar-inputs"
            repository = $Fixture.repository
            runId = $Fixture.runId
            runAttempt = $Fixture.runAttempt
            event = $Fixture.event
            ref = $Fixture.ref
            mergeSha = $Fixture.mergeSha
            pullRequestHeadSha = $Fixture.prHeadSha
            suites = $suiteEntries.ToArray()
        })

    return $download.FullName
}

function Copy-DownloadArtifact {
    param(
        [Parameter(Mandatory)]
        [string]$TemplateRoot,
        [Parameter(Mandatory)]
        [string]$CaseRoot
    )

    $destination = New-Item -ItemType Directory -Path $CaseRoot -Force
    foreach ($item in @(Get-ChildItem -LiteralPath $TemplateRoot -Force)) {
        Copy-Item -LiteralPath $item.FullName -Destination $destination.FullName -Recurse -Force
    }

    return $destination.FullName
}

function Get-Manifest {
    param([Parameter(Mandatory)][string]$DownloadRoot)

    return [System.IO.File]::ReadAllText((Join-Path $DownloadRoot "manifest.json")) | ConvertFrom-Json -Depth 16
}

function Save-Manifest {
    param(
        [Parameter(Mandatory)][string]$DownloadRoot,
        [Parameter(Mandatory)][object]$Manifest
    )

    Write-FixtureJson -Path (Join-Path $DownloadRoot "manifest.json") -Value $Manifest
}

function Update-ManifestReportMetadata {
    param(
        [Parameter(Mandatory)][string]$DownloadRoot,
        [Parameter(Mandatory)][string]$Suite,
        [Parameter(Mandatory)][ValidateSet("trx", "coverage")][string]$Report
    )

    $manifest = Get-Manifest -DownloadRoot $DownloadRoot
    $entry = @($manifest.suites | Where-Object { $_.suite -ceq $Suite })
    if ($entry.Count -ne 1) {
        throw "Fixture suite '$Suite' was not uniquely present in the manifest."
    }

    $path = Get-ArtifactPath -DownloadRoot $DownloadRoot -RelativePath $entry[0].$Report.checkoutRelativePath
    $file = Get-Item -LiteralPath $path
    $entry[0].$Report.sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $entry[0].$Report.bytes = [long]$file.Length
    Save-Manifest -DownloadRoot $DownloadRoot -Manifest $manifest
}

function Invoke-Consumer {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [Parameter(Mandatory)][string]$DownloadRoot
    )

    $arguments = @(
        "-NoProfile",
        "-File", $Fixture.assertScript,
        "-DownloadRoot", $DownloadRoot,
        "-CheckoutRoot", $Fixture.checkoutRoot,
        "-Repository", $Fixture.repository,
        "-RunId", $Fixture.runId,
        "-RunAttempt", $Fixture.runAttempt,
        "-Event", $Fixture.event,
        "-Ref", $Fixture.ref,
        "-MergeSha", $Fixture.mergeSha,
        "-PullRequestHeadSha", $Fixture.prHeadSha
    )

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

function Assert-ValidArtifact {
    param(
        [Parameter(Mandatory)][object]$Result
    )

    if ($Result.exitCode -ne 0) {
        throw "The valid five-suite artifact was rejected: $($Result.output)"
    }

    $records = @($Result.output -split "`r?`n" | Where-Object { $_ -match "^SONAR_INPUT " })
    if ($records.Count -ne $ExpectedSuites.Count) {
        throw "The valid five-suite artifact emitted $($records.Count) SONAR_INPUT records instead of $($ExpectedSuites.Count)."
    }

    for ($index = 0; $index -lt $ExpectedSuites.Count; $index++) {
        $suite = $ExpectedSuites[$index]
        if ($records[$index] -notmatch "^SONAR_INPUT suite=$suite coveragePath=.+ sha256=[0-9a-f]{64} bytes=[1-9][0-9]* modules=1 files=1$") {
            throw "SONAR_INPUT record $index was not deterministic or did not describe suite '$suite': $($records[$index])"
        }
    }

    return $records
}

function Assert-RejectedArtifact {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][object]$Result
    )

    if ($Result.exitCode -eq 0) {
        throw "Failure fixture '$Name' unexpectedly succeeded: $($Result.output)"
    }

    $script:FailureResults.Add([pscustomobject]@{
            name = $Name
            exitCode = $Result.exitCode
        })
}

function Invoke-FailureCase {
    param(
        [Parameter(Mandatory)][object]$Fixture,
        [Parameter(Mandatory)][string]$CasesRoot,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Mutate
    )

    $downloadRoot = Copy-DownloadArtifact -TemplateRoot $Fixture.templateArtifactRoot -CaseRoot (Join-Path $CasesRoot $Name)
    & $Mutate $downloadRoot
    Assert-RejectedArtifact -Name $Name -Result (Invoke-Consumer -Fixture $Fixture -DownloadRoot $downloadRoot)
}

$temporaryRoot = $null
$retain = -not [string]::IsNullOrWhiteSpace($RetainFixtureRoot)
try {
    if ($retain) {
        if (Test-Path -LiteralPath $RetainFixtureRoot) {
            throw "The retained fixture root already exists: $RetainFixtureRoot"
        }
        $temporaryRoot = (New-Item -ItemType Directory -Path $RetainFixtureRoot -Force).FullName
    }
    else {
        $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("lgym-sonar-inputs-" + [Guid]::NewGuid().ToString("N"))
        $null = New-Item -ItemType Directory -Path $temporaryRoot -Force
    }

    $checkout = Initialize-FixtureRepository -Root $temporaryRoot
    $fixture = [pscustomobject]@{
        assertScript = Join-Path $PSScriptRoot "assert-sonar-inputs.ps1"
        checkoutRoot = $checkout.path
        sourcePath = $checkout.sourcePath
        repository = "owner/repository"
        runId = "123456789"
        runAttempt = "2"
        event = "pull_request"
        ref = "refs/pull/440/merge"
        mergeSha = $checkout.head
        prHeadSha = "abcdefabcdefabcdefabcdefabcdefabcdefabcd"
    }
    $fixture | Add-Member -NotePropertyName templateArtifactRoot -NotePropertyValue (New-DownloadArtifact -Root (Join-Path $temporaryRoot "artifact-template") -Fixture $fixture)
    $casesRoot = (New-Item -ItemType Directory -Path (Join-Path $temporaryRoot "cases") -Force).FullName

    $validRoot = Copy-DownloadArtifact -TemplateRoot $fixture.templateArtifactRoot -CaseRoot (Join-Path $casesRoot "valid")
    $validManifest = Get-Manifest -DownloadRoot $validRoot
    Assert-ProducerSchemaFixture -Manifest $validManifest
    $happyRecords = Assert-ValidArtifact -Result (Invoke-Consumer -Fixture $fixture -DownloadRoot $validRoot)

    $missingGeneratedReleaseSourceRoot = Copy-DownloadArtifact -TemplateRoot $fixture.templateArtifactRoot -CaseRoot (Join-Path $casesRoot "missing-generated-release-source")
    $missingGeneratedReleaseSourcePath = Join-Path $fixture.checkoutRoot "Fixture\obj\Release\net10.0\Generator\Generated.g.cs"
    $missingGeneratedReleaseCoveragePath = Get-ArtifactPath -DownloadRoot $missingGeneratedReleaseSourceRoot -RelativePath "TestResults/SonarInputs/Unit/coverage.opencover.xml"
    Write-OpenCoverFixture -Path $missingGeneratedReleaseCoveragePath -SourcePaths @($fixture.sourcePath, $missingGeneratedReleaseSourcePath)
    Update-ManifestReportMetadata -DownloadRoot $missingGeneratedReleaseSourceRoot -Suite "Unit" -Report "coverage"
    $missingGeneratedReleaseManifest = Get-Manifest -DownloadRoot $missingGeneratedReleaseSourceRoot
    (@($missingGeneratedReleaseManifest.suites | Where-Object { $_.suite -ceq "Unit" }))[0].coverage.fileCount = 2
    Save-Manifest -DownloadRoot $missingGeneratedReleaseSourceRoot -Manifest $missingGeneratedReleaseManifest
    $missingGeneratedReleaseResult = Invoke-Consumer -Fixture $fixture -DownloadRoot $missingGeneratedReleaseSourceRoot
    $missingGeneratedReleaseRecords = @($missingGeneratedReleaseResult.output -split "`r?`n" | Where-Object { $_ -match "^SONAR_INPUT " })
    if ($missingGeneratedReleaseResult.exitCode -ne 0 -or
        $missingGeneratedReleaseRecords.Count -ne $ExpectedSuites.Count -or
        $missingGeneratedReleaseRecords[0] -notmatch "^SONAR_INPUT suite=Unit coveragePath=.+ sha256=[0-9a-f]{64} bytes=[1-9][0-9]* modules=1 files=2$") {
        throw "The five-suite artifact with a missing repository-contained generated Release source was not accepted with five deterministic SONAR_INPUT records."
    }

    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "missing-coverage-report" -Mutate {
        param($downloadRoot)
        Remove-Item -LiteralPath (Get-ArtifactPath -DownloadRoot $downloadRoot -RelativePath "TestResults/SonarInputs/Unit/coverage.opencover.xml") -Force
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "missing-trx-report" -Mutate {
        param($downloadRoot)
        Remove-Item -LiteralPath (Get-ArtifactPath -DownloadRoot $downloadRoot -RelativePath "TestResults/SonarInputs/Unit/Unit-$($fixture.mergeSha).trx") -Force
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "extra-report" -Mutate {
        param($downloadRoot)
        Write-TrxFixture -Path (Get-ArtifactPath -DownloadRoot $downloadRoot -RelativePath "TestResults/SonarInputs/Unit/unexpected.trx") -Suite "Unexpected"
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "path-traversal" -Mutate {
        param($downloadRoot)
        $manifest = Get-Manifest -DownloadRoot $downloadRoot
        (@($manifest.suites | Where-Object { $_.suite -ceq "Unit" }))[0].coverage.checkoutRelativePath = "../coverage.opencover.xml"
        Save-Manifest -DownloadRoot $downloadRoot -Manifest $manifest
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "empty-coverage-report" -Mutate {
        param($downloadRoot)
        $path = Get-ArtifactPath -DownloadRoot $downloadRoot -RelativePath "TestResults/SonarInputs/Unit/coverage.opencover.xml"
        [System.IO.File]::WriteAllBytes($path, [byte[]]@())
        Update-ManifestReportMetadata -DownloadRoot $downloadRoot -Suite "Unit" -Report "coverage"
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "malformed-trx-report" -Mutate {
        param($downloadRoot)
        $path = Get-ArtifactPath -DownloadRoot $downloadRoot -RelativePath "TestResults/SonarInputs/Unit/Unit-$($fixture.mergeSha).trx"
        [System.IO.File]::WriteAllText($path, "<TestRun", [System.Text.UTF8Encoding]::new($false))
        Update-ManifestReportMetadata -DownloadRoot $downloadRoot -Suite "Unit" -Report "trx"
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "http-sourcelink-uri" -Mutate {
        param($downloadRoot)
        $path = Get-ArtifactPath -DownloadRoot $downloadRoot -RelativePath "TestResults/SonarInputs/Unit/coverage.opencover.xml"
        Write-OpenCoverFixture -Path $path -SourcePaths @("https://example.invalid/FixtureSource.cs")
        Update-ManifestReportMetadata -DownloadRoot $downloadRoot -Suite "Unit" -Report "coverage"
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "missing-checkout-source" -Mutate {
        param($downloadRoot)
        $path = Get-ArtifactPath -DownloadRoot $downloadRoot -RelativePath "TestResults/SonarInputs/Unit/coverage.opencover.xml"
        Write-OpenCoverFixture -Path $path -SourcePaths @((Join-Path $fixture.checkoutRoot "missing\FixtureSource.cs"))
        Update-ManifestReportMetadata -DownloadRoot $downloadRoot -Suite "Unit" -Report "coverage"
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "outside-checkout-source" -Mutate {
        param($downloadRoot)
        $outsideDirectory = New-Item -ItemType Directory -Path (Join-Path $temporaryRoot "outside-checkout") -Force
        $outsidePath = Join-Path $outsideDirectory.FullName "Outside.cs"
        [System.IO.File]::WriteAllText($outsidePath, "namespace Outside;`n", [System.Text.UTF8Encoding]::new($false))
        $path = Get-ArtifactPath -DownloadRoot $downloadRoot -RelativePath "TestResults/SonarInputs/Unit/coverage.opencover.xml"
        Write-OpenCoverFixture -Path $path -SourcePaths @($outsidePath)
        Update-ManifestReportMetadata -DownloadRoot $downloadRoot -Suite "Unit" -Report "coverage"
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "stale-report" -Mutate {
        param($downloadRoot)
        $path = Get-ArtifactPath -DownloadRoot $downloadRoot -RelativePath "TestResults/SonarInputs/Unit/coverage.opencover.xml"
        (Get-Item -LiteralPath $path).LastWriteTimeUtc = [DateTime]::UtcNow.AddHours(-25)
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "coverage-hash-mismatch" -Mutate {
        param($downloadRoot)
        $manifest = Get-Manifest -DownloadRoot $downloadRoot
        (@($manifest.suites | Where-Object { $_.suite -ceq "Unit" }))[0].coverage.sha256 = "0" * 64
        Save-Manifest -DownloadRoot $downloadRoot -Manifest $manifest
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "coverage-size-mismatch" -Mutate {
        param($downloadRoot)
        $manifest = Get-Manifest -DownloadRoot $downloadRoot
        (@($manifest.suites | Where-Object { $_.suite -ceq "Unit" }))[0].coverage.bytes++
        Save-Manifest -DownloadRoot $downloadRoot -Manifest $manifest
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "trx-hash-mismatch" -Mutate {
        param($downloadRoot)
        $manifest = Get-Manifest -DownloadRoot $downloadRoot
        (@($manifest.suites | Where-Object { $_.suite -ceq "Unit" }))[0].trx.sha256 = "0" * 64
        Save-Manifest -DownloadRoot $downloadRoot -Manifest $manifest
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "trx-size-mismatch" -Mutate {
        param($downloadRoot)
        $manifest = Get-Manifest -DownloadRoot $downloadRoot
        (@($manifest.suites | Where-Object { $_.suite -ceq "Unit" }))[0].trx.bytes++
        Save-Manifest -DownloadRoot $downloadRoot -Manifest $manifest
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "duplicate-suite" -Mutate {
        param($downloadRoot)
        $manifest = Get-Manifest -DownloadRoot $downloadRoot
        $manifest.suites[1].suite = "Unit"
        Save-Manifest -DownloadRoot $downloadRoot -Manifest $manifest
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "unexpected-suite" -Mutate {
        param($downloadRoot)
        $manifest = Get-Manifest -DownloadRoot $downloadRoot
        $manifest.suites = @($manifest.suites) + @([pscustomobject]@{
                suite = "Unexpected"
                trx = [pscustomobject]@{ checkoutRelativePath = "TestResults/SonarInputs/Unexpected/Unexpected-$($fixture.mergeSha).trx"; sha256 = "0" * 64; bytes = 1 }
                coverage = [pscustomobject]@{ checkoutRelativePath = "TestResults/SonarInputs/Unexpected/coverage.opencover.xml"; sha256 = "0" * 64; bytes = 1; moduleCount = 1; fileCount = 1; localPathMode = "repository-rooted" }
            })
        Save-Manifest -DownloadRoot $downloadRoot -Manifest $manifest
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "legacy-report-path-schema" -Mutate {
        param($downloadRoot)
        $manifest = Get-Manifest -DownloadRoot $downloadRoot
        $report = (@($manifest.suites | Where-Object { $_.suite -ceq "Unit" }))[0].coverage
        $report.PSObject.Properties.Remove("checkoutRelativePath")
        $report | Add-Member -NotePropertyName "path" -NotePropertyValue "TestResults/SonarInputs/Unit/coverage.opencover.xml"
        Save-Manifest -DownloadRoot $downloadRoot -Manifest $manifest
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "missing-local-path-mode" -Mutate {
        param($downloadRoot)
        $manifest = Get-Manifest -DownloadRoot $downloadRoot
        (@($manifest.suites | Where-Object { $_.suite -ceq "Unit" }))[0].coverage.PSObject.Properties.Remove("localPathMode")
        Save-Manifest -DownloadRoot $downloadRoot -Manifest $manifest
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "non-repository-rooted-coverage" -Mutate {
        param($downloadRoot)
        $manifest = Get-Manifest -DownloadRoot $downloadRoot
        (@($manifest.suites | Where-Object { $_.suite -ceq "Unit" }))[0].coverage.localPathMode = "rooted"
        Save-Manifest -DownloadRoot $downloadRoot -Manifest $manifest
    }
    Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name "missing-non-generated-release-source" -Mutate {
        param($downloadRoot)
        $path = Get-ArtifactPath -DownloadRoot $downloadRoot -RelativePath "TestResults/SonarInputs/Unit/coverage.opencover.xml"
        Write-OpenCoverFixture -Path $path -SourcePaths @((Join-Path $fixture.checkoutRoot "Fixture\obj\Release\net10.0\Generator\Generated.cs"))
        Update-ManifestReportMetadata -DownloadRoot $downloadRoot -Suite "Unit" -Report "coverage"
    }

    foreach ($provenance in @(
            [pscustomobject]@{ name = "wrong-repository"; value = "other/repository" },
            [pscustomobject]@{ name = "wrong-run-id"; value = "999999999" },
            [pscustomobject]@{ name = "wrong-run-attempt"; value = "3" },
            [pscustomobject]@{ name = "wrong-event"; value = "push" },
            [pscustomobject]@{ name = "wrong-ref"; value = "refs/heads/main" },
            [pscustomobject]@{ name = "wrong-merge-sha"; value = "0000000000000000000000000000000000000000" },
            [pscustomobject]@{ name = "wrong-pr-head-sha"; value = "fedcbafedcbafedcbafedcbafedcbafedcbafedcba" })) {
        Invoke-FailureCase -Fixture $fixture -CasesRoot $casesRoot -Name $provenance.name -Mutate {
            param($downloadRoot)
            $manifest = Get-Manifest -DownloadRoot $downloadRoot
            switch ($provenance.name) {
                "wrong-repository" { $manifest.repository = $provenance.value }
                "wrong-run-id" { $manifest.runId = $provenance.value }
                "wrong-run-attempt" { $manifest.runAttempt = $provenance.value }
                "wrong-event" { $manifest.event = $provenance.value }
                "wrong-ref" { $manifest.ref = $provenance.value }
                "wrong-merge-sha" { $manifest.mergeSha = $provenance.value }
                "wrong-pr-head-sha" { $manifest.pullRequestHeadSha = $provenance.value }
                default { throw "Unknown provenance fixture '$($provenance.name)'." }
            }
            Save-Manifest -DownloadRoot $downloadRoot -Manifest $manifest
        }
    }

    $junctionRoot = Copy-DownloadArtifact -TemplateRoot $fixture.templateArtifactRoot -CaseRoot (Join-Path $casesRoot "artifact-junction")
    $junctionPath = Get-ArtifactPath -DownloadRoot $junctionRoot -RelativePath "TestResults/SonarInputs/Unit"
    $junctionTarget = New-Item -ItemType Directory -Path (Join-Path $temporaryRoot "outside-artifact-suite") -Force
    Copy-Item -LiteralPath (Get-ArtifactPath -DownloadRoot $fixture.templateArtifactRoot -RelativePath "TestResults/SonarInputs/Unit") -Destination $junctionTarget.FullName -Recurse -Force
    Remove-Item -LiteralPath $junctionPath -Recurse -Force
    $junctionOutput = @(& cmd /c "mklink /J `"$junctionPath`" `"$($junctionTarget.FullName)`"" 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create the required artifact junction fixture: $($junctionOutput -join [Environment]::NewLine)"
    }
    try {
        Assert-RejectedArtifact -Name "artifact-junction" -Result (Invoke-Consumer -Fixture $fixture -DownloadRoot $junctionRoot)
    }
    finally {
        $null = & cmd /c "rmdir `"$junctionPath`""
    }

    $headMismatchRoot = Copy-DownloadArtifact -TemplateRoot $fixture.templateArtifactRoot -CaseRoot (Join-Path $casesRoot "checkout-head-mismatch")
    [System.IO.File]::WriteAllText((Join-Path $fixture.checkoutRoot "later.txt"), "later`n", [System.Text.UTF8Encoding]::new($false))
    $null = & git -C $fixture.checkoutRoot add later.txt
    $null = & git -C $fixture.checkoutRoot -c user.name=fixture -c user.email=fixture@example.invalid commit --quiet -m later
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create the checkout HEAD mismatch fixture."
    }
    Assert-RejectedArtifact -Name "checkout-head-mismatch" -Result (Invoke-Consumer -Fixture $fixture -DownloadRoot $headMismatchRoot)

    if ($retain) {
        [System.IO.File]::WriteAllLines((Join-Path $temporaryRoot "happy-receipt.log"), [string[]]$happyRecords, [System.Text.UTF8Encoding]::new($false))
        Write-FixtureJson -Path (Join-Path $temporaryRoot "failure-receipt.json") -Value $script:FailureResults.ToArray()
        Write-ProducerCompatibilityReceipt -Manifest $validManifest -Path (Join-Path $temporaryRoot "producer-schema-compatibility.json")
    }

    Write-Host "sonar-inputs fixture matrix passed: valid=1, sonar-inputs=$($ExpectedSuites.Count), failures=$($script:FailureResults.Count), artifact-junction=1, checkout-head=1."
}
finally {
    if (-not $retain -and $null -ne $temporaryRoot -and (Test-Path -LiteralPath $temporaryRoot)) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
