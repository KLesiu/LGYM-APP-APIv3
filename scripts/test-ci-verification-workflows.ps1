Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'VerificationEvidence.psm1') -Force

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,
        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-YamlDocuments {
    param(
        [Parameter(Mandatory)]
        [string[]]$Paths
    )

    $python = Get-Command python -ErrorAction Stop
    $parser = @'
import json
import re
import sys
import yaml

class GitHubActionsLoader(yaml.SafeLoader):
    pass

GitHubActionsLoader.yaml_implicit_resolvers = {
    key: [entry for entry in entries if entry[0] != 'tag:yaml.org,2002:bool']
    for key, entries in yaml.SafeLoader.yaml_implicit_resolvers.items()
}
GitHubActionsLoader.add_implicit_resolver(
    'tag:yaml.org,2002:bool',
    re.compile(r'^(?:true|false)$', re.IGNORECASE),
    list('tTfF'))

documents = []
for path in sys.argv[1:]:
    with open(path, encoding='utf-8') as stream:
        documents.append(yaml.load(stream, Loader=GitHubActionsLoader))
print(json.dumps(documents))
'@

    $output = & $python.Source -c $parser @Paths
    if ($LASTEXITCODE -ne 0) {
        throw 'The local YAML parser rejected a workflow document.'
    }

    return @($output | ConvertFrom-Json -Depth 64)
}

function Get-GitHubExpressionTokens {
    param([Parameter(Mandatory)][string]$Expression)

    $tokens = [System.Collections.Generic.List[object]]::new()
    $index = 0
    while ($index -lt $Expression.Length) {
        $character = $Expression[$index]
        if ([char]::IsWhiteSpace($character)) {
            $index++
            continue
        }

        if ($index + 1 -lt $Expression.Length) {
            $pair = $Expression.Substring($index, 2)
            if ($pair -in @('&&', '||', '==', '!=')) {
                $tokens.Add([pscustomobject]@{ kind = $pair; value = $pair })
                $index += 2
                continue
            }
        }

        if ($character -in @('(', ')')) {
            $tokens.Add([pscustomobject]@{ kind = [string]$character; value = [string]$character })
            $index++
            continue
        }

        if ($character -eq "'") {
            $end = $Expression.IndexOf("'", $index + 1)
            if ($end -lt 0) {
                throw 'GitHub expression contains an unterminated string literal.'
            }

            $tokens.Add([pscustomobject]@{ kind = 'string'; value = $Expression.Substring($index + 1, $end - $index - 1) })
            $index = $end + 1
            continue
        }

        $match = [regex]::Match($Expression.Substring($index), '^[A-Za-z_][A-Za-z0-9_.-]*')
        if (-not $match.Success) {
            throw "GitHub expression contains unsupported syntax at offset $index."
        }

        $tokens.Add([pscustomobject]@{ kind = 'identifier'; value = $match.Value })
        $index += $match.Length
    }

    $tokens.Add([pscustomobject]@{ kind = 'end'; value = '' })
    return @($tokens)
}

function Resolve-GitHubExpressionValue {
    param(
        [Parameter(Mandatory)][string]$Identifier,
        [Parameter(Mandatory)][hashtable]$Context
    )

    if ($Identifier -ceq 'true') {
        return $true
    }

    if ($Identifier -ceq 'false') {
        return $false
    }

    $value = $Context
    foreach ($segment in $Identifier.Split('.')) {
        if ($value -is [System.Collections.IDictionary]) {
            if (-not $value.Contains($segment)) {
                throw "GitHub expression references unavailable context '$Identifier'."
            }
            $value = $value[$segment]
            continue
        }

        $property = @($value.PSObject.Properties | Where-Object { $_.Name -ceq $segment })
        if ($property.Count -ne 1) {
            throw "GitHub expression references unavailable context '$Identifier'."
        }
        $value = $property[0].Value
    }

    return $value
}

