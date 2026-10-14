# Contrôle complet du projet, hors ligne et sans effet de bord (convention Nexus : test.ps1 à la racine).
# Mêmes étapes que la CI : compilation puis tests de régression.
$ErrorActionPreference = 'Stop'
$steps = @(
    @('compile.ps1'),
    @('scripts\test_features.ps1'),
    @('scripts\test_settings.ps1'),
    @('scripts\test_runtime_pause.ps1'),
    @('scripts\test_responsive_layout.ps1', '-Quick', '-Label', 'local')
)
foreach ($step in $steps) {
    $script, $arguments = $step
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot $script) @arguments
    if ($LASTEXITCODE) { throw "$script a échoué (code $LASTEXITCODE)." }
}
