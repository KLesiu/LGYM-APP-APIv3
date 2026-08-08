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

function Assert-UnifiedSonarContract {
    param([Parameter(Mandatory)][object]$Workflow)

    $expectedJobIds = @('final-evidence-gate', 'non-postgresql', 'postgresql', 'postgresql-cleanup', 'release-build', 'sonar-push', 'sonar-required', 'tests')
    Assert-StringSet -Expected $expectedJobIds -Actual @($Workflow.jobs.PSObject.Properties.Name) -Description 'unified PR workflow job identifiers'

    Assert-StringSet -Expected @('automation/modular-monolith-milestone', 'main') -Actual @($Workflow.on.pull_request.branches) -Description 'unified Sonar pull-request branches'
    Assert-StringSet -Expected @('opened', 'ready_for_review', 'reopened', 'synchronize') -Actual @($Workflow.on.pull_request.types) -Description 'unified Sonar pull-request activity types'
    Assert-StringSet -Expected @('automation/modular-monolith-milestone', 'main') -Actual @($Workflow.on.push.branches) -Description 'unified Sonar push branches'

    foreach ($environmentName in @('SONAR_HOST_URL', 'SONAR_ORG', 'SONAR_PROJECT_KEY', 'SONAR_COVERAGE_EXCLUSIONS')) {
        Assert-True -Condition ($null -ne $Workflow.env.PSObject.Properties[$environmentName]) -Message "The unified workflow must retain '$environmentName' for its Sonar jobs."
    }
    Assert-True -Condition ([string]$Workflow.env.SONAR_HOST_URL -ceq 'https://sonarcloud.io') -Message 'The unified workflow must retain the SonarCloud host URL.'
    Assert-True -Condition ([string]$Workflow.env.SONAR_ORG -ceq '${{ vars.SONAR_ORG != '''' && vars.SONAR_ORG || secrets.SONAR_ORG }}') -Message 'The unified workflow must retain the Sonar organization variable/secret binding.'
    Assert-True -Condition ([string]$Workflow.env.SONAR_PROJECT_KEY -ceq '${{ vars.SONAR_PROJECT_KEY != '''' && vars.SONAR_PROJECT_KEY || secrets.SONAR_PROJECT_KEY }}') -Message 'The unified workflow must retain the Sonar project-key variable/secret binding.'
    Assert-True -Condition ([string]$Workflow.env.SONAR_COVERAGE_EXCLUSIONS -ceq '**/.github/**,**/Migrations/**,**/LgymApi.Resources.Generator/**,**/LgymApi.DataSeeder/**,**/LgymApi.Application/Common/Errors/*Errors.cs') -Message 'The unified workflow must retain Sonar coverage exclusions.'

    $expectedArtifactName = 'sonar-inputs-${{ github.run_id }}-${{ github.run_attempt }}-${{ github.sha }}'
    $expectedDownloadRoot = '${{ runner.temp }}/sonar-inputs-${{ github.run_id }}-${{ github.run_attempt }}-${{ github.sha }}'
    $waitPropertyPattern = '(?<!\S)/d:sonar\.qualitygate\.wait=true(?!\S)'
    $qualityGateTimeoutPattern = '(?<!\S)/d:sonar\.qualitygate\.timeout(?:=|\s|$)'
    $expectedWaitCounts = @{ 'sonar-required' = 1; 'sonar-push' = 0 }

    foreach ($jobId in @('sonar-required', 'sonar-push')) {
        $job = $Workflow.jobs.$jobId
        Assert-True -Condition ([string]$job.name -ceq $jobId) -Message "Sonar job '$jobId' must retain its published job name."
        Assert-True -Condition ([string]$job.'runs-on' -ceq 'ubuntu-latest') -Message "Sonar job '$jobId' must retain its runner."
        Assert-StringSet -Expected @('final-evidence-gate') -Actual @($job.needs) -Description "Sonar job '$jobId' dependencies"
        Assert-True -Condition ($null -eq $job.PSObject.Properties['continue-on-error']) -Message "Sonar job '$jobId' must remain required."
        Assert-True -Condition ($null -eq $job.PSObject.Properties['timeout-minutes']) -Message "Sonar job '$jobId' must retain the scanner default timeout."
        Assert-True -Condition ($null -eq $job.PSObject.Properties['services']) -Message "Sonar job '$jobId' must not provision PostgreSQL or another service."
        Assert-True -Condition ($null -eq $job.PSObject.Properties['env']) -Message "Sonar job '$jobId' must not carry a PostgreSQL admin connection."

        $jobText = $job | ConvertTo-Json -Depth 64 -Compress
        foreach ($forbiddenContent in @('dotnet test', 'run-postgresql-integration-tests.ps1', 'LGYM_CI_POSTGRES_ADMIN', 'postgres:', 'Create coverage directories')) {
            Assert-True -Condition ($jobText -notmatch [regex]::Escape($forbiddenContent)) -Message "Sonar job '$jobId' must not contain '$forbiddenContent'."
        }
        Assert-True -Condition ($jobText -notmatch '(?i)(mkdir|New-Item).*?(TestResults|coverage)') -Message "Sonar job '$jobId' must not create coverage directories."
        Assert-True -Condition ($jobText -notmatch '(?i)\b(gh|github)\s+api\b|api\.github\.com|workflow_run') -Message "Sonar job '$jobId' must not select artifacts through an external API."

        $steps = @($job.steps)
        $stepNames = @($steps | ForEach-Object { [string]$_.name })
        function Get-StepIndex {
            param([Parameter(Mandatory)][string]$Name)
            return [array]::IndexOf([string[]]$stepNames, $Name)
        }

        foreach ($requiredStepName in @('Checkout', 'Setup Java', 'Setup .NET', 'Install SonarScanner', 'Sonar Begin', 'Restore', 'Build', 'Download validated Sonar inputs', 'Validate downloaded Sonar inputs', 'Copy validated Sonar inputs into scanner checkout', 'Sonar End')) {
            Assert-True -Condition (@($stepNames | Where-Object { $_ -ceq $requiredStepName }).Count -eq 1) -Message "Sonar job '$jobId' must contain exactly one '$requiredStepName' step."
        }

        $checkoutStep = @($steps | Where-Object { $_.name -ceq 'Checkout' })[0]
        $javaStep = @($steps | Where-Object { $_.name -ceq 'Setup Java' })[0]
        $dotnetStep = @($steps | Where-Object { $_.name -ceq 'Setup .NET' })[0]
        $scannerStep = @($steps | Where-Object { $_.name -ceq 'Install SonarScanner' })[0]
        Assert-True -Condition ([string]$checkoutStep.uses -ceq 'actions/checkout@v4') -Message "Sonar job '$jobId' must retain checkout v4."
        Assert-True -Condition ([string]$javaStep.uses -ceq 'actions/setup-java@v4' -and [string]$javaStep.with.distribution -ceq 'temurin' -and [string]$javaStep.with.'java-version' -ceq '17') -Message "Sonar job '$jobId' must retain Java 17 through setup-java v4."
        Assert-True -Condition ([string]$dotnetStep.uses -ceq 'actions/setup-dotnet@v4' -and [string]$dotnetStep.with.'dotnet-version' -ceq '10.0.102') -Message "Sonar job '$jobId' must retain .NET 10.0.102 through setup-dotnet v4."
        Assert-True -Condition ([string]$scannerStep.run -ceq 'dotnet tool install --global dotnet-sonarscanner') -Message "Sonar job '$jobId' must retain the global scanner install policy."

        $beginStep = @($steps | Where-Object { $_.name -ceq 'Sonar Begin' })[0]
        $restoreStep = @($steps | Where-Object { $_.name -ceq 'Restore' })[0]
        $buildStep = @($steps | Where-Object { $_.name -ceq 'Build' })[0]
        $downloadStep = @($steps | Where-Object { $_.name -ceq 'Download validated Sonar inputs' })[0]
        $validateStep = @($steps | Where-Object { $_.name -ceq 'Validate downloaded Sonar inputs' })[0]
        $copyStep = @($steps | Where-Object { $_.name -ceq 'Copy validated Sonar inputs into scanner checkout' })[0]
        $endStep = @($steps | Where-Object { $_.name -ceq 'Sonar End' })[0]
        $beginRun = [string]$beginStep.run
        $validateRun = [string]$validateStep.run
        $copyRun = [string]$copyStep.run

        Assert-True -Condition ($beginRun -match '^\s*dotnet sonarscanner begin(?:\s|$)') -Message "Sonar job '$jobId' must invoke Begin directly."
        foreach ($property in @('/k:"${{ env.SONAR_PROJECT_KEY }}"', '/o:"${{ env.SONAR_ORG }}"', '/d:sonar.host.url="${{ env.SONAR_HOST_URL }}"', '/d:sonar.token="${{ secrets.SONAR_TOKEN }}"', '/d:sonar.exclusions="**/.github/**,**/Migrations/**"', '/d:sonar.coverage.exclusions="${{ env.SONAR_COVERAGE_EXCLUSIONS }}"', '/d:sonar.cs.opencover.reportsPaths="TestResults/SonarInputs/**/coverage.opencover.xml"', '/d:sonar.cs.vstest.reportsPaths="TestResults/SonarInputs/**/*.trx"', '/d:sonar.verbose=true')) {
            Assert-True -Condition ($beginRun -match [regex]::Escape($property)) -Message "Sonar job '$jobId' Begin must contain '$property'."
        }
        Assert-True -Condition (@([regex]::Matches($beginRun, $waitPropertyPattern)).Count -eq $expectedWaitCounts[$jobId]) -Message "Sonar job '$jobId' has an incorrect Quality Gate wait policy."
        Assert-True -Condition (@([regex]::Matches($jobText, $qualityGateTimeoutPattern)).Count -eq 0) -Message "Sonar job '$jobId' must retain the scanner's default Quality Gate timeout."
        Assert-True -Condition ([string]$restoreStep.run -ceq 'dotnet restore LgymApi.sln') -Message "Sonar job '$jobId' must restore after Begin."
        Assert-True -Condition ([string]$buildStep.run -ceq 'dotnet build LgymApi.sln --configuration Release --no-restore') -Message "Sonar job '$jobId' must perform the scanner-instrumented Release build after restore."
        Assert-True -Condition ([string]$endStep.run -ceq 'dotnet sonarscanner end /d:sonar.token="${{ secrets.SONAR_TOKEN }}"') -Message "Sonar job '$jobId' must retain its End invocation."

        Assert-True -Condition ([string]$downloadStep.uses -ceq 'actions/download-artifact@v4') -Message "Sonar job '$jobId' must download through download-artifact v4."
        Assert-StringSet -Expected @('name', 'path') -Actual @($downloadStep.with.PSObject.Properties.Name) -Description "Sonar job '$jobId' download inputs"
        Assert-True -Condition ([string]$downloadStep.with.name -ceq $expectedArtifactName) -Message "Sonar job '$jobId' must download the exact current-run Sonar input artifact."
        Assert-True -Condition ([string]$downloadStep.with.path -ceq $expectedDownloadRoot) -Message "Sonar job '$jobId' must download into the isolated runner-temp artifact root."
        foreach ($forbiddenInput in @('artifact-ids', 'github-token', 'token', 'pattern', 'repository', 'run-id')) {
            Assert-True -Condition ($null -eq $downloadStep.with.PSObject.Properties[$forbiddenInput]) -Message "Sonar job '$jobId' must not set the cross-run download selector '$forbiddenInput'."
        }

        Assert-True -Condition ([string]$validateStep.shell -ceq 'pwsh') -Message "Sonar job '$jobId' must validate artifact provenance through PowerShell."
        foreach ($argument in @('scripts/assert-sonar-inputs.ps1', "-DownloadRoot '$expectedDownloadRoot'", '-CheckoutRoot ''${{ github.workspace }}''', '-Repository ${{ github.repository }}', '-RunId ${{ github.run_id }}', '-RunAttempt ${{ github.run_attempt }}', '-Event ${{ github.event_name }}', '-Ref ${{ github.ref }}', '-MergeSha ${{ github.sha }}', "-PullRequestHeadSha '`$`{{ github.event.pull_request.head.sha }}`'")) {
            Assert-True -Condition ($validateRun -match [regex]::Escape($argument)) -Message "Sonar job '$jobId' must bind '$argument' to assert-sonar-inputs."
        }
        Assert-True -Condition ($validateRun -notmatch [regex]::Escape('-DownloadRoot ''${{ github.workspace }}''')) -Message "Sonar job '$jobId' must keep the consumer input root distinct from its checkout."

        Assert-True -Condition ([string]$copyStep.shell -ceq 'pwsh') -Message "Sonar job '$jobId' must copy validated reports through PowerShell."
        foreach ($copyRequirement in @("`$downloadRoot = '$expectedDownloadRoot'", "Join-Path `$downloadRoot 'TestResults/SonarInputs'", "Join-Path `$env:GITHUB_WORKSPACE 'TestResults/SonarInputs'", 'if (Test-Path -LiteralPath $checkoutSonarInputsDirectory)', 'Copy-Item -LiteralPath $downloadSonarInputsDirectory -Destination $checkoutSonarInputsDirectory -Recurse')) {
            Assert-True -Condition ($copyRun -match [regex]::Escape($copyRequirement)) -Message "Sonar job '$jobId' must copy only the validated SonarInputs subtree through '$copyRequirement'."
        }

        $beginIndex = Get-StepIndex -Name 'Sonar Begin'
        $restoreIndex = Get-StepIndex -Name 'Restore'
        $buildIndex = Get-StepIndex -Name 'Build'
        $downloadIndex = Get-StepIndex -Name 'Download validated Sonar inputs'
        $validateIndex = Get-StepIndex -Name 'Validate downloaded Sonar inputs'
        $copyIndex = Get-StepIndex -Name 'Copy validated Sonar inputs into scanner checkout'
        $endIndex = Get-StepIndex -Name 'Sonar End'
        Assert-True -Condition ($beginIndex -lt $restoreIndex -and $restoreIndex -lt $buildIndex -and $buildIndex -lt $downloadIndex -and $downloadIndex -lt $validateIndex -and $validateIndex -lt $copyIndex -and $copyIndex -lt $endIndex) -Message "Sonar job '$jobId' must preserve Begin, restore, build, download, validate, copy, End ordering."
        foreach ($step in @($steps[0..($validateIndex - 1)])) {
            $stepRun = if ($null -eq $step.PSObject.Properties['run']) { '' } else { [string]$step.run }
            Assert-True -Condition ($stepRun -notmatch 'Copy-Item') -Message "Sonar job '$jobId' must not copy reports into the checkout before consumer validation."
        }
    }

    $requiredCondition = [string]$Workflow.jobs.'sonar-required'.if
    foreach ($requiredClause in @("github.event_name == 'pull_request'", "github.base_ref == 'main'", "github.base_ref == 'automation/modular-monolith-milestone'", 'github.event.pull_request.draft == false', 'github.event.pull_request.head.repo.fork == false', "needs.final-evidence-gate.result == 'success'")) {
        Assert-True -Condition ($requiredCondition -match [regex]::Escape($requiredClause)) -Message "The sonar-required guard must retain '$requiredClause'."
    }
    $pushCondition = [string]$Workflow.jobs.'sonar-push'.if
    foreach ($pushClause in @("github.event_name == 'push'", "github.ref == 'refs/heads/main'", "github.ref == 'refs/heads/automation/modular-monolith-milestone'", "needs.final-evidence-gate.result == 'success'")) {
        Assert-True -Condition ($pushCondition -match [regex]::Escape($pushClause)) -Message "The sonar-push guard must retain '$pushClause'."
    }
}