function Test-GitHubExpression {
    param(
        [Parameter(Mandatory)][string]$Expression,
        [Parameter(Mandatory)][hashtable]$Context
    )

    $tokens = Get-GitHubExpressionTokens -Expression $Expression.Trim()
    $state = [pscustomobject]@{ index = 0 }

    function Get-CurrentToken { return $tokens[$state.index] }
    function Take-Token {
        $token = Get-CurrentToken
        $state.index++
        return $token
    }
    function Parse-Value {
        $token = Take-Token
        if ($token.kind -eq 'string') { return $token.value }
        if ($token.kind -eq 'identifier') { return Resolve-GitHubExpressionValue -Identifier $token.value -Context $Context }
        throw "GitHub expression expected a value but found '$($token.value)'."
    }
    function Parse-Primary {
        if ((Get-CurrentToken).kind -eq '(') {
            $null = Take-Token
            $nested = Parse-Or
            if ((Take-Token).kind -ne ')') {
                throw 'GitHub expression has an unclosed parenthesis.'
            }
            return $nested
        }

        $left = Parse-Value
        $operator = Take-Token
        if ($operator.kind -notin @('==', '!=')) {
            throw "GitHub expression expected a comparison operator but found '$($operator.value)'."
        }
        $right = Parse-Value
        if ($operator.kind -eq '==') { return $left -ceq $right }
        return $left -cne $right
    }
    function Parse-And {
        $value = Parse-Primary
        while ((Get-CurrentToken).kind -eq '&&') {
            $null = Take-Token
            $right = Parse-Primary
            $value = $value -and $right
        }
        return $value
    }
    function Parse-Or {
        $value = Parse-And
        while ((Get-CurrentToken).kind -eq '||') {
            $null = Take-Token
            $right = Parse-And
            $value = $value -or $right
        }
        return $value
    }

    $result = Parse-Or
    if ((Get-CurrentToken).kind -ne 'end') {
        throw "GitHub expression contains trailing token '$((Get-CurrentToken).value)'."
    }
    return [bool]$result
}

function Get-SelectedJobs {
    param(
        [Parameter(Mandatory)][object]$Workflow,
        [Parameter(Mandatory)][hashtable]$Context
    )

    $selected = [System.Collections.Generic.List[string]]::new()
    foreach ($property in $Workflow.jobs.PSObject.Properties) {
        $condition = [string]$property.Value.if
        if ([string]::IsNullOrWhiteSpace($condition) -or (Test-GitHubExpression -Expression $condition -Context $Context)) {
            $selected.Add($property.Name)
        }
    }
    return @($selected | Sort-Object)
}

function Get-WorkflowActionSteps {
    param([Parameter(Mandatory)][object]$Workflow)

    foreach ($job in $Workflow.jobs.PSObject.Properties) {
        foreach ($step in @($job.Value.steps)) {
            if ($null -ne $step.PSObject.Properties['uses']) {
                $step
            }
        }
    }
}

function Assert-StringSet {
    param(
        [Parameter(Mandatory)][string[]]$Expected,
        [Parameter(Mandatory)][string[]]$Actual,
        [Parameter(Mandatory)][string]$Description
    )

    if ((($Expected | Sort-Object) -join "`n") -cne (($Actual | Sort-Object) -join "`n")) {
        throw "$Description did not match. Expected '$($Expected -join ', ')'; actual '$($Actual -join ', ')'."
    }
}

function New-EventContext {
    param(
        [Parameter(Mandatory)][string]$EventName,
        [Parameter(Mandatory)][AllowEmptyString()][string]$BaseRef,
        [Parameter(Mandatory)][string]$Ref
    )

    return @{
        github = @{
            event_name = $EventName
            base_ref = $BaseRef
            ref = $Ref
            event = @{ pull_request = @{ draft = $false; head = @{ repo = @{ fork = $false } } } }
        }
        needs = @{
            'release-build' = @{ result = 'success' }
            'non-postgresql' = @{ result = 'success' }
            postgresql = @{ result = 'success' }
            'postgresql-cleanup' = @{ result = 'success' }
        }
    }
}

function Write-JsonFixture {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][object]$Value)
    [System.IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 16) + [Environment]::NewLine), [System.Text.UTF8Encoding]::new($false))
}

function Write-TrxFixture {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$TestName
    )

    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testName="$TestName" executionId="$([Guid]::NewGuid())" outcome="Passed" />
  </Results>
  <ResultSummary outcome="Completed">
    <Counters total="1" executed="1" passed="1" failed="0" notExecuted="0" />
  </ResultSummary>
</TestRun>
"@
    [System.IO.File]::WriteAllText($Path, $xml, [System.Text.UTF8Encoding]::new($false))
}

