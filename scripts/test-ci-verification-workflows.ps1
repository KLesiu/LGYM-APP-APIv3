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
        $isAlways = $condition.Trim() -ceq 'always()'
        if ([string]::IsNullOrWhiteSpace($condition) -or $isAlways -or (Test-GitHubExpression -Expression $condition -Context $Context)) {
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

function Assert-SonarQualityGateWaitContract {
    param([Parameter(Mandatory)][object]$Workflow)

    $expectedJobIds = @('sonar-required', 'sonar-push')
    Assert-StringSet -Expected $expectedJobIds -Actual @($Workflow.jobs.PSObject.Properties.Name) -Description 'Sonar workflow job identifiers'

    $waitPropertyPattern = '(?<!\S)/d:sonar\.qualitygate\.wait=true(?!\S)'
    $qualityGateTimeoutPattern = '(?<!\S)/d:sonar\.qualitygate\.timeout(?:=|\s|$)'
    $expectedBeginWaitCounts = @{
        'sonar-required' = 1
        'sonar-push' = 0
    }
    $totalBeginSteps = 0
    $totalEndSteps = 0
    $totalWaitProperties = 0
    foreach ($jobId in $expectedJobIds) {
        $job = $Workflow.jobs.$jobId
        Assert-True -Condition ([string]$job.name -ceq $jobId) -Message "Sonar job '$jobId' must retain its published job name."

        $runSteps = @($job.steps | Where-Object { $null -ne $_.PSObject.Properties['run'] })
        $beginSteps = @($runSteps | Where-Object { [string]$_.run -match '^\s*dotnet sonarscanner begin(?:\s|$)' })
        $endSteps = @($runSteps | Where-Object { [string]$_.run -match '^\s*dotnet sonarscanner end(?:\s|$)' })
        Assert-True -Condition ($beginSteps.Count -eq 1) -Message "Sonar job '$jobId' must contain exactly one Begin invocation."
        Assert-True -Condition ($endSteps.Count -eq 1) -Message "Sonar job '$jobId' must contain exactly one End invocation."

        $beginWaitProperties = @([regex]::Matches([string]$beginSteps[0].run, $waitPropertyPattern))
        $endWaitProperties = @([regex]::Matches([string]$endSteps[0].run, $waitPropertyPattern))
        $jobWaitProperties = @($runSteps | ForEach-Object { [regex]::Matches([string]$_.run, $waitPropertyPattern) })
        $expectedBeginWaitCount = $expectedBeginWaitCounts[$jobId]
        Assert-True -Condition ($beginWaitProperties.Count -eq $expectedBeginWaitCount) -Message "Sonar job '$jobId' Begin must contain exactly $expectedBeginWaitCount /d:sonar.qualitygate.wait=true properties."
        Assert-True -Condition ($endWaitProperties.Count -eq 0) -Message "Sonar job '$jobId' End must not contain /d:sonar.qualitygate.wait=true."
        Assert-True -Condition ($jobWaitProperties.Count -eq $expectedBeginWaitCount) -Message "Sonar job '$jobId' has an incorrect Quality Gate wait policy."
        Assert-True -Condition (@($runSteps | ForEach-Object { [regex]::Matches([string]$_.run, $qualityGateTimeoutPattern) }).Count -eq 0) -Message "Sonar job '$jobId' must retain the scanner's default Quality Gate timeout."

        $totalBeginSteps += $beginSteps.Count
        $totalEndSteps += $endSteps.Count
        $totalWaitProperties += $jobWaitProperties.Count
    }

    Assert-True -Condition ($totalBeginSteps -eq 2) -Message 'The Sonar workflow must contain exactly two Begin invocations.'
    Assert-True -Condition ($totalEndSteps -eq 2) -Message 'The Sonar workflow must contain exactly two End invocations.'
    Assert-True -Condition ($totalWaitProperties -eq 1) -Message 'The Sonar workflow must contain exactly one Quality Gate wait property.'
}

function Assert-SonarWaitFixtureRejected {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Description
    )

    $fixtureWorkflow = @(Get-YamlDocuments -Paths @($Path))[0]
    $rejection = $null
    try {
        Assert-SonarQualityGateWaitContract -Workflow $fixtureWorkflow
    }
    catch {
        $rejection = $_.Exception.Message
    }

    Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($rejection)) -Message "The Sonar workflow contract accepted the $Description fixture."
    Write-Output "Rejected Sonar $Description fixture: $rejection"
}

