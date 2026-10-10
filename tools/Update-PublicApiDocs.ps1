<#
.SYNOPSIS
    Writes docs/public-api/<Package>.txt: every public member of each package under src/, one per line.

.DESCRIPTION
    Builds the packages with the public API analyzer (Microsoft.CodeAnalysis.PublicApiAnalyzers) and empty API lists,
    so it reports every public member (RS0016), and writes each member's API line from the project's SARIF log. The
    lines are the analyzer's own format: `!` marks a non-nullable reference, `?` a nullable one. The packages' normal
    build doesn't run the analyzer: the docs are a reference, regenerated when the public API changes, and the diff
    shows what changed.

.EXAMPLE
    pwsh tools/Update-PublicApiDocs.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("public-api-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

try
{
    # Empty lists: everything public is "not declared", so the analyzer reports it.
    Set-Content -Path (Join-Path $work 'PublicAPI.Shipped.txt') -Value '#nullable enable' -Encoding utf8
    Set-Content -Path (Join-Path $work 'PublicAPI.Unshipped.txt') -Value '#nullable enable' -Encoding utf8

    $solution = Join-Path $root 'Resrcify.SharedKernel.slnx'
    & dotnet build $solution `
        -p:PublicApiReport=true `
        "-p:PublicApiReportDir=$work" `
        -p:TreatWarningsAsErrors=false `
        -p:CodeAnalysisTreatWarningsAsErrors=false `
        --nologo -v quiet | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

    $docs = Join-Path $root 'docs/public-api'
    New-Item -ItemType Directory -Force -Path $docs | Out-Null
    $projects = Get-ChildItem -Path (Join-Path $root 'src') -Filter *.csproj -Recurse
    foreach ($project in $projects)
    {
        $name = $project.BaseName
        $sarif = Join-Path $work "$name.sarif"
        if (-not (Test-Path $sarif)) { throw "No SARIF log for $name" }

        $log = Get-Content -Raw -Path $sarif | ConvertFrom-Json
        $set = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
        foreach ($run in $log.runs)
        {
            foreach ($result in $run.results)
            {
                if ($result.ruleId -eq 'RS0016') { [void]$set.Add($result.properties.customProperties.APIName) }
            }
        }
        # Ordinal order: the same file on every machine, so a diff shows only API changes.
        $lines = [string[]]@($set)

        $target = Join-Path $docs "$name.txt"
        [System.IO.File]::WriteAllText($target, (($lines -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
        Write-Host ("{0,5} {1}" -f $lines.Count, $name)
    }
}
finally
{
    Remove-Item -Recurse -Force -Path $work -ErrorAction SilentlyContinue
}
