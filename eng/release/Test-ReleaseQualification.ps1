#Requires -Version 7.4
<#
.SYNOPSIS
Checks the Checkpoint F qualification gate of a release: matrix rows Q02-Q10, Q40 and Q41 qualified for the released
tree, or waived in the release notes.

.DESCRIPTION
Reads docs/qualification/matrix.json (cheatengine-qualification-matrix/v0) of the released tree and evaluates each
gating parent row: it passes when every level of its requiredLevels is Passed, or NotApplicable with a justification,
and every Passed C3/C4 cell names the released tree or carries a transfer justification. A row that does not pass is
waived when the release notes (the CHANGELOG section of the version) list it under a '### Qualification waivers'
heading as '- Qxx: <reason>'.

Enforce (stable tags) fails on any open row; Report (prereleases and dry runs) lists the open rows in a notice and
succeeds. A missing matrix leaves every row open. C1/C2 evidence is counted only where a row requires C1/C2; it never
stands in for a required C3/C4 level.

.EXAMPLE
./eng/release/Test-ReleaseQualification.ps1 -MatrixPath docs/qualification/matrix.json -TreeHash (git rev-parse 'HEAD^{tree}') `
  -ReleaseNotesPath artifacts/release-notes.md -Mode Enforce
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $MatrixPath,
  [Parameter(Mandatory)] [string] $TreeHash,
  [string] $ReleaseNotesPath = '',
  [Parameter(Mandatory)] [ValidateSet('Enforce', 'Report')] [string] $Mode
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force

# One line per failure: readable in any console width and an annotation on the GitHub run.
trap {
  Write-Host "::error::$($_.Exception.Message)"
  exit 1
}

$MatrixSchema = 'cheatengine-qualification-matrix/v0'
if ($TreeHash -cnotmatch '^[0-9a-f]{40}$') { throw "-TreeHash '$TreeHash' is not a 40-hex git tree id." }

$matrixFile = [IO.Path]::GetFullPath($MatrixPath, $PWD.Path)
$matrix = $null
if (Test-Path -LiteralPath $matrixFile -PathType Leaf) {
  $matrix = Get-Content -LiteralPath $matrixFile -Raw | ConvertFrom-Json
  $schema = [string](Get-JsonValue -InputObject $matrix -Name 'schema')
  if ($schema -cne $MatrixSchema) { throw "$MatrixPath declares schema '$schema', expected $MatrixSchema." }
}

$notes = ''
if ($ReleaseNotesPath) {
  $notesFile = [IO.Path]::GetFullPath($ReleaseNotesPath, $PWD.Path)
  if (-not (Test-Path -LiteralPath $notesFile -PathType Leaf)) { throw "The release notes '$ReleaseNotesPath' do not exist." }
  $notes = [IO.File]::ReadAllText($notesFile)
}

$report = Get-ReleaseQualificationReport -Matrix $matrix -TreeHash $TreeHash -ReleaseNotes $notes
$open = @($report | Where-Object { $_.State -ceq 'Open' })

$lines = [Collections.Generic.List[string]]::new()
$lines.Add("### Qualification gate ($Mode)")
$lines.Add('')
$lines.Add("Released tree ``$TreeHash``. Rows Q02-Q10, Q40 and Q41 must pass at every required level or be waived in the release notes (RELEASING.md).")
$lines.Add('')
$lines.Add('| Row | State | Reason |')
$lines.Add('| --- | --- | --- |')
foreach ($row in $report) { $lines.Add("| $($row.Id) | $($row.State) | $($row.Reason -replace '\|', '\|') |") }
$lines.Add('')
$lines | ForEach-Object { Write-Host $_ }
if ($env:GITHUB_STEP_SUMMARY) { $lines | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8 }

if ($open.Count -eq 0) {
  Write-Host "Qualification gate: every gating row passes or is waived."
  exit 0
}

$ids = ($open | ForEach-Object { $_.Id }) -join ', '
if ($Mode -ceq 'Enforce') {
  Write-Host "::error::A stable release requires qualification of $ids for tree $TreeHash, or a '- Qxx: <reason>' line per row under '### Qualification waivers' in the CHANGELOG section of the version (RELEASING.md)."
  exit 1
}
Write-Host "::notice title=Qualification gate::Open rows $ids; a stable release would need them qualified or waived."
exit 0