function Assert-UnifiedSonarFixtureRejected {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Description
    )

    $fixtureWorkflow = @(Get-YamlDocuments -Paths @($Path))[0]
    $rejection = $null
    try {
        Assert-UnifiedSonarContract -Workflow $fixtureWorkflow
    }
    catch {
        $rejection = $_.Exception.Message
    }

    Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($rejection)) -Message "The unified Sonar contract accepted the $Description fixture."
    Write-Output "Rejected unified Sonar $Description fixture: $rejection"
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

function Assert-Task4EvidenceProducerContract {
    param(
        [Parameter(Mandatory)][object]$Workflow,
        [Parameter(Mandatory)][string]$RepositoryRoot
    )

    $nonPostgreSqlJob = $Workflow.jobs.'non-postgresql'
    $matrix = @($nonPostgreSqlJob.strategy.matrix.include)
    Assert-True -Condition ($matrix.Count -eq 4) -Message 'The non-PostgreSQL matrix must contain exactly four suites.'
    Assert-StringSet -Expected @('Architecture', 'DataSeeder', 'InMemoryIntegration', 'Unit') -Actual @($matrix | ForEach-Object { [string]$_.suite }) -Description 'non-PostgreSQL suite matrix'

    $nonPostgreSqlSteps = @($nonPostgreSqlJob.steps | Where-Object { $_.name -ceq 'Run suite with same-SHA TRX evidence' })
    Assert-True -Condition ($nonPostgreSqlSteps.Count -eq 1) -Message 'The non-PostgreSQL matrix must contain exactly one suite execution step.'
    $nonPostgreSqlRun = [string]$nonPostgreSqlSteps[0].run
    Assert-True -Condition (@([regex]::Matches($nonPostgreSqlRun, '(?m)^\s*& dotnet @arguments\s*$')).Count -eq 1) -Message 'The non-PostgreSQL matrix must execute its generated test command exactly once per matrix entry.'
    Assert-True -Condition ($nonPostgreSqlRun -match [regex]::Escape("`$suiteDirectory = Join-Path `$env:GITHUB_WORKSPACE")) -Message 'The non-PostgreSQL suite directory must be rooted at GITHUB_WORKSPACE.'
    Assert-True -Condition ($nonPostgreSqlRun -match [regex]::Escape("`$coveragePath = Join-Path `$suiteDirectory 'coverage.opencover.xml'")) -Message 'The non-PostgreSQL coverage report must be named coverage.opencover.xml beneath its suite directory.'
    foreach ($coverageArgument in @('/p:CollectCoverage=true', '/p:CoverletOutputFormat=opencover', '/p:CoverletOutput=$coveragePath', '/p:UseSourceLink=false')) {
        Assert-True -Condition (@([regex]::Matches($nonPostgreSqlRun, [regex]::Escape($coverageArgument))).Count -eq 1) -Message "The non-PostgreSQL suite command must contain exactly one '$coverageArgument' coverage argument."
    }
    Assert-True -Condition (@([regex]::Matches($nonPostgreSqlRun, 'scripts/assert-trx\.ps1')).Count -eq 1) -Message 'The non-PostgreSQL suite must assert its evidence exactly once.'
    Assert-True -Condition ($nonPostgreSqlRun -match [regex]::Escape('-CoveragePath $coveragePath')) -Message 'The non-PostgreSQL suite must pass its OpenCover report to assert-trx.'

    $postgreSqlJob = $Workflow.jobs.postgresql
    $postgreSqlSteps = @($postgreSqlJob.steps | Where-Object { $_.name -ceq 'Run unfiltered PostgreSQL suite with same-SHA TRX evidence' })
    Assert-True -Condition ($postgreSqlSteps.Count -eq 1) -Message 'The PostgreSQL job must contain exactly one unfiltered suite execution step.'
    $postgreSqlRun = [string]$postgreSqlSteps[0].run
    Assert-True -Condition (@([regex]::Matches($postgreSqlRun, 'scripts/run-postgresql-integration-tests\.ps1')).Count -eq 2) -Message 'The PostgreSQL suite must contain one runner invocation and one command-record reference.'
    Assert-True -Condition (@([regex]::Matches($postgreSqlRun, '(?m)^\s*& pwsh -NoProfile -File scripts/run-postgresql-integration-tests\.ps1')).Count -eq 1) -Message 'The PostgreSQL suite must invoke its runner exactly once.'
    Assert-True -Condition ($postgreSqlRun -notmatch '(?i)--filter|-TestFilter') -Message 'The PostgreSQL suite must remain unfiltered.'
    Assert-True -Condition ($postgreSqlRun -match [regex]::Escape("`$suiteDirectory = Join-Path `$env:GITHUB_WORKSPACE")) -Message 'The PostgreSQL suite directory must be rooted at GITHUB_WORKSPACE.'
    Assert-True -Condition ($postgreSqlRun -match [regex]::Escape("`$coveragePath = Join-Path `$suiteDirectory 'coverage.opencover.xml'")) -Message 'The PostgreSQL coverage report must be named coverage.opencover.xml beneath its suite directory.'
    Assert-True -Condition (@([regex]::Matches($postgreSqlRun, [regex]::Escape('-CoverageOutput $coveragePath'))).Count -eq 1) -Message 'The PostgreSQL runner invocation must bind the suite OpenCover path.'
    Assert-True -Condition ($postgreSqlRun -match [regex]::Escape("'-CoverageOutput', `$coveragePath")) -Message 'The PostgreSQL command record must bind the suite OpenCover path.'
    Assert-True -Condition (@([regex]::Matches($postgreSqlRun, 'scripts/assert-trx\.ps1')).Count -eq 1) -Message 'The PostgreSQL suite must assert its evidence exactly once.'
    Assert-True -Condition ($postgreSqlRun -match [regex]::Escape('-CoveragePath $coveragePath')) -Message 'The PostgreSQL suite must pass its OpenCover report to assert-trx.'

    $runnerPath = Join-Path $RepositoryRoot 'scripts/run-postgresql-integration-tests.ps1'
    $runnerText = [System.IO.File]::ReadAllText($runnerPath)
    Assert-True -Condition ($runnerText -match '\[string\]\$CoverageOutput\s*=\s*""') -Message 'The PostgreSQL runner must accept a coverage output path.'
    Assert-True -Condition ($runnerText -match 'CoverletOutputFormat=opencover') -Message 'The PostgreSQL runner must emit OpenCover coverage.'
    Assert-True -Condition ($runnerText -match 'CoverletOutput=\$CoverageOutput') -Message 'The PostgreSQL runner must direct Coverlet output to the requested path.'
    Assert-True -Condition ($runnerText -match 'UseSourceLink=false') -Message 'The PostgreSQL runner must disable SourceLink when collecting coverage.'

    $expectedArtifactNames = @(
        'verification-build-${{ github.run_id }}-${{ github.run_attempt }}-${{ github.sha }}',
        'verification-${{ matrix.suite }}-${{ github.run_id }}-${{ github.run_attempt }}-${{ github.sha }}',
        'verification-PostgreSqlIntegration-${{ github.run_id }}-${{ github.run_attempt }}-${{ github.sha }}',
        'verification-PostgreSqlCleanup-${{ github.run_id }}-${{ github.run_attempt }}-${{ github.sha }}',
        'verification-final-${{ github.run_id }}-${{ github.run_attempt }}-${{ github.sha }}',
        'sonar-inputs-${{ github.run_id }}-${{ github.run_attempt }}-${{ github.sha }}'
    )
    $actionSteps = @(Get-WorkflowActionSteps -Workflow $Workflow)
    $uploads = @($actionSteps | Where-Object { $_.uses -ceq 'actions/upload-artifact@v4' })
    Assert-True -Condition ($uploads.Count -eq $expectedArtifactNames.Count) -Message 'Expected exactly six fatal verification and Sonar input artifact uploads.'
    Assert-StringSet -Expected $expectedArtifactNames -Actual @($uploads | ForEach-Object { [string]$_.with.name }) -Description 'run-aware artifact names'
    foreach ($upload in $uploads) {
        Assert-True -Condition ($upload.with.'if-no-files-found' -ceq 'error') -Message "Artifact '$($upload.with.name)' must fail when files are absent."
    }

    $sonarUploads = @($uploads | Where-Object { [string]$_.with.name -ceq 'sonar-inputs-${{ github.run_id }}-${{ github.run_attempt }}-${{ github.sha }}' })
    Assert-True -Condition ($sonarUploads.Count -eq 1) -Message 'The final gate must publish exactly one normalized sonar-inputs artifact.'
    Assert-True -Condition ([string]$sonarUploads[0].with.path -ceq 'TestResults/SonarInputsArtifact') -Message 'The sonar-inputs artifact must upload only the normalized producer output directory.'

    $finalGate = $Workflow.jobs.'final-evidence-gate'
    Assert-StringSet -Expected @('non-postgresql', 'postgresql', 'postgresql-cleanup', 'release-build') -Actual @($finalGate.needs) -Description 'final evidence gate dependencies'
    $finalDownloads = @($finalGate.steps | Where-Object { $null -ne $_.PSObject.Properties['uses'] -and $_.uses -ceq 'actions/download-artifact@v4' })
    Assert-True -Condition ($finalDownloads.Count -eq 1) -Message 'The final gate must download exactly one current-run verification artifact set.'
    Assert-True -Condition ([string]$finalDownloads[0].with.pattern -ceq 'verification-*-${{ github.run_id }}-${{ github.run_attempt }}-${{ github.sha }}') -Message 'The final gate must match only the current run, attempt, and SHA verification artifacts.'
    Assert-True -Condition ([string]$finalDownloads[0].with.path -ceq 'TestResults/Collected' -and $finalDownloads[0].with.'merge-multiple' -eq $false) -Message 'The final gate must preserve isolated verification artifact directories.'

    $validatorSteps = @($finalGate.steps | Where-Object { $_.name -ceq 'Validate complete same-SHA evidence before publication' })
    Assert-True -Condition ($validatorSteps.Count -eq 1) -Message 'The final gate must contain exactly one final evidence producer invocation.'
    $validatorRun = [string]$validatorSteps[0].run
    foreach ($argument in @(
            'scripts/assert-ci-verification-evidence.ps1',
            '-EvidenceRoot TestResults/Collected',
            '-Repository ${{ github.repository }}',
            '-RunId ${{ github.run_id }}',
            '-RunAttempt ${{ github.run_attempt }}',
            '-Event ${{ github.event_name }}',
            '-Ref ${{ github.ref }}',
            '-MergeSha ${{ github.sha }}',
            "-PullRequestHeadSha '`$`{{ github.event.pull_request.head.sha }}`'",
            '-SonarInputsDirectory TestResults/SonarInputsArtifact')) {
        Assert-True -Condition ($validatorRun -match [regex]::Escape($argument)) -Message "The final evidence producer must receive '$argument'."
    }

    Assert-TestsCompatibilityContract -Workflow $Workflow
}