function Assert-SonarPostgreSqlCoverageContract {
    param(
        [Parameter(Mandatory)][object]$Workflow,
        [Parameter(Mandatory)][string]$RepositoryRoot
    )

    foreach ($jobId in @('sonar-required', 'sonar-push')) {
        $job = $Workflow.jobs.$jobId
        Assert-True -Condition ($null -ne $job.services.postgres) -Message "Sonar job '$jobId' must provision disposable PostgreSQL."
        Assert-True -Condition ([string]$job.env.LGYM_CI_POSTGRES_ADMIN -match '^Host=localhost;') -Message "Sonar job '$jobId' must provide the disposable PostgreSQL admin connection through its environment."

        $coverageDirectoryStep = @($job.steps | Where-Object { $_.name -ceq 'Create coverage directories' })
        Assert-True -Condition ($coverageDirectoryStep.Count -eq 1 -and [string]$coverageDirectoryStep[0].run -match 'TestResults/PostgreSqlIntegration') -Message "Sonar job '$jobId' must create a PostgreSQL coverage directory."

        $postgreSqlCoverageStep = @($job.steps | Where-Object { $_.name -ceq 'Run disposable PostgreSQL integration tests with coverage' })
        Assert-True -Condition ($postgreSqlCoverageStep.Count -eq 1) -Message "Sonar job '$jobId' must run the disposable PostgreSQL coverage suite exactly once."
        Assert-True -Condition ([string]$postgreSqlCoverageStep[0].shell -ceq 'pwsh') -Message "Sonar job '$jobId' must run the PostgreSQL coverage suite through PowerShell."

        $run = [string]$postgreSqlCoverageStep[0].run
        Assert-True -Condition ($run -match 'scripts/run-postgresql-integration-tests\.ps1') -Message "Sonar job '$jobId' must use the repository PostgreSQL runner."
        Assert-True -Condition ($run -match '-ConnectionString\s+\$env:LGYM_CI_POSTGRES_ADMIN') -Message "Sonar job '$jobId' must pass the ephemeral PostgreSQL connection through the environment."
        Assert-True -Condition ($run -match '-CoverageOutput') -Message "Sonar job '$jobId' must collect OpenCover output from the PostgreSQL suite."
        Assert-True -Condition ($run -match 'TestResults/PostgreSqlIntegration/coverage\.opencover\.xml') -Message "Sonar job '$jobId' must write PostgreSQL coverage to Sonar's report glob."
        Assert-True -Condition ($run -notmatch '(?i)--filter|-TestFilter') -Message "Sonar job '$jobId' must keep the PostgreSQL coverage suite unfiltered."
    }

    $runnerPath = Join-Path $RepositoryRoot 'scripts/run-postgresql-integration-tests.ps1'
    $runnerText = [System.IO.File]::ReadAllText($runnerPath)
    Assert-True -Condition ($runnerText -match '\[string\]\$CoverageOutput\s*=\s*""') -Message 'The PostgreSQL runner must accept a coverage output path.'
    Assert-True -Condition ($runnerText -match 'CoverletOutputFormat=opencover') -Message 'The PostgreSQL runner must emit OpenCover coverage.'
    Assert-True -Condition ($runnerText -match 'CoverletOutput=\$coverageOutput') -Message 'The PostgreSQL runner must direct Coverlet output to the requested path.'
}

