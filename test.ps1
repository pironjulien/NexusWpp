# Contrôle complet du projet, hors ligne et sans effet de bord (convention Nexus : test.ps1 à la racine).
# Mêmes étapes que la CI : compilation puis tests (JavaScript et xUnit, fenêtres hors écran jamais activées).
# Un seul passage à la fois par copie du dépôt : deux agents partageraient sinon les binaires et profils WebView2 de work\.
$ErrorActionPreference = 'Stop'
$testLock = & (Join-Path $PSScriptRoot 'scripts\test_lock.ps1') 'test'
try {
    foreach ($script in @('compile.ps1', 'scripts\test_features.ps1')) {
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot $script)
        if ($LASTEXITCODE) { throw "$script a échoué (code $LASTEXITCODE)." }
    }
} finally { $testLock.Dispose() }
