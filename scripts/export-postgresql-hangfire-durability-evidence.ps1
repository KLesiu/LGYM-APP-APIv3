param(
    [Parameter(Mandatory)]
    [string]$TrxPath,
    [Parameter(Mandatory)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $TrxPath -PathType Leaf)) {
    throw "The PostgreSQL Hangfire durability TRX does not exist."
}

$settings = [System.Xml.XmlReaderSettings]::new()
$settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
$settings.XmlResolver = $null
$reader = [System.Xml.XmlReader]::Create($TrxPath, $settings)
try {
    $document = [System.Xml.XmlDocument]::new()
    $document.XmlResolver = $null
    $document.Load($reader)
}
finally {
    $reader.Dispose()
}

$namespace = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
$namespace.AddNamespace("trx", $document.DocumentElement.NamespaceURI)
$counters = $document.SelectSingleNode("/trx:TestRun/trx:ResultSummary/trx:Counters", $namespace)
if ($null -eq $counters) {
    throw "The PostgreSQL Hangfire durability TRX does not contain counters."
}

$total = [int]$counters.GetAttribute("total")
$passed = [int]$counters.GetAttribute("passed")
$failed = [int]$counters.GetAttribute("failed")
$notExecuted = [int]$counters.GetAttribute("notExecuted")
if ($total -eq 0 -or $passed -ne $total -or $failed -ne 0 -or $notExecuted -ne 0) {
    throw "The PostgreSQL Hangfire durability TRX is not a nonempty passing result."
}

$result = $document.SelectSingleNode("/trx:TestRun/trx:Results/trx:UnitTestResult[contains(@testName, 'PostgreSqlHangfire_PersistsRestartRetryAndIdempotentReplayWithoutExternalProviders')]", $namespace)
if ($null -eq $result) {
    throw "The PostgreSQL Hangfire durability test result is absent from the TRX."
}

$output = $result.SelectSingleNode("trx:Output/trx:StdOut", $namespace)
if ($null -eq $output -or $output.InnerText -notmatch "Hangfire durability evidence:") {
    $output = $document.SelectSingleNode("/trx:TestRun/trx:ResultSummary/trx:Output/trx:StdOut", $namespace)
}
if ($null -eq $output -or $output.InnerText -notmatch "Hangfire durability evidence:") {
    throw "The PostgreSQL Hangfire durability lifecycle evidence is absent from the TRX."
}

$lifecycleMatch = [regex]::Match($output.InnerText, '(?m)^Hangfire durability evidence:.*$')
if (-not $lifecycleMatch.Success) {
    throw "The PostgreSQL Hangfire durability lifecycle marker could not be isolated from the TRX output."
}

$lifecycle = [regex]::Replace(
    $lifecycleMatch.Value.Trim(),
    '(?i)\b(host|server|database|username|user id|password|pwd)\s*=\s*[^;\s]+',
    '$1=[redacted]')

$outputParent = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputParent) -and -not (Test-Path -LiteralPath $outputParent -PathType Container)) {
    New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
}

@(
    "# PostgreSQL Hangfire Durability Evidence",
    "",
    "- TRX: $([System.IO.Path]::GetFileName($TrxPath))",
    "- Counters: total=$total, passed=$passed, failed=$failed, notExecuted=$notExecuted",
    "- Lifecycle: $lifecycle"
) | Set-Content -LiteralPath $OutputPath -Encoding utf8