function Assert-Task4EvidenceProducerFixtureRejected {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$Description
    )

    $fixtureWorkflow = @(Get-YamlDocuments -Paths @($Path))[0]
    $rejection = $null
    try {
        Assert-Task4EvidenceProducerContract -Workflow $fixtureWorkflow -RepositoryRoot $RepositoryRoot
    }
    catch {
        $rejection = $_.Exception.Message
    }

    Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($rejection)) -Message "The Task 4 evidence producer contract accepted the $Description fixture."
    Write-Output "Rejected Task 4 $Description fixture: $rejection"
}

function Assert-FinalEvidenceProducerArgumentBinding {
    param(
        [Parameter(Mandatory)][string]$ValidatorRun,
        [Parameter(Mandatory)][string]$ProbePath
    )

    $probe = @'
param(
    [Parameter(Mandatory)][string]$EvidenceRoot,
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$RunId,
    [Parameter(Mandatory)][string]$RunAttempt,
    [Parameter(Mandatory)][string]$Event,
    [Parameter(Mandatory)][string]$Ref,
    [Parameter(Mandatory)][string]$MergeSha,
    [Parameter(Mandatory)][AllowEmptyString()][string]$PullRequestHeadSha,
    [Parameter(Mandatory)][string]$SonarInputsDirectory
)

[pscustomobject]@{
    pullRequestHeadSha = $PullRequestHeadSha
    sonarInputsDirectory = $SonarInputsDirectory
} | ConvertTo-Json -Compress
'@
    [System.IO.File]::WriteAllText($ProbePath, $probe, [System.Text.UTF8Encoding]::new($false))

    function Invoke-RenderedProducerProbe {
        param([Parameter(Mandatory)][string]$RenderedCommand)

        $output = @(& ([scriptblock]::Create($RenderedCommand)) 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "The rendered producer command failed with exit code $($LASTEXITCODE): $($output -join [Environment]::NewLine)"
        }

        return (($output -join [Environment]::NewLine) | ConvertFrom-Json -Depth 8)
    }

    function Render-ProducerCommand {
        param(
            [Parameter(Mandatory)][string]$Event,
            [Parameter(Mandatory)][string]$Ref,
            [Parameter(Mandatory)][AllowEmptyString()][string]$PullRequestHeadSha
        )

        $rendered = $ValidatorRun.Replace('scripts/assert-ci-verification-evidence.ps1', "'$($ProbePath.Replace("'", "''"))'")
        $replacements = [ordered]@{
                '${{ github.repository }}' = 'lgym/LGYM-APP-APIv3'
                '${{ github.run_id }}' = '440001'
                '${{ github.run_attempt }}' = '2'
                '${{ github.event_name }}' = $Event
                '${{ github.ref }}' = $Ref
                '${{ github.sha }}' = '0123456789abcdef0123456789abcdef01234567'
                '${{ github.event.pull_request.head.sha }}' = $PullRequestHeadSha
            }
        foreach ($replacement in $replacements.GetEnumerator()) {
            $rendered = $rendered.Replace([string]$replacement.Key, [string]$replacement.Value)
        }

        return $rendered
    }

    $prHeadSha = '89abcdef0123456789abcdef0123456789abcdef'
    $prCommand = Render-ProducerCommand -Event 'pull_request' -Ref 'refs/pull/440/merge' -PullRequestHeadSha $prHeadSha
    $prResult = Invoke-RenderedProducerProbe -RenderedCommand $prCommand
    Assert-True -Condition ($prResult.pullRequestHeadSha -ceq $prHeadSha -and $prResult.sonarInputsDirectory -ceq 'TestResults/SonarInputsArtifact') -Message 'The rendered pull-request producer command did not bind its head SHA and Sonar input directory independently.'
    Write-Output "Bound final producer pull_request command: $prCommand"

    $pushCommand = Render-ProducerCommand -Event 'push' -Ref 'refs/heads/main' -PullRequestHeadSha ''
    $pushResult = Invoke-RenderedProducerProbe -RenderedCommand $pushCommand
    Assert-True -Condition ($pushResult.pullRequestHeadSha -ceq '' -and $pushResult.sonarInputsDirectory -ceq 'TestResults/SonarInputsArtifact') -Message 'The rendered push producer command did not bind an empty PR head SHA and independent Sonar input directory.'
    Write-Output "Bound final producer push command: $pushCommand"

    $unsafePushCommand = $pushCommand.Replace("-PullRequestHeadSha ''", '-PullRequestHeadSha')
    $unsafeOutput = @(& ([scriptblock]::Create($unsafePushCommand)) 2>&1)
    Assert-True -Condition ($LASTEXITCODE -ne 0) -Message 'The unprotected rendered push command unexpectedly bound its next switch safely.'
    Write-Output "Rejected unprotected rendered push command: $($unsafeOutput -join [Environment]::NewLine)"
}

function Assert-SonarConsumerArgumentBinding {
    param(
        [Parameter(Mandatory)][string]$ValidatorRun,
        [Parameter(Mandatory)][string]$ProbePath
    )

    $probe = @'
param(
    [Parameter(Mandatory)][string]$DownloadRoot,
    [Parameter(Mandatory)][string]$CheckoutRoot,
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$RunId,
    [Parameter(Mandatory)][string]$RunAttempt,
    [Parameter(Mandatory)][string]$Event,
    [Parameter(Mandatory)][string]$Ref,
    [Parameter(Mandatory)][string]$MergeSha,
    [Parameter(Mandatory)][AllowEmptyString()][string]$PullRequestHeadSha
)

[pscustomobject]@{
    downloadRoot = $DownloadRoot
    checkoutRoot = $CheckoutRoot
    pullRequestHeadSha = $PullRequestHeadSha
} | ConvertTo-Json -Compress
'@
    [System.IO.File]::WriteAllText($ProbePath, $probe, [System.Text.UTF8Encoding]::new($false))

    function Invoke-RenderedConsumerProbe {
        param([Parameter(Mandatory)][string]$RenderedCommand)

        $output = @(& ([scriptblock]::Create($RenderedCommand)) 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "The rendered consumer command failed with exit code $($LASTEXITCODE): $($output -join [Environment]::NewLine)"
        }

        return (($output -join [Environment]::NewLine) | ConvertFrom-Json -Depth 8)
    }

    function Render-ConsumerCommand {
        param(
            [Parameter(Mandatory)][string]$Event,
            [Parameter(Mandatory)][string]$Ref,
            [Parameter(Mandatory)][AllowEmptyString()][string]$PullRequestHeadSha
        )

        $rendered = $ValidatorRun.Replace('scripts/assert-sonar-inputs.ps1', "'$($ProbePath.Replace("'", "''"))'")
        $replacements = [ordered]@{
            '${{ runner.temp }}' = '/tmp/lgym-runner-temp'
            '${{ github.workspace }}' = '/tmp/lgym-checkout'
            '${{ github.repository }}' = 'lgym/LGYM-APP-APIv3'
            '${{ github.run_id }}' = '440001'
            '${{ github.run_attempt }}' = '2'
            '${{ github.event_name }}' = $Event
            '${{ github.ref }}' = $Ref
            '${{ github.sha }}' = '0123456789abcdef0123456789abcdef01234567'
            '${{ github.event.pull_request.head.sha }}' = $PullRequestHeadSha
        }
        foreach ($replacement in $replacements.GetEnumerator()) {
            $rendered = $rendered.Replace([string]$replacement.Key, [string]$replacement.Value)
        }

        return $rendered
    }

    $prHeadSha = '89abcdef0123456789abcdef0123456789abcdef'
    $prResult = Invoke-RenderedConsumerProbe -RenderedCommand (Render-ConsumerCommand -Event 'pull_request' -Ref 'refs/pull/440/merge' -PullRequestHeadSha $prHeadSha)
    Assert-True -Condition ($prResult.downloadRoot -ceq '/tmp/lgym-runner-temp/sonar-inputs-440001-2-0123456789abcdef0123456789abcdef01234567' -and $prResult.checkoutRoot -ceq '/tmp/lgym-checkout' -and $prResult.pullRequestHeadSha -ceq $prHeadSha) -Message 'The rendered pull-request consumer command did not bind isolated input, checkout, and PR-head values.'

    $pushCommand = Render-ConsumerCommand -Event 'push' -Ref 'refs/heads/main' -PullRequestHeadSha ''
    $pushResult = Invoke-RenderedConsumerProbe -RenderedCommand $pushCommand
    Assert-True -Condition ($pushResult.downloadRoot -ceq '/tmp/lgym-runner-temp/sonar-inputs-440001-2-0123456789abcdef0123456789abcdef01234567' -and $pushResult.checkoutRoot -ceq '/tmp/lgym-checkout' -and $pushResult.pullRequestHeadSha -ceq '') -Message 'The rendered push consumer command did not bind an empty PR head SHA safely.'

    $unsafePushCommand = $pushCommand.Replace("-PullRequestHeadSha ''", '-PullRequestHeadSha')
    $unsafeOutput = @(& ([scriptblock]::Create($unsafePushCommand)) 2>&1)
    Assert-True -Condition ($LASTEXITCODE -ne 0) -Message 'The unprotected rendered push consumer command unexpectedly bound its next switch safely.'
    Write-Output "Bound Sonar consumer pull_request and push commands; rejected unprotected push command: $($unsafeOutput -join [Environment]::NewLine)"
}

function New-EventContext {
    param(
        [Parameter(Mandatory)][string]$EventName,
        [Parameter(Mandatory)][AllowEmptyString()][string]$BaseRef,
        [Parameter(Mandatory)][string]$Ref,
        [bool]$Draft = $false,
        [bool]$Fork = $false,
        [string]$ReleaseBuildResult = 'success',
        [string]$FinalEvidenceGateResult = 'success'
    )

    return @{
        github = @{
            event_name = $EventName
            base_ref = $BaseRef
            ref = $Ref
            event = @{ pull_request = @{ draft = $Draft; head = @{ repo = @{ fork = $Fork } } } }
        }
        needs = @{
            'release-build' = @{ result = $ReleaseBuildResult }
            'non-postgresql' = @{ result = $ReleaseBuildResult }
            postgresql = @{ result = $ReleaseBuildResult }
            'postgresql-cleanup' = @{ result = $ReleaseBuildResult }
            'final-evidence-gate' = @{ result = $FinalEvidenceGateResult }
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

function Write-OpenCoverFixture {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$SourcePath
    )

    $escapedSourcePath = [System.Security.SecurityElement]::Escape($SourcePath)
    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<CoverageSession>
  <Modules>
    <Module>
      <Files>
        <File uid="1" fullPath="$escapedSourcePath" />
      </Files>
    </Module>
  </Modules>
</CoverageSession>
"@
    [System.IO.File]::WriteAllText($Path, $xml, [System.Text.UTF8Encoding]::new($false))
}

function Update-FixtureCoverageMetadata {
    param(
        [Parameter(Mandatory)][string]$SummaryPath,
        [Parameter(Mandatory)][string]$CoveragePath
    )

    $summary = [System.IO.File]::ReadAllText($SummaryPath) | ConvertFrom-Json -Depth 32
    $coverage = Get-Item -LiteralPath $CoveragePath
    $summary.coverage.sha256 = (Get-FileHash -LiteralPath $coverage.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $summary.coverage.bytes = $coverage.Length
    $summary.coverage.lastWriteUtc = $coverage.LastWriteTimeUtc.ToString('O')
    Write-JsonFixture -Path $SummaryPath -Value $summary
}

function Invoke-FinalEvidenceGate {
    param(
        [Parameter(Mandatory)][string]$EvidenceGatePath,
        [Parameter(Mandatory)][string]$EvidenceRoot,
        [Parameter(Mandatory)][string]$MergeSha,
        [string]$Repository = 'lgym/LGYM-APP-APIv3',
        [string]$RunId = '440001',
        [string]$RunAttempt = '2',
        [string]$Event = 'pull_request',
        [string]$Ref = 'refs/pull/440/merge',
        [string]$PullRequestHeadSha = '89abcdef0123456789abcdef0123456789abcdef',
        [string]$SonarInputsDirectory = ''
    )

    $arguments = @(
        '-EvidenceRoot', $EvidenceRoot,
        '-Repository', $Repository,
        '-RunId', $RunId,
        '-RunAttempt', $RunAttempt,
        '-Event', $Event,
        '-Ref', $Ref,
        '-MergeSha', $MergeSha,
        '-PullRequestHeadSha', $PullRequestHeadSha
    )
    if (-not [string]::IsNullOrWhiteSpace($SonarInputsDirectory)) {
        $arguments += @('-SonarInputsDirectory', $SonarInputsDirectory)
    }

    & pwsh -NoProfile -File $EvidenceGatePath @arguments *> $null
    return $LASTEXITCODE
}

function Assert-EvidenceGateRejectedWithoutPublish {
    param(
        [Parameter(Mandatory)][string]$EvidenceGatePath,
        [Parameter(Mandatory)][string]$EvidenceRoot,
        [Parameter(Mandatory)][string]$MergeSha,
        [Parameter(Mandatory)][string]$SonarInputsDirectory,
        [Parameter(Mandatory)][string]$Description,
        [string]$Repository = 'lgym/LGYM-APP-APIv3',
        [string]$RunId = '440001',
        [string]$RunAttempt = '2'
    )

    $exitCode = Invoke-FinalEvidenceGate `
        -EvidenceGatePath $EvidenceGatePath `
        -EvidenceRoot $EvidenceRoot `
        -MergeSha $MergeSha `
        -Repository $Repository `
        -RunId $RunId `
        -RunAttempt $RunAttempt `
        -SonarInputsDirectory $SonarInputsDirectory 2>$null
    Assert-True -Condition ($exitCode -ne 0) -Message "The final evidence gate accepted the $Description fixture."
    Assert-True -Condition (-not (Test-Path -LiteralPath $SonarInputsDirectory)) -Message "The final evidence gate published partial Sonar inputs for the $Description fixture."

    $parent = Split-Path -Parent $SonarInputsDirectory
    $leaf = Split-Path -Leaf $SonarInputsDirectory
    $partialDirectories = @(Get-ChildItem -LiteralPath $parent -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "^\.$([regex]::Escape($leaf))\.staging-" })
    Assert-True -Condition ($partialDirectories.Count -eq 0) -Message "The final evidence gate left staging debris for the $Description fixture."
    Write-Output "Rejected final-evidence $Description fixture without publication."
}

function Assert-StagedSonarInputs {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$MergeSha
    )

    $expectedSuites = @('Architecture', 'DataSeeder', 'InMemoryIntegration', 'PostgreSqlIntegration', 'Unit')
    $expectedFiles = @('manifest.json')
    foreach ($suite in $expectedSuites) {
        $expectedFiles += @(
            "TestResults/SonarInputs/$suite/$suite-$MergeSha.trx",
            "TestResults/SonarInputs/$suite/coverage.opencover.xml"
        )
    }

    $actualFiles = @(
        Get-ChildItem -LiteralPath $Path -File -Recurse |
            ForEach-Object { $_.FullName.Substring($Path.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar).Replace('\', '/') }
    )
    Assert-StringSet -Expected $expectedFiles -Actual $actualFiles -Description 'staged Sonar input files'
    Assert-True -Condition ($actualFiles.Count -eq 11) -Message 'The staged Sonar input tree must contain exactly eleven files.'
    Assert-StringSet -Expected @('manifest.json', 'TestResults') -Actual @(Get-ChildItem -LiteralPath $Path -Force | ForEach-Object { $_.Name }) -Description 'consumer-compatible artifact root entries'
    $testResultsDirectory = Join-Path $Path 'TestResults'
    Assert-StringSet -Expected @('SonarInputs') -Actual @(Get-ChildItem -LiteralPath $testResultsDirectory -Force | ForEach-Object { $_.Name }) -Description 'consumer-compatible TestResults entries'
    $sonarInputsDirectory = Join-Path $testResultsDirectory 'SonarInputs'
    Assert-StringSet -Expected $expectedSuites -Actual @(Get-ChildItem -LiteralPath $sonarInputsDirectory -Directory | ForEach-Object { $_.Name }) -Description 'consumer-compatible SonarInputs suite entries'
    Assert-True -Condition (@(Get-ChildItem -LiteralPath $Path -Directory | Where-Object { $_.Name -in $expectedSuites }).Count -eq 0) -Message 'The artifact root must not contain suite directories.'

    $manifest = [System.IO.File]::ReadAllText((Join-Path $Path 'manifest.json')) | ConvertFrom-Json -Depth 32
    Assert-True -Condition ($manifest.schemaVersion -eq 1 -and $manifest.kind -ceq 'sonar-inputs') -Message 'The staged Sonar input manifest has an unsupported schema.'
    Assert-True -Condition ($manifest.repository -ceq 'lgym/LGYM-APP-APIv3') -Message 'The staged Sonar input manifest repository is incorrect.'
    Assert-True -Condition ($manifest.runId -is [string] -and $manifest.runAttempt -is [string] -and $manifest.runId -ceq '440001' -and $manifest.runAttempt -ceq '2') -Message 'The staged Sonar input manifest run provenance must be canonical decimal strings matching the CLI values.'
    Assert-True -Condition ($manifest.event -ceq 'pull_request' -and $manifest.ref -ceq 'refs/pull/440/merge') -Message 'The staged Sonar input manifest event provenance is incorrect.'
    Assert-True -Condition ($manifest.mergeSha -ceq $MergeSha -and $manifest.pullRequestHeadSha -ceq '89abcdef0123456789abcdef0123456789abcdef') -Message 'The staged Sonar input manifest SHA provenance is incorrect.'
    Assert-StringSet -Expected $expectedSuites -Actual @($manifest.suites | ForEach-Object { [string]$_.suite }) -Description 'staged Sonar input manifest suites'
    foreach ($suiteEvidence in @($manifest.suites)) {
        $suite = [string]$suiteEvidence.suite
        Assert-True -Condition ($suiteEvidence.trx.checkoutRelativePath -ceq "TestResults/SonarInputs/$suite/$suite-$MergeSha.trx") -Message "The staged '$suite' TRX path is not checkout-relative."
        Assert-True -Condition ($suiteEvidence.coverage.checkoutRelativePath -ceq "TestResults/SonarInputs/$suite/coverage.opencover.xml") -Message "The staged '$suite' coverage path is not checkout-relative."
        Assert-True -Condition ($suiteEvidence.trx.sha256 -match '^[0-9a-f]{64}$' -and $suiteEvidence.trx.bytes -gt 0) -Message "The staged '$suite' TRX metadata is incomplete."
        Assert-True -Condition ($suiteEvidence.coverage.sha256 -match '^[0-9a-f]{64}$' -and $suiteEvidence.coverage.bytes -gt 0 -and $suiteEvidence.coverage.moduleCount -eq 1 -and $suiteEvidence.coverage.fileCount -eq 1 -and $suiteEvidence.coverage.localPathMode -ceq 'repository-rooted') -Message "The staged '$suite' coverage metadata is incomplete."
    }

    Write-Output "Inspected staged Sonar tree: $(($actualFiles | Sort-Object) -join ', ')"
    Write-Output "Inspected staged Sonar manifest: $($manifest | ConvertTo-Json -Depth 16 -Compress)"
}

function New-EvidenceFixture {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Head,
        [Parameter(Mandatory)][string]$RepositorySourcePath
    )

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        $null = New-Item -ItemType Directory -Path $Root -Force
    }
    Get-ChildItem -LiteralPath $Root -Force | Remove-Item -Recurse -Force

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
        $evidenceDirectory = New-Item -ItemType Directory -Path (Join-Path $directory.FullName 'evidence') -Force
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
        $coveragePath = Join-Path $directory.FullName 'coverage.opencover.xml'
        Write-OpenCoverFixture -Path $coveragePath -SourcePath $RepositorySourcePath
        $coverageFile = Get-Item -LiteralPath $coveragePath
        $summaryPath = Join-Path $evidenceDirectory.FullName 'trx-summary.json'
        Write-JsonFixture -Path $summaryPath -Value ([pscustomobject]@{
                kind = 'trx-summary'
                suite = $suite
                completeSuite = $true
                repository = $cleanRepository
                command = [pscustomobject]@{ exitCode = 0; notBeforeUtc = [System.DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('O') }
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
                coverage = [pscustomobject]@{
                    path = $coverageFile.FullName
                    fileName = $coverageFile.Name
                    sha256 = (Get-FileHash -LiteralPath $coverageFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                    bytes = $coverageFile.Length
                    lastWriteUtc = $coverageFile.LastWriteTimeUtc.ToString('O')
                    moduleCount = 1
                    fileCount = 1
                    localPathMode = 'repository-rooted'
                }
            })
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$prWorkflowPath = Join-Path $repositoryRoot '.github/workflows/pr-and-main-tests.yml'
$sonarWorkflowPath = Join-Path $repositoryRoot '.github/workflows/sonarcloud-analysis.yml'
$evidenceGatePath = Join-Path $PSScriptRoot 'assert-ci-verification-evidence.ps1'

Assert-True -Condition (-not (Test-Path -LiteralPath $sonarWorkflowPath -PathType Leaf)) -Message 'The standalone Sonar workflow must be deleted so the PR workflow is the only eligible Sonar trigger.'
$documents = Get-YamlDocuments -Paths @($prWorkflowPath)
$prWorkflow = $documents[0]

$expectedBranches = @('automation/modular-monolith-milestone', 'main')
Assert-StringSet -Expected $expectedBranches -Actual @($prWorkflow.on.pull_request.branches) -Description 'PR workflow pull-request branches'
Assert-StringSet -Expected $expectedBranches -Actual @($prWorkflow.on.push.branches) -Description 'PR workflow push branches'

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
Assert-Task4EvidenceProducerContract -Workflow $prWorkflow -RepositoryRoot $repositoryRoot
Assert-UnifiedSonarContract -Workflow $prWorkflow
$actionSteps = @(Get-WorkflowActionSteps -Workflow $prWorkflow)
Assert-True -Condition (@($actionSteps | Where-Object { $_.uses -match 'test-reporter|dorny/' }).Count -eq 0) -Message 'A summary publisher can run before final evidence validation.'

$requiredCiJobs = @('release-build', 'non-postgresql', 'postgresql', 'postgresql-cleanup', 'final-evidence-gate', 'tests')
$cases = @(
    [pscustomobject]@{ name = 'trusted non-draft PR to automation'; context = New-EventContext -EventName 'pull_request' -BaseRef 'automation/modular-monolith-milestone' -Ref 'refs/pull/1/merge'; jobs = @($requiredCiJobs + 'sonar-required') },
    [pscustomobject]@{ name = 'trusted ready-for-review PR to main'; context = New-EventContext -EventName 'pull_request' -BaseRef 'main' -Ref 'refs/pull/2/merge'; jobs = @($requiredCiJobs + 'sonar-required') },
    [pscustomobject]@{ name = 'fork PR to main'; context = New-EventContext -EventName 'pull_request' -BaseRef 'main' -Ref 'refs/pull/3/merge' -Fork $true; jobs = @($requiredCiJobs) },
    [pscustomobject]@{ name = 'trusted draft PR'; context = New-EventContext -EventName 'pull_request' -BaseRef 'main' -Ref 'refs/pull/4/merge' -Draft $true -ReleaseBuildResult 'skipped' -FinalEvidenceGateResult 'skipped'; jobs = @('tests') },
    [pscustomobject]@{ name = 'fork draft PR'; context = New-EventContext -EventName 'pull_request' -BaseRef 'main' -Ref 'refs/pull/5/merge' -Draft $true -Fork $true -ReleaseBuildResult 'skipped' -FinalEvidenceGateResult 'skipped'; jobs = @('tests') },
    [pscustomobject]@{ name = 'automation push'; context = New-EventContext -EventName 'push' -BaseRef '' -Ref 'refs/heads/automation/modular-monolith-milestone'; jobs = @($requiredCiJobs + 'sonar-push') },
    [pscustomobject]@{ name = 'main push'; context = New-EventContext -EventName 'push' -BaseRef '' -Ref 'refs/heads/main'; jobs = @($requiredCiJobs + 'sonar-push') }
)
foreach ($case in $cases) {
    Assert-StringSet -Expected $case.jobs -Actual (Get-SelectedJobs -Workflow $prWorkflow -Context $case.context) -Description "$($case.name) unified CI/Sonar job selection"
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("lgym-ci-workflow-fixtures-" + [Guid]::NewGuid().ToString('N'))
try {
    $null = New-Item -ItemType Directory -Path $temporaryRoot -Force
    $fixtureHead = '0123456789abcdef0123456789abcdef01234567'
    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath

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

    $validatorRun = [string]($prWorkflow.jobs.'final-evidence-gate'.steps | Where-Object { $_.name -ceq 'Validate complete same-SHA evidence before publication' }).run
    Assert-FinalEvidenceProducerArgumentBinding -ValidatorRun $validatorRun -ProbePath (Join-Path $temporaryRoot 'final-evidence-binding-probe.ps1')

    $task4FixtureRoot = New-Item -ItemType Directory -Path (Join-Path $temporaryRoot 'task-4-workflows') -Force
    $duplicateSuiteExecutionPath = Join-Path $task4FixtureRoot.FullName 'duplicate-suite-execution.yml'
    $missingSuitePath = Join-Path $task4FixtureRoot.FullName 'missing-suite.yml'
    $missingCoveragePath = Join-Path $task4FixtureRoot.FullName 'missing-coverage.yml'
    $secondPostgreSqlRunnerPath = Join-Path $task4FixtureRoot.FullName 'second-postgresql-runner.yml'
    $shaOnlyArtifactPath = Join-Path $task4FixtureRoot.FullName 'sha-only-artifact.yml'
    $nonfatalUploadPath = Join-Path $task4FixtureRoot.FullName 'nonfatal-upload.yml'
    $brokenProvenancePath = Join-Path $task4FixtureRoot.FullName 'broken-provenance.yml'
    $artifactLayoutPath = Join-Path $task4FixtureRoot.FullName 'artifact-layout.yml'
    $unprotectedPushHeadPath = Join-Path $task4FixtureRoot.FullName 'unprotected-push-head.yml'

    $duplicateSuiteExecutionText = [regex]::Replace($prWorkflowText, '(?m)^(\s*)& dotnet @arguments\s*$', "`$1& dotnet @arguments`r`n`$1& dotnet @arguments", 1)
    $missingSuiteText = [regex]::Replace($prWorkflowText, '(?ms)\r?\n          - suite: DataSeeder.*?(?=\r?\n    env:)', '', 1)
    $missingCoverageText = [regex]::Replace($prWorkflowText, "(?m)^\s*'/p:UseSourceLink=false'\r?\n", '', 1)
    $postgreSqlRunnerInvocation = [regex]::Match($prWorkflowText, '(?ms)^\s*& pwsh -NoProfile -File scripts/run-postgresql-integration-tests\.ps1\s+.*?^\s+-NoBuild\s*$')
    Assert-True -Condition $postgreSqlRunnerInvocation.Success -Message 'The second-PostgreSQL-runner fixture could not locate the existing runner invocation.'
    $secondPostgreSqlRunnerText = $prWorkflowText.Insert($postgreSqlRunnerInvocation.Index + $postgreSqlRunnerInvocation.Length, [Environment]::NewLine + $postgreSqlRunnerInvocation.Value)
    $shaOnlyArtifactText = $prWorkflowText.Replace('verification-build-${{ github.run_id }}-${{ github.run_attempt }}-${{ github.sha }}', 'verification-build-${{ github.sha }}')
    $nonfatalUploadText = $prWorkflowText.Replace('if-no-files-found: error', 'if-no-files-found: warn')
    $brokenProvenanceText = $prWorkflowText.Replace('-RunAttempt ${{ github.run_attempt }}', '')
    $artifactLayoutText = $prWorkflowText.Replace('TestResults/SonarInputsArtifact', 'TestResults/Collected')
    $unprotectedPushHeadText = $prWorkflowText.Replace("-PullRequestHeadSha '`$`{{ github.event.pull_request.head.sha }}`'", '-PullRequestHeadSha ${{ github.event.pull_request.head.sha }}')
    foreach ($fixture in @(
            [pscustomobject]@{ path = $duplicateSuiteExecutionPath; text = $duplicateSuiteExecutionText; description = 'duplicate suite execution' },
            [pscustomobject]@{ path = $missingSuitePath; text = $missingSuiteText; description = 'missing suite' },
            [pscustomobject]@{ path = $missingCoveragePath; text = $missingCoverageText; description = 'missing coverage flag' },
            [pscustomobject]@{ path = $secondPostgreSqlRunnerPath; text = $secondPostgreSqlRunnerText; description = 'second PostgreSQL runner invocation' },
            [pscustomobject]@{ path = $shaOnlyArtifactPath; text = $shaOnlyArtifactText; description = 'SHA-only artifact name' },
            [pscustomobject]@{ path = $nonfatalUploadPath; text = $nonfatalUploadText; description = 'nonfatal artifact upload' },
            [pscustomobject]@{ path = $brokenProvenancePath; text = $brokenProvenanceText; description = 'broken final producer provenance' },
            [pscustomobject]@{ path = $artifactLayoutPath; text = $artifactLayoutText; description = 'non-normalized Sonar artifact layout' },
            [pscustomobject]@{ path = $unprotectedPushHeadPath; text = $unprotectedPushHeadText; description = 'unprotected push PR-head parameter' })) {
        Assert-True -Condition ($fixture.text -cne $prWorkflowText) -Message "The Task 4 $($fixture.description) fixture could not mutate the workflow."
        [System.IO.File]::WriteAllText($fixture.path, $fixture.text, [System.Text.UTF8Encoding]::new($false))
        Assert-Task4EvidenceProducerFixtureRejected -Path $fixture.path -RepositoryRoot $repositoryRoot -Description $fixture.description
    }

    foreach ($jobId in @('sonar-required', 'sonar-push')) {
        $consumerRun = [string]($prWorkflow.jobs.$jobId.steps | Where-Object { $_.name -ceq 'Validate downloaded Sonar inputs' }).run
        Assert-SonarConsumerArgumentBinding -ValidatorRun $consumerRun -ProbePath (Join-Path $temporaryRoot "sonar-$jobId-binding-probe.ps1")
    }

    $sonarFixtureRoot = New-Item -ItemType Directory -Path (Join-Path $temporaryRoot 'sonar-workflows') -Force
    $waitProperty = '/d:sonar.qualitygate.wait=true'
    $missingRequiredText = [regex]::Replace($prWorkflowText, '(?ms)\r?\n  sonar-required:\r?\n.*?(?=\r?\n  sonar-push:)', '', 1)
    $wrongJobNameText = $prWorkflowText.Replace('    name: sonar-required', '    name: sonar-required-renamed')
    $missingNeedsText = [regex]::Replace($prWorkflowText, '(?ms)(  sonar-required:.*?    needs:) final-evidence-gate', '${1} release-build', 1)
    $removedDraftGuardText = $prWorkflowText.Replace("      github.event.pull_request.draft == false &&`n", '')
    $removedForkGuardText = $prWorkflowText.Replace("      github.event.pull_request.head.repo.fork == false`n", '')
    $sonarTestText = [regex]::Replace($prWorkflowText, '(?ms)(  sonar-required:.*?      - name: Restore\r?\n        run: dotnet restore LgymApi\.sln)', '$1 && dotnet test LgymApi.UnitTests/LgymApi.UnitTests.csproj', 1)
    $sonarRunnerText = [regex]::Replace($prWorkflowText, '(?ms)(  sonar-required:.*?      - name: Restore\r?\n        run: dotnet restore LgymApi\.sln)', '$1 && pwsh -NoProfile -File scripts/run-postgresql-integration-tests.ps1', 1)
    $coverageDirectoryText = [regex]::Replace($prWorkflowText, '(?ms)(  sonar-required:.*?      - name: Restore\r?\n        run: dotnet restore LgymApi\.sln)', '$1 && mkdir -p TestResults/Unit', 1)
    $sonarPostgreSqlText = [regex]::Replace($prWorkflowText, '(?ms)(  sonar-required:.*?    runs-on: ubuntu-latest)\r?\n', "`$1`r`n    services:`r`n      postgres: {}`r`n", 1)
    $crossRunSelectorText = [regex]::Replace($prWorkflowText, '(?ms)(  sonar-required:.*?          name: sonar-inputs-\$\{\{ github\.run_id \}\}-\$\{\{ github\.run_attempt \}\}-\$\{\{ github\.sha \}\}\r?\n)', "`$1          repository: another/repository`r`n", 1)
    $wrongArtifactNameText = [regex]::Replace($prWorkflowText, '(?ms)(  sonar-required:.*?          name: )sonar-inputs-\$\{\{ github\.run_id \}\}-\$\{\{ github\.run_attempt \}\}-\$\{\{ github\.sha \}\}', '${1}sonar-inputs-${{ github.sha }}', 1)
    $wrongDownloadPathText = [regex]::Replace($prWorkflowText, '(?ms)(  sonar-required:.*?          path: )\$\{\{ runner\.temp \}\}/sonar-inputs-\$\{\{ github\.run_id \}\}-\$\{\{ github\.run_attempt \}\}-\$\{\{ github\.sha \}\}', '${1}${{ github.workspace }}', 1)
    $missingReportGlobText = $prWorkflowText.Replace('/d:sonar.cs.vstest.reportsPaths="TestResults/SonarInputs/**/*.trx"', '')
    $missingConsumerBindingText = [regex]::Replace($prWorkflowText, '(?ms)(  sonar-required:.*?)-RunAttempt \$\{\{ github\.run_attempt \}\}\r?\n', '${1}', 1)
    $copyBeforeValidationText = [regex]::Replace($prWorkflowText, '(?ms)(      - name: Validate downloaded Sonar inputs.*?)(      - name: Copy validated Sonar inputs into scanner checkout.*?)(?=      - name: Sonar End)', '$2$1', 1)
    $misorderedBeginText = [regex]::Replace($prWorkflowText, '(?ms)(      - name: Sonar Begin.*?)(      - name: Restore.*?)(?=      - name: Build)', '$2$1', 1)
    $missingWaitText = [regex]::Replace($prWorkflowText, [regex]::Escape($waitProperty), '', 1)
    $duplicateWaitText = [regex]::Replace($prWorkflowText, [regex]::Escape($waitProperty), "$waitProperty $waitProperty", 1)
    $misplacedWaitText = [regex]::Replace($prWorkflowText, 'dotnet sonarscanner end', "dotnet sonarscanner end $waitProperty", 1)
    $pushWaitText = [regex]::Replace($prWorkflowText, '(?s)(\n  sonar-push:.*?dotnet sonarscanner begin [^\r\n]*?)( /d:sonar\.exclusions=)', "`$1 $waitProperty`$2", 1)
    foreach ($fixture in @(
            [pscustomobject]@{ name = 'missing-sonar-required'; text = $missingRequiredText; description = 'missing Sonar job' },
            [pscustomobject]@{ name = 'wrong-sonar-name'; text = $wrongJobNameText; description = 'wrong public job name' },
            [pscustomobject]@{ name = 'missing-needs'; text = $missingNeedsText; description = 'missing final-evidence-gate dependency' },
            [pscustomobject]@{ name = 'removed-draft-guard'; text = $removedDraftGuardText; description = 'removed draft guard' },
            [pscustomobject]@{ name = 'removed-fork-guard'; text = $removedForkGuardText; description = 'removed fork guard' },
            [pscustomobject]@{ name = 'sonar-tests'; text = $sonarTestText; description = 'Sonar-side test execution' },
            [pscustomobject]@{ name = 'sonar-runner'; text = $sonarRunnerText; description = 'Sonar-side PostgreSQL runner' },
            [pscustomobject]@{ name = 'coverage-directory'; text = $coverageDirectoryText; description = 'Sonar-side coverage directory creation' },
            [pscustomobject]@{ name = 'sonar-postgresql'; text = $sonarPostgreSqlText; description = 'Sonar-side PostgreSQL service' },
            [pscustomobject]@{ name = 'cross-run-selector'; text = $crossRunSelectorText; description = 'cross-run artifact selector' },
            [pscustomobject]@{ name = 'wrong-artifact-name'; text = $wrongArtifactNameText; description = 'wrong artifact name' },
            [pscustomobject]@{ name = 'wrong-download-path'; text = $wrongDownloadPathText; description = 'workspace-root artifact download' },
            [pscustomobject]@{ name = 'missing-report-glob'; text = $missingReportGlobText; description = 'missing VSTest report glob' },
            [pscustomobject]@{ name = 'missing-consumer-binding'; text = $missingConsumerBindingText; description = 'missing consumer provenance binding' },
            [pscustomobject]@{ name = 'copy-before-validation'; text = $copyBeforeValidationText; description = 'copy before validation' },
            [pscustomobject]@{ name = 'misordered-begin'; text = $misorderedBeginText; description = 'misordered Begin and restore' },
            [pscustomobject]@{ name = 'missing-wait'; text = $missingWaitText; description = 'missing Quality Gate wait' },
            [pscustomobject]@{ name = 'duplicate-wait'; text = $duplicateWaitText; description = 'duplicate Quality Gate wait' },
            [pscustomobject]@{ name = 'misplaced-wait'; text = $misplacedWaitText; description = 'misplaced Quality Gate wait' },
            [pscustomobject]@{ name = 'push-wait'; text = $pushWaitText; description = 'push Quality Gate wait' })) {
        Assert-True -Condition ($fixture.text -cne $prWorkflowText) -Message "The unified Sonar $($fixture.description) fixture could not mutate the workflow."
        $fixturePath = Join-Path $sonarFixtureRoot.FullName "$($fixture.name).yml"
        [System.IO.File]::WriteAllText($fixturePath, $fixture.text, [System.Text.UTF8Encoding]::new($false))
        Assert-UnifiedSonarFixtureRejected -Path $fixturePath -Description $fixture.description
    }

    $sonarInputsPath = Join-Path $temporaryRoot 'SonarInputsArtifact'
    $happyExitCode = Invoke-FinalEvidenceGate -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead -SonarInputsDirectory $sonarInputsPath
    Assert-True -Condition ($happyExitCode -eq 0) -Message 'The complete run-aware same-SHA artifact fixture failed.'
    Assert-StagedSonarInputs -Path $sonarInputsPath -MergeSha $fixtureHead

    Remove-Item -LiteralPath (Join-Path $temporaryRoot 'Unit/Unit-fixture.trx') -Force
    $missingTrxExitCode = Invoke-FinalEvidenceGate -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead 2>$null
    Assert-True -Condition ($missingTrxExitCode -ne 0) -Message 'The final evidence gate accepted a missing TRX artifact.'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    Remove-Item -LiteralPath (Join-Path $temporaryRoot 'DataSeeder/evidence/trx-summary.json') -Force
    $missingSuiteExitCode = Invoke-FinalEvidenceGate -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead 2>$null
    Assert-True -Condition ($missingSuiteExitCode -ne 0) -Message 'The final evidence gate accepted a missing suite artifact.'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    Remove-Item -LiteralPath (Join-Path $temporaryRoot 'Unit-discovery.json') -Force
    $missingDiscoveryExitCode = Invoke-FinalEvidenceGate -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead 2>$null
    Assert-True -Condition ($missingDiscoveryExitCode -ne 0) -Message 'The final evidence gate accepted a missing referenced discovery artifact.'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    $unitTrxPath = Join-Path $temporaryRoot 'Unit/Unit-fixture.trx'
    [System.IO.File]::WriteAllText($unitTrxPath, '<TestRun', [System.Text.UTF8Encoding]::new($false))
    Update-FixtureTrxMetadata -SummaryPath (Join-Path $temporaryRoot 'Unit/evidence/trx-summary.json') -TrxPath $unitTrxPath
    $malformedTrxExitCode = Invoke-FinalEvidenceGate -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead 2>$null
    Assert-True -Condition ($malformedTrxExitCode -ne 0) -Message 'The final evidence gate accepted malformed TRX XML.'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    [System.IO.File]::AppendAllText((Join-Path $temporaryRoot 'Unit/Unit-fixture.trx'), "`n<!-- hash tamper -->", [System.Text.UTF8Encoding]::new($false))
    $tamperedTrxExitCode = Invoke-FinalEvidenceGate -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead 2>$null
    Assert-True -Condition ($tamperedTrxExitCode -ne 0) -Message 'The final evidence gate accepted a hash-tampered TRX artifact.'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    $counterSummaryPath = Join-Path $temporaryRoot 'Unit/evidence/trx-summary.json'
    $counterSummary = [System.IO.File]::ReadAllText($counterSummaryPath) | ConvertFrom-Json -Depth 32
    $counterSummary.trx.counters.passed = 0
    Write-JsonFixture -Path $counterSummaryPath -Value $counterSummary
    $counterMismatchExitCode = Invoke-FinalEvidenceGate -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead 2>$null
    Assert-True -Condition ($counterMismatchExitCode -ne 0) -Message 'The final evidence gate accepted inconsistent TRX counters.'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    $nameSummaryPath = Join-Path $temporaryRoot 'Unit/evidence/trx-summary.json'
    $nameSummary = [System.IO.File]::ReadAllText($nameSummaryPath) | ConvertFrom-Json -Depth 32
    $nameSummary.trx.testNames = @('Unit.ForgedDisplayName')
    Write-JsonFixture -Path $nameSummaryPath -Value $nameSummary
    $nameMismatchExitCode = Invoke-FinalEvidenceGate -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead 2>$null
    Assert-True -Condition ($nameMismatchExitCode -ne 0) -Message 'The final evidence gate accepted a mismatched TRX display-name multiset.'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    Remove-Item -LiteralPath (Join-Path $temporaryRoot 'Unit/coverage.opencover.xml') -Force
    Assert-EvidenceGateRejectedWithoutPublish -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead -SonarInputsDirectory (Join-Path $temporaryRoot 'SonarInputs-missing-coverage') -Description 'missing coverage'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    $missingCoverageMetadataPath = Join-Path $temporaryRoot 'Unit/evidence/trx-summary.json'
    $missingCoverageMetadata = [System.IO.File]::ReadAllText($missingCoverageMetadataPath) | ConvertFrom-Json -Depth 32
    $missingCoverageMetadata.PSObject.Properties.Remove('coverage')
    Write-JsonFixture -Path $missingCoverageMetadataPath -Value $missingCoverageMetadata
    Assert-EvidenceGateRejectedWithoutPublish -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead -SonarInputsDirectory (Join-Path $temporaryRoot 'SonarInputs-missing-coverage-metadata') -Description 'missing coverage metadata'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    [System.IO.File]::WriteAllText((Join-Path $temporaryRoot 'Unit/coverage.opencover.xml'), '', [System.Text.UTF8Encoding]::new($false))
    Assert-EvidenceGateRejectedWithoutPublish -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead -SonarInputsDirectory (Join-Path $temporaryRoot 'SonarInputs-empty-coverage') -Description 'empty coverage'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    $malformedCoveragePath = Join-Path $temporaryRoot 'Unit/coverage.opencover.xml'
    [System.IO.File]::WriteAllText($malformedCoveragePath, '<CoverageSession', [System.Text.UTF8Encoding]::new($false))
    Update-FixtureCoverageMetadata -SummaryPath (Join-Path $temporaryRoot 'Unit/evidence/trx-summary.json') -CoveragePath $malformedCoveragePath
    Assert-EvidenceGateRejectedWithoutPublish -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead -SonarInputsDirectory (Join-Path $temporaryRoot 'SonarInputs-malformed-coverage') -Description 'malformed coverage'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    [System.IO.File]::AppendAllText((Join-Path $temporaryRoot 'Unit/coverage.opencover.xml'), "`n<!-- hash tamper -->", [System.Text.UTF8Encoding]::new($false))
    Assert-EvidenceGateRejectedWithoutPublish -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead -SonarInputsDirectory (Join-Path $temporaryRoot 'SonarInputs-tampered-coverage') -Description 'tampered coverage'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    $duplicateCoverageDirectory = New-Item -ItemType Directory -Path (Join-Path $temporaryRoot 'Unit/duplicate') -Force
    Copy-Item -LiteralPath (Join-Path $temporaryRoot 'Unit/coverage.opencover.xml') -Destination (Join-Path $duplicateCoverageDirectory.FullName 'coverage.opencover.xml')
    Assert-EvidenceGateRejectedWithoutPublish -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead -SonarInputsDirectory (Join-Path $temporaryRoot 'SonarInputs-duplicate-coverage') -Description 'duplicate coverage'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    $sixthCoverageDirectory = New-Item -ItemType Directory -Path (Join-Path $temporaryRoot 'Unexpected') -Force
    Copy-Item -LiteralPath (Join-Path $temporaryRoot 'Unit/coverage.opencover.xml') -Destination (Join-Path $sixthCoverageDirectory.FullName 'coverage.opencover.xml')
    Assert-EvidenceGateRejectedWithoutPublish -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead -SonarInputsDirectory (Join-Path $temporaryRoot 'SonarInputs-sixth-coverage') -Description 'unexpected sixth coverage'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    $wrongSuiteSummaryPath = Join-Path $temporaryRoot 'Unit/evidence/trx-summary.json'
    $wrongSuiteSummary = [System.IO.File]::ReadAllText($wrongSuiteSummaryPath) | ConvertFrom-Json -Depth 32
    $wrongSuiteSummary.coverage.path = Join-Path $temporaryRoot 'WrongSuite/coverage.opencover.xml'
    Write-JsonFixture -Path $wrongSuiteSummaryPath -Value $wrongSuiteSummary
    Assert-EvidenceGateRejectedWithoutPublish -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead -SonarInputsDirectory (Join-Path $temporaryRoot 'SonarInputs-wrong-suite') -Description 'wrong suite coverage metadata'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    $wrongCoverageShaSummaryPath = Join-Path $temporaryRoot 'Unit/evidence/trx-summary.json'
    $wrongCoverageShaSummary = [System.IO.File]::ReadAllText($wrongCoverageShaSummaryPath) | ConvertFrom-Json -Depth 32
    $wrongCoverageShaSummary.coverage.sha256 = '0' * 64
    Write-JsonFixture -Path $wrongCoverageShaSummaryPath -Value $wrongCoverageShaSummary
    Assert-EvidenceGateRejectedWithoutPublish -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead -SonarInputsDirectory (Join-Path $temporaryRoot 'SonarInputs-wrong-coverage-sha') -Description 'wrong coverage SHA metadata'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    Assert-EvidenceGateRejectedWithoutPublish -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead -RunId 'not-a-run-id' -SonarInputsDirectory (Join-Path $temporaryRoot 'SonarInputs-wrong-run') -Description 'wrong run metadata'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    Assert-EvidenceGateRejectedWithoutPublish -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead -RunId '0440001' -SonarInputsDirectory (Join-Path $temporaryRoot 'SonarInputs-noncanonical-run') -Description 'noncanonical run metadata'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    Assert-EvidenceGateRejectedWithoutPublish -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead -RunAttempt '02' -SonarInputsDirectory (Join-Path $temporaryRoot 'SonarInputs-noncanonical-attempt') -Description 'noncanonical run-attempt metadata'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    Assert-EvidenceGateRejectedWithoutPublish -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead -Repository 'lgym/LGYM-APP-APIv3/invalid' -SonarInputsDirectory (Join-Path $temporaryRoot 'SonarInputs-wrong-repository') -Description 'wrong repository metadata'

    New-EvidenceFixture -Root $temporaryRoot -Head $fixtureHead -RepositorySourcePath $evidenceGatePath
    $existingOutputPath = Join-Path $temporaryRoot 'SonarInputs-existing'
    $null = New-Item -ItemType Directory -Path $existingOutputPath -Force
    [System.IO.File]::WriteAllText((Join-Path $existingOutputPath 'sentinel.txt'), 'do not overwrite', [System.Text.UTF8Encoding]::new($false))
    $existingOutputExitCode = Invoke-FinalEvidenceGate -EvidenceGatePath $evidenceGatePath -EvidenceRoot $temporaryRoot -MergeSha $fixtureHead -SonarInputsDirectory $existingOutputPath 2>$null
    Assert-True -Condition ($existingOutputExitCode -ne 0) -Message 'The final evidence gate accepted an existing Sonar input destination.'
    Assert-True -Condition ([System.IO.File]::ReadAllText((Join-Path $existingOutputPath 'sentinel.txt')) -ceq 'do not overwrite') -Message 'The final evidence gate overwrote the existing Sonar input destination.'

    Write-Host 'CI workflow fixture matrix passed: yaml=1, events=7, unified-sonar-happy=1, sonar-consumer-binding-pr-push=2, sonar-mutations=20, compatibility-happy=1, compatibility-missing=1, compatibility-miswired=1, compatibility-permissive=1, task4-producer-happy=1, task4-push-nullability-happy=1, task4-push-nullability-rejected=1, task4-duplicate-suite-execution=1, task4-missing-suite=1, task4-missing-coverage=1, task4-second-postgresql-runner=1, task4-sha-only-artifact=1, task4-nonfatal-upload=1, task4-broken-provenance=1, task4-artifact-layout=1, evidence-happy=1, staging-files=11, consumer-root-shape=1, manifest-run-types=string, missing-trx=1, missing-artifact=1, missing-discovery=1, malformed-trx=1, hash-tamper=1, counter-mismatch=1, name-mismatch=1, missing-coverage=1, missing-coverage-metadata=1, empty-coverage=1, malformed-coverage=1, tampered-coverage=1, duplicate-coverage=1, sixth-coverage=1, wrong-suite-coverage=1, wrong-coverage-sha=1, wrong-run=1, noncanonical-run=1, noncanonical-run-attempt=1, wrong-repository=1, existing-output=1, no-partial-publish=13.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