function Assert-TestsCompatibilityContract {
    param([Parameter(Mandatory)][object]$Workflow)

    $jobProperties = @($Workflow.jobs.PSObject.Properties | Where-Object { $_.Name -ceq 'tests' })
    Assert-True -Condition ($jobProperties.Count -eq 1) -Message 'The PR workflow must contain exactly one tests compatibility job.'

    $job = $jobProperties[0].Value
    Assert-True -Condition ([string]$job.name -ceq 'tests') -Message "The tests compatibility job must publish the exact check name 'tests'."
    $dependencies = @($job.needs)
    Assert-True -Condition ($dependencies.Count -eq 1 -and [string]$dependencies[0] -ceq 'final-evidence-gate') -Message 'The tests compatibility job must depend only on final-evidence-gate.'
    Assert-True -Condition ([string]$job.if -ceq 'always()') -Message 'The tests compatibility job must evaluate the terminal gate result even after an upstream failure.'
    Assert-True -Condition ($null -eq $job.PSObject.Properties['continue-on-error']) -Message 'The tests compatibility job must not allow failures.'

    $steps = @($job.steps)
    Assert-True -Condition ($steps.Count -eq 1) -Message 'The tests compatibility job must contain exactly one aggregate assertion step.'
    $step = $steps[0]
    Assert-True -Condition ([string]$step.name -ceq 'Require successful final evidence gate') -Message 'The tests compatibility assertion step has an unexpected name.'
    Assert-True -Condition ([string]$step.shell -ceq 'pwsh') -Message 'The tests compatibility assertion step must use PowerShell.'

    $resultExpression = '${{ needs.final-evidence-gate.result }}'
    $runTemplate = [string]$step.run
    Assert-True -Condition (@([regex]::Matches($runTemplate, [regex]::Escape($resultExpression))).Count -eq 1) -Message 'The tests compatibility assertion must consume the final-evidence-gate result exactly once.'

    $resultCases = @(
        [pscustomobject]@{ result = 'success'; shouldSucceed = $true },
        [pscustomobject]@{ result = 'failure'; shouldSucceed = $false },
        [pscustomobject]@{ result = 'skipped'; shouldSucceed = $false },
        [pscustomobject]@{ result = 'cancelled'; shouldSucceed = $false }
    )
    foreach ($case in $resultCases) {
        $renderedScript = $runTemplate.Replace($resultExpression, $case.result)
        $failure = $null
        try {
            & ([scriptblock]::Create($renderedScript)) *> $null
        }
        catch {
            $failure = $_.Exception.Message
        }

        if ($case.shouldSucceed) {
            Assert-True -Condition ([string]::IsNullOrWhiteSpace($failure)) -Message "The tests compatibility assertion rejected terminal result '$($case.result)'."
        }
        else {
            Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($failure)) -Message "The tests compatibility assertion accepted terminal result '$($case.result)'."
        }
    }
}

function Assert-TestsCompatibilityFixtureRejected {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Description
    )

    $fixtureWorkflow = @(Get-YamlDocuments -Paths @($Path))[0]
    $rejection = $null
    try {
        Assert-TestsCompatibilityContract -Workflow $fixtureWorkflow
    }
    catch {
        $rejection = $_.Exception.Message
    }

    Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($rejection)) -Message "The PR workflow contract accepted the $Description tests compatibility fixture."
    Write-Output "Rejected tests compatibility $Description fixture: $rejection"
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
Assert-TestsCompatibilityContract -Workflow $prWorkflow
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
Assert-SonarQualityGateWaitContract -Workflow $sonarWorkflow
Assert-SonarPostgreSqlCoverageContract -Workflow $sonarWorkflow -RepositoryRoot $repositoryRoot