function Update-FixtureTrxMetadata {
    param(
        [Parameter(Mandatory)][string]$SummaryPath,
        [Parameter(Mandatory)][string]$TrxPath
    )

    $summary = [System.IO.File]::ReadAllText($SummaryPath) | ConvertFrom-Json -Depth 32
    $trx = Get-Item -LiteralPath $TrxPath
    $summary.trx.sha256 = (Get-FileHash -LiteralPath $trx.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $summary.trx.bytes = $trx.Length
    Write-JsonFixture -Path $SummaryPath -Value $summary
}

function New-EvidenceFixture {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Head)

    $cleanRepository = [pscustomobject]@{
        head = $Head
        worktree = [pscustomobject]@{ isClean = $true }
    }
    $buildPath = Join-Path $Root 'release-build.json'
    Write-JsonFixture -Path $buildPath -Value ([pscustomobject]@{
            kind = 'release-build'
            repository = $cleanRepository
            configuration = 'Release'
            exitCode = 0
        })
    Write-JsonFixture -Path (Join-Path $Root 'cleanup-probe.json') -Value ([pscustomobject]@{
            kind = 'postgresql-cleanup'
            outcome = 'Passed'
            head = $Head
        })

    $buildSha256 = (Get-FileHash -LiteralPath $buildPath -Algorithm SHA256).Hash.ToLowerInvariant()
    foreach ($suite in @('Unit', 'Architecture', 'InMemoryIntegration', 'PostgreSqlIntegration', 'DataSeeder')) {
        $directory = New-Item -ItemType Directory -Path (Join-Path $Root $suite) -Force
        $trxName = "$suite-fixture.trx"
        $testName = "$suite.FixturePass"
        $discoveryPath = Join-Path $Root "$suite-discovery.json"
        Write-JsonFixture -Path $discoveryPath -Value ([pscustomobject]@{
                kind = 'test-discovery'
                suite = $suite
                repository = $cleanRepository
                buildHead = $Head
                identityScheme = 'vstest-display-name-multiset-utf8-v1'
                tests = @($testName)
                testCount = 1
                testListSha256 = Get-StringListSha256 -Values @($testName)
            })
        $discoverySha256 = (Get-FileHash -LiteralPath $discoveryPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $trxPath = Join-Path $directory.FullName $trxName
        Write-TrxFixture -Path $trxPath -TestName $testName
        $trxFile = Get-Item -LiteralPath $trxPath
        $summaryPath = Join-Path $directory.FullName 'trx-summary.json'
        Write-JsonFixture -Path $summaryPath -Value ([pscustomobject]@{
                kind = 'trx-summary'
                suite = $suite
                completeSuite = $true
                repository = $cleanRepository
                command = [pscustomobject]@{ exitCode = 0 }
                buildManifest = [pscustomobject]@{ sha256 = $buildSha256 }
                discoveryManifest = [pscustomobject]@{
                    sha256 = $discoverySha256
                    identityScheme = 'vstest-display-name-multiset-utf8-v1'
                    testListSha256 = Get-StringListSha256 -Values @($testName)
                }
                testIdentity = [pscustomobject]@{ discovery = 'vstest display-name multiset'; execution = 'trx executionId' }
                trx = [pscustomobject]@{
                    fileName = $trxName
                    sha256 = (Get-FileHash -LiteralPath $trxFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                    bytes = $trxFile.Length
                    testCount = 1
                    testNames = @($testName)
                    counters = [pscustomobject]@{ total = 1; executed = 1; passed = 1; failed = 0; notExecuted = 0 }
                }
            })
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$prWorkflowPath = Join-Path $repositoryRoot '.github/workflows/pr-and-main-tests.yml'
$sonarWorkflowPath = Join-Path $repositoryRoot '.github/workflows/sonarcloud-analysis.yml'
$evidenceGatePath = Join-Path $PSScriptRoot 'assert-ci-verification-evidence.ps1'
$documents = Get-YamlDocuments -Paths @($prWorkflowPath, $sonarWorkflowPath)
$prWorkflow = $documents[0]
$sonarWorkflow = $documents[1]

$expectedBranches = @('automation/modular-monolith-milestone', 'main')
Assert-StringSet -Expected $expectedBranches -Actual @($prWorkflow.on.pull_request.branches) -Description 'PR workflow pull-request branches'
Assert-StringSet -Expected $expectedBranches -Actual @($prWorkflow.on.push.branches) -Description 'PR workflow push branches'
Assert-StringSet -Expected $expectedBranches -Actual @($sonarWorkflow.on.pull_request.branches) -Description 'Sonar pull-request branches'
Assert-StringSet -Expected $expectedBranches -Actual @($sonarWorkflow.on.push.branches) -Description 'Sonar push branches'

$matrix = @($prWorkflow.jobs.'non-postgresql'.strategy.matrix.include)
Assert-True -Condition ($matrix.Count -eq 4) -Message 'The non-PostgreSQL matrix must contain exactly four suites.'
$expectedMatrix = @{
    Unit = ''
    Architecture = ''
    InMemoryIntegration = 'TestCategory!=PostgreSql'
    DataSeeder = ''
}
foreach ($entry in $matrix) {
    $suite = [string]$entry.suite
    Assert-True -Condition $expectedMatrix.ContainsKey($suite) -Message "The matrix contains unexpected suite '$suite'."
    Assert-True -Condition ([string]$entry.filter -ceq $expectedMatrix[$suite]) -Message "Suite '$suite' has an incorrect filter."
}
Assert-True -Condition (($prWorkflow.jobs.'non-postgresql'.steps | Where-Object { $_.name -ceq 'Run suite with same-SHA TRX evidence' }).run -match 'scripts/assert-trx\.ps1') -Message 'The non-PostgreSQL matrix does not assert TRX evidence.'
Assert-True -Condition (($prWorkflow.jobs.postgresql.steps | Where-Object { $_.name -ceq 'Run unfiltered PostgreSQL suite with same-SHA TRX evidence' }).run -notmatch '(?i)--filter|-filter') -Message 'The PostgreSQL runner must remain unfiltered.'
Assert-True -Condition (($prWorkflow.jobs.postgresql.steps | Where-Object { $_.name -ceq 'Run unfiltered PostgreSQL suite with same-SHA TRX evidence' }).run -match 'scripts/assert-trx\.ps1') -Message 'The PostgreSQL suite does not assert TRX evidence.'
Assert-True -Condition (($prWorkflow.jobs.'final-evidence-gate'.steps | Where-Object { $_.name -ceq 'Validate complete same-SHA evidence before publication' }).run -match 'assert-ci-verification-evidence\.ps1') -Message 'The final evidence gate does not execute the artifact validator.'
$actionSteps = @(Get-WorkflowActionSteps -Workflow $prWorkflow)
$uploads = @($actionSteps | Where-Object { $_.uses -ceq 'actions/upload-artifact@v4' })
Assert-True -Condition ($uploads.Count -eq 5) -Message 'Expected five fatal evidence artifact uploads.'
foreach ($upload in $uploads) {
    Assert-True -Condition ($upload.with.name -match [regex]::Escape('${{ github.sha }}')) -Message 'An evidence artifact name is not keyed by github.sha.'
    Assert-True -Condition ($upload.with.'if-no-files-found' -ceq 'error') -Message 'An evidence artifact upload does not fail when files are absent.'
}
Assert-True -Condition (@($actionSteps | Where-Object { $_.uses -match 'test-reporter|dorny/' }).Count -eq 0) -Message 'A summary publisher can run before final evidence validation.'
Assert-True -Condition ($null -eq $sonarWorkflow.jobs.'sonar-required'.PSObject.Properties['continue-on-error']) -Message 'Required Sonar analysis must not be optional.'
foreach ($sonarJob in @($sonarWorkflow.jobs.'sonar-required', $sonarWorkflow.jobs.'sonar-push')) {
    $integrationStep = @($sonarJob.steps | Where-Object { $_.name -ceq 'Run declared InMemory integration tests with coverage' })
    Assert-True -Condition ($integrationStep.Count -eq 1 -and $integrationStep[0].run -match '--filter "TestCategory!=PostgreSql"') -Message 'Sonar does not use the declared InMemory integration filter.'
}

$requiredCiJobs = @('release-build', 'non-postgresql', 'postgresql', 'postgresql-cleanup', 'final-evidence-gate')
$cases = @(
    [pscustomobject]@{ name = 'PR to automation'; context = New-EventContext -EventName 'pull_request' -BaseRef 'automation/modular-monolith-milestone' -Ref 'refs/pull/1/merge'; sonar = 'sonar-required' },
    [pscustomobject]@{ name = 'automation push'; context = New-EventContext -EventName 'push' -BaseRef '' -Ref 'refs/heads/automation/modular-monolith-milestone'; sonar = 'sonar-push' },
    [pscustomobject]@{ name = 'PR to main'; context = New-EventContext -EventName 'pull_request' -BaseRef 'main' -Ref 'refs/pull/2/merge'; sonar = 'sonar-required' },
    [pscustomobject]@{ name = 'main push'; context = New-EventContext -EventName 'push' -BaseRef '' -Ref 'refs/heads/main'; sonar = 'sonar-push' }
)
foreach ($case in $cases) {
    Assert-StringSet -Expected $requiredCiJobs -Actual (Get-SelectedJobs -Workflow $prWorkflow -Context $case.context) -Description "$($case.name) CI job selection"
    Assert-StringSet -Expected @($case.sonar) -Actual (Get-SelectedJobs -Workflow $sonarWorkflow -Context $case.context) -Description "$($case.name) Sonar job selection"
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("lgym-ci-workflow-fixtures-" + [Guid]::NewGuid().ToString('N'))
try {
    $null = New-Item -ItemType Directory -Path $temporaryRoot -Force
    $fixtureHead = '0123456789abcdef0123456789abcdef01234567'
    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead

    & pwsh -NoProfile -File $evidenceGatePath -EvidenceRoot $temporaryRoot -ExpectedHead $fixtureHead
    Assert-True -Condition ($LASTEXITCODE -eq 0) -Message 'The complete same-SHA artifact fixture failed.'

    Remove-Item -LiteralPath (Join-Path $temporaryRoot 'Unit/Unit-fixture.trx') -Force
    & pwsh -NoProfile -File $evidenceGatePath -EvidenceRoot $temporaryRoot -ExpectedHead $fixtureHead 2>$null
    Assert-True -Condition ($LASTEXITCODE -ne 0) -Message 'The final evidence gate accepted a missing TRX artifact.'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead
    Remove-Item -LiteralPath (Join-Path $temporaryRoot 'DataSeeder/trx-summary.json') -Force
    & pwsh -NoProfile -File $evidenceGatePath -EvidenceRoot $temporaryRoot -ExpectedHead $fixtureHead 2>$null
    Assert-True -Condition ($LASTEXITCODE -ne 0) -Message 'The final evidence gate accepted a missing suite artifact.'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead
    Remove-Item -LiteralPath (Join-Path $temporaryRoot 'Unit-discovery.json') -Force
    & pwsh -NoProfile -File $evidenceGatePath -EvidenceRoot $temporaryRoot -ExpectedHead $fixtureHead 2>$null
    Assert-True -Condition ($LASTEXITCODE -ne 0) -Message 'The final evidence gate accepted a missing referenced discovery artifact.'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead
    $unitTrxPath = Join-Path $temporaryRoot 'Unit/Unit-fixture.trx'
    [System.IO.File]::WriteAllText($unitTrxPath, '<TestRun', [System.Text.UTF8Encoding]::new($false))
    Update-FixtureTrxMetadata -SummaryPath (Join-Path $temporaryRoot 'Unit/trx-summary.json') -TrxPath $unitTrxPath
    & pwsh -NoProfile -File $evidenceGatePath -EvidenceRoot $temporaryRoot -ExpectedHead $fixtureHead 2>$null
    Assert-True -Condition ($LASTEXITCODE -ne 0) -Message 'The final evidence gate accepted malformed TRX XML.'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead
    [System.IO.File]::AppendAllText((Join-Path $temporaryRoot 'Unit/Unit-fixture.trx'), "`n<!-- hash tamper -->", [System.Text.UTF8Encoding]::new($false))
    & pwsh -NoProfile -File $evidenceGatePath -EvidenceRoot $temporaryRoot -ExpectedHead $fixtureHead 2>$null
    Assert-True -Condition ($LASTEXITCODE -ne 0) -Message 'The final evidence gate accepted a hash-tampered TRX artifact.'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead
    $counterSummaryPath = Join-Path $temporaryRoot 'Unit/trx-summary.json'
    $counterSummary = [System.IO.File]::ReadAllText($counterSummaryPath) | ConvertFrom-Json -Depth 32
    $counterSummary.trx.counters.passed = 0
    Write-JsonFixture -Path $counterSummaryPath -Value $counterSummary
    & pwsh -NoProfile -File $evidenceGatePath -EvidenceRoot $temporaryRoot -ExpectedHead $fixtureHead 2>$null
    Assert-True -Condition ($LASTEXITCODE -ne 0) -Message 'The final evidence gate accepted inconsistent TRX counters.'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead
    $nameSummaryPath = Join-Path $temporaryRoot 'Unit/trx-summary.json'
    $nameSummary = [System.IO.File]::ReadAllText($nameSummaryPath) | ConvertFrom-Json -Depth 32
    $nameSummary.trx.testNames = @('Unit.ForgedDisplayName')
    Write-JsonFixture -Path $nameSummaryPath -Value $nameSummary
    & pwsh -NoProfile -File $evidenceGatePath -EvidenceRoot $temporaryRoot -ExpectedHead $fixtureHead 2>$null
    Assert-True -Condition ($LASTEXITCODE -ne 0) -Message 'The final evidence gate accepted a mismatched TRX display-name multiset.'

    Write-Host 'CI workflow fixture matrix passed: yaml=2, events=4, evidence-happy=1, missing-trx=1, missing-artifact=1, missing-discovery=1, malformed-trx=1, hash-tamper=1, counter-mismatch=1, name-mismatch=1.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
