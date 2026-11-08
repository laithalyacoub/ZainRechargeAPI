# Renames the evidence screenshots and writes evidence/README.md. Run from C:\dev\zain\ZainRechargeAPI
$ErrorActionPreference = "Stop"
if (-not (Test-Path "evidence")) { throw "Wrong folder. cd C:\dev\zain\ZainRechargeAPI first." }
$map = [ordered]@{
  "125051" = "07-prefix-00962-response"
  "125314" = "02-recharge-25-response"
  "125437" = "03-recharge-negative-response"
  "125625" = "04-provider-503-response"
  "125726" = "05-provider-timeout-response"
  "130015" = "06-history-response"
  "143533" = "00-seq-both-apis-running"
  "144024" = "01-recharge-5-seq-flow"
  "144407" = "01-recharge-5-seq-correlation-id"
  "144539" = "01-recharge-5-seq-transaction-search"
  "163727" = "01-recharge-5-postman-response"
  "164503" = "10-postman-provider-401"
  "165241" = "09-tests-provider"
  "165332" = "09-tests-zain"
}
foreach ($key in $map.Keys) {
  $old = "evidence\Screenshot 2026-11-08 $key.png"
  if (Test-Path $old) {
    Rename-Item $old ($map[$key] + ".png")
    Write-Host "renamed $key -> $($map[$key]).png"
  } else {
    Write-Warning "not found: $old"
  }
}
$utf8 = New-Object System.Text.UTF8Encoding $false
$text = @'
# Evidence

Screenshots of the six mandatory scenarios and of the trainer's extra requirements. Every file name starts with the scenario number.

| Scenario | File | What it shows |
|---|---|---|
| 1. Recharge 5 JOD | 01-recharge-5-postman-response.png | Postman: 201 Created, status SUCCESS, generated TXN id, PAY reference, both Postman tests passed |
| 1. Recharge 5 JOD | 01-recharge-5-seq-flow.png | Seq: the whole flow of one recharge across both APIs (PENDING, token, payment, SUCCESS, 201) |
| 1. Recharge 5 JOD | 01-recharge-5-seq-correlation-id.png | Seq: one log line opened, showing the CorrelationId property |
| 1. Recharge 5 JOD | 01-recharge-5-seq-transaction-search.png | Seq: search for the transaction id shows the provider lines (number masked as 0791***567) |
| 2. Recharge 25 JOD | 02-recharge-25-response.png | 200 with status FAILED and the failure reason (limit of 20 JOD) |
| 3. Recharge -5 JOD | 03-recharge-negative-response.png | 400 with the error on amount; nothing is saved |
| 4. Provider unavailable | 04-provider-503-response.png | 503 with Retry-After header; the recharge is FAILED because the payment was not attempted |
| 5. Provider timeout | 05-provider-timeout-response.png | 202 with status UNKNOWN and a Location header to check the result later |
| 6. History | 06-history-response.png | 200, newest first, with totalCount and paging |
| Trainer rule: 00962 | 07-prefix-00962-response.png | Sent 00962791234567, answered and stored as 0791234567 |
| Provider security | 10-postman-provider-401.png | Provider answers 401 when the X-Api-Key is missing |
| Unit tests | 09-tests-zain.png | dotnet test in the Zain solution: 40 passed, 0 failed |
| Unit tests | 09-tests-provider.png | dotnet test in the provider solution: 9 passed, 0 failed |
| Logging | 00-seq-both-apis-running.png | Seq receiving logs from both APIs (ports 5200 and 5100) |

The database script is in docs/schema.sql and the Postman collection is in the postman folder.

'@
[System.IO.File]::WriteAllText((Join-Path (Get-Location) "evidence\README.md"), $text.Replace("`r`n","`n").Replace("`n","`r`n") + "`r`n", $utf8)
Write-Host "created evidence/README.md"
Write-Host "Now run: git add .   then   git commit -m `"docs: name and index the evidence screenshots`""
