# Releases are built by GitHub (.github/workflows/release.yml), never on a PC: pushing to main a VERSION that has no
# v<VERSION> release yet tests, builds, attests and publishes the release with its tag.
#   .\scripts\tag_release.ps1             checks, before the push, that the version is ready to release
#   .\scripts\tag_release.ps1 -Download   downloads the files of the release v<VERSION> into dist\release and checks
#                                         their attestation: the MSIX there is the one to submit to the Store
param([switch]$Download)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$version = & (Join-Path $PSScriptRoot 'read_version.ps1')
$tag = "v$version"
$repository = 'pironjulien/NexusWpp'

if ($Download) {
    $target = Join-Path $project 'dist\release'
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    & gh release download $tag --repo $repository --dir $target --clobber
    if ($LASTEXITCODE -ne 0) { throw "Release $tag not found: is its Release workflow finished?" }
    foreach ($file in Get-ChildItem -LiteralPath $target -File | Where-Object { $_.Extension -in '.msix', '.exe' }) {
        & gh attestation verify $file.FullName --repo $repository | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Attestation of $($file.Name) failed." }
        Write-Host "Attested: $($file.Name)" -ForegroundColor Green
    }
    $msix = Get-ChildItem -LiteralPath $target -Filter "*_${version}_x64.msix" | Select-Object -First 1
    if (!$msix) { throw "Release $tag has no MSIX." }
    Write-Host "Store package to submit: $($msix.FullName)" -ForegroundColor Green
    return
}

& gh release view $tag --repo $repository --json tagName 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) { throw "Release $tag already exists: raise VERSION first." }
$dirty = & git -C $project status --porcelain --untracked-files=no
if ($dirty) { throw "Uncommitted changes: commit them before releasing $tag." }
$section = '^## .+ — ' + [regex]::Escape($version) + '$'
if (-not (Get-Content -LiteralPath (Join-Path $project 'CHANGELOG.md') -Encoding utf8 | Where-Object { $_ -match $section })) {
    throw "CHANGELOG.md has no '## <date> — $version' section (its notes become the release notes)."
}
Write-Host "$tag is ready: push main, the Release workflow publishes it." -ForegroundColor Green