$requiredCiJobs = @('release-build', 'non-postgresql', 'postgresql', 'postgresql-cleanup', 'final-evidence-gate', 'tests')
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

    $compatibilityFixtureRoot = New-Item -ItemType Directory -Path (Join-Path $temporaryRoot 'pr-workflows') -Force
    $prWorkflowText = [System.IO.File]::ReadAllText($prWorkflowPath)
    $missingTestsPath = Join-Path $compatibilityFixtureRoot.FullName 'missing-tests.yml'
    $miswiredTestsPath = Join-Path $compatibilityFixtureRoot.FullName 'miswired-tests.yml'
    $permissiveTestsPath = Join-Path $compatibilityFixtureRoot.FullName 'permissive-tests.yml'
    $missingTestsText = [regex]::Replace($prWorkflowText, '(?ms)\r?\n  tests:\r?\n.*\z', '')
    $miswiredTestsText = [regex]::Replace($prWorkflowText, '(?m)^    needs: final-evidence-gate\s*$', '    needs: release-build', 1)
    $permissiveTestsText = $prWorkflowText.Replace(
        '            throw "final-evidence-gate completed with ''$finalResult''."',
        '            Write-Output "Ignored unsuccessful final-evidence-gate result ''$finalResult''."')
    Assert-True -Condition ($missingTestsText -cne $prWorkflowText) -Message 'The missing tests compatibility fixture could not remove the job.'
    Assert-True -Condition ($miswiredTestsText -cne $prWorkflowText) -Message 'The miswired tests compatibility fixture could not replace the dependency.'
    Assert-True -Condition ($permissiveTestsText -cne $prWorkflowText) -Message 'The permissive tests compatibility fixture could not remove fail-closed behavior.'
    [System.IO.File]::WriteAllText($missingTestsPath, $missingTestsText, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($miswiredTestsPath, $miswiredTestsText, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($permissiveTestsPath, $permissiveTestsText, [System.Text.UTF8Encoding]::new($false))
    Assert-TestsCompatibilityFixtureRejected -Path $missingTestsPath -Description 'missing'
    Assert-TestsCompatibilityFixtureRejected -Path $miswiredTestsPath -Description 'miswired'
    Assert-TestsCompatibilityFixtureRejected -Path $permissiveTestsPath -Description 'permissive'

    $sonarFixtureRoot = New-Item -ItemType Directory -Path (Join-Path $temporaryRoot 'sonar-workflows') -Force
    $sonarWorkflowText = [System.IO.File]::ReadAllText($sonarWorkflowPath)
    $waitProperty = '/d:sonar.qualitygate.wait=true'
    $missingWaitPath = Join-Path $sonarFixtureRoot.FullName 'missing-wait.yml'
    $duplicateWaitPath = Join-Path $sonarFixtureRoot.FullName 'duplicate-wait.yml'
    $misplacedWaitPath = Join-Path $sonarFixtureRoot.FullName 'misplaced-wait.yml'
    $pushWaitPath = Join-Path $sonarFixtureRoot.FullName 'push-wait.yml'
    [System.IO.File]::WriteAllText($missingWaitPath, [regex]::Replace($sonarWorkflowText, [regex]::Escape($waitProperty), '', 1), [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($duplicateWaitPath, [regex]::Replace($sonarWorkflowText, [regex]::Escape($waitProperty), "$waitProperty $waitProperty", 1), [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($misplacedWaitPath, [regex]::Replace($sonarWorkflowText, 'dotnet sonarscanner end', "dotnet sonarscanner end $waitProperty", 1), [System.Text.UTF8Encoding]::new($false))
    $pushWaitWorkflowText = [regex]::Replace($sonarWorkflowText, '(?s)(\n  sonar-push:.*?dotnet sonarscanner begin [^\r\n]*?)( /d:sonar\.exclusions=)', "`$1 $waitProperty`$2", 1)
    Assert-True -Condition ($pushWaitWorkflowText -cne $sonarWorkflowText) -Message 'The Sonar push wait fixture could not add a Begin wait property.'
    [System.IO.File]::WriteAllText($pushWaitPath, $pushWaitWorkflowText, [System.Text.UTF8Encoding]::new($false))
    Assert-SonarWaitFixtureRejected -Path $missingWaitPath -Description 'missing-wait'
    Assert-SonarWaitFixtureRejected -Path $duplicateWaitPath -Description 'duplicate-wait'
    Assert-SonarWaitFixtureRejected -Path $misplacedWaitPath -Description 'misplaced-wait'
    Assert-SonarWaitFixtureRejected -Path $pushWaitPath -Description 'push-wait'

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

    Write-Host 'CI workflow fixture matrix passed: yaml=2, events=4, compatibility-happy=1, compatibility-missing=1, compatibility-miswired=1, compatibility-permissive=1, sonar-wait-happy=1, sonar-wait-missing=1, sonar-wait-duplicate=1, sonar-wait-misplaced=1, sonar-wait-push=1, evidence-happy=1, missing-trx=1, missing-artifact=1, missing-discovery=1, malformed-trx=1, hash-tamper=1, counter-mismatch=1, name-mismatch=1.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
