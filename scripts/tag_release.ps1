# Creates the annotated git tag v<VERSION> on the current commit, so a Store or installer
# release can be traced back to its source. The tag stays local: push it yourself with
#   git push origin v<VERSION>
param([string]$Message)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$version = & (Join-Path $PSScriptRoot 'read_version.ps1')
$tag = "v$version"

& git -C $project rev-parse --verify --quiet "refs/tags/$tag" | Out-Null
if ($LASTEXITCODE -eq 0) { throw "Tag $tag already exists." }
$dirty = & git -C $project status --porcelain --untracked-files=no
if ($dirty) { throw "Uncommitted changes: commit them before tagging $tag." }
if (-not (Select-String -LiteralPath (Join-Path $project 'CHANGELOG.md') -SimpleMatch $version -Quiet)) {
    throw "CHANGELOG.md does not mention $version."
}

if (-not $Message) { $Message = "NexusWpp $version" }
& git -C $project tag -a $tag -m $Message
if ($LASTEXITCODE -ne 0) { throw "git tag failed with exit code $LASTEXITCODE." }
Write-Host "Created tag $tag on $(& git -C $project rev-parse --short HEAD). Push it with: git push origin $tag" -ForegroundColor Green
