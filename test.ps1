# Contrôle complet du projet, hors ligne et sans effet de bord (convention Nexus : test.ps1 à la racine).
# Mêmes étapes que la CI : compilation puis tests (JavaScript et xUnit, fenêtres hors écran jamais activées).
# Un seul passage à la fois par copie du dépôt : deux agents partageraient sinon les binaires et profils WebView2 de work\.
# Barrière de tests Nexus : sur un poste Nexus, le contrôle passe par ~\.nexus\git\Invoke-NexusTest.ps1 (NexusInfra), qui
# note l'arbre testé s'il réussit ; le crochet pre-push refuse un arbre poussé qui n'a pas réussi ce script.
if (-not $env:NEXUS_TEST_EMPREINTE -and (Test-Path -LiteralPath "$HOME\.nexus\git\Invoke-NexusTest.ps1")) { & "$HOME\.nexus\git\Invoke-NexusTest.ps1" -Script $PSCommandPath -Parametres $PSBoundParameters -Arguments $args; exit $LASTEXITCODE }
$ErrorActionPreference = 'Stop'
$testLock = & (Join-Path $PSScriptRoot 'scripts\test_lock.ps1') 'test'
try {
    foreach ($script in @('compile.ps1', 'scripts\test_features.ps1')) {
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot $script)
        if ($LASTEXITCODE) { throw "$script a échoué (code $LASTEXITCODE)." }
    }
} finally { $testLock.Dispose() }
