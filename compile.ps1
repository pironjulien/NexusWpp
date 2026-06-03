$ErrorActionPreference = "Stop"

$binDir = Join-Path $PSScriptRoot "bin"
$buildTempDir = Join-Path $env:TEMP "wv2_build_temp"
$nugetUrl = "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.2592.51/microsoft.web.webview2.1.0.2592.51.nupkg"
$zipPath = Join-Path $buildTempDir "webview2.zip"
$extractPath = Join-Path $buildTempDir "webview2_package"

# Check if DLLs already exist
$coreDllDest = Join-Path $binDir "Microsoft.Web.WebView2.Core.dll"
$winFormsDllDest = Join-Path $binDir "Microsoft.Web.WebView2.WinForms.dll"
$loaderDllDest = Join-Path $binDir "WebView2Loader.dll"

$hasDlls = (Test-Path $coreDllDest) -and (Test-Path $winFormsDllDest) -and (Test-Path $loaderDllDest)

if (-not $hasDlls) {
    # 1. Ensure clean build environment
    if (!(Test-Path $binDir)) { New-Item -ItemType Directory -Path $binDir -Force | Out-Null }
    if (Test-Path $buildTempDir) {
        try {
            Remove-Item $buildTempDir -Recurse -Force
        } catch {
            Write-Host "Warning: Could not clean temp_build: $_" -ForegroundColor Yellow
        }
    }
    if (!(Test-Path $buildTempDir)) { New-Item -ItemType Directory -Path $buildTempDir -Force | Out-Null }

    # 2. Download WebView2 NuGet Package
    Write-Host "Downloading Microsoft.Web.WebView2 NuGet package from official v3 flatcontainer..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $nugetUrl -OutFile $zipPath

    # 3. Extract Package
    Write-Host "Extracting WebView2 package..." -ForegroundColor Cyan
    Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force

    # 4. stage references to output binary directory
    Write-Host "Staging build references and DLLs..." -ForegroundColor Cyan
    $coreDll = Join-Path $extractPath "lib\net462\Microsoft.Web.WebView2.Core.dll"
    $winFormsDll = Join-Path $extractPath "lib\net462\Microsoft.Web.WebView2.WinForms.dll"
    $loaderDll = Join-Path $extractPath "build\native\x64\WebView2Loader.dll"

    Copy-Item $coreDll -Destination $binDir -Force
    Copy-Item $winFormsDll -Destination $binDir -Force
    Copy-Item $loaderDll -Destination $binDir -Force
    
    try {
        Remove-Item $buildTempDir -Recurse -Force -ErrorAction SilentlyContinue | Out-Null
    } catch {}
} else {
    Write-Host "WebView2 DLLs already present in bin/. Skipping NuGet package download and extraction." -ForegroundColor Green
}

# 5. Compile C# Host code using system csc.exe
Write-Host "Compiling DesktopHtmlHost.cs into nexuswpp.exe..." -ForegroundColor Cyan
$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$sourceFile = Join-Path $PSScriptRoot "DesktopHtmlHost.cs"
$outputExe = Join-Path $binDir "nexuswpp.exe"

$compilerArgs = @(
    "/target:winexe",
    "/out:$outputExe",
    "/win32icon:$(Join-Path $PSScriptRoot "icon.ico")",
    "/reference:$(Join-Path $binDir Microsoft.Web.WebView2.Core.dll)",
    "/reference:$(Join-Path $binDir Microsoft.Web.WebView2.WinForms.dll)",
    "/reference:System.Management.dll",
    $sourceFile
)

$process = Start-Process -FilePath $cscPath -ArgumentList $compilerArgs -NoNewWindow -Wait -PassThru

if ($process.ExitCode -eq 0 -and (Test-Path $outputExe)) {
    Write-Host "`n[SUCCESS] nexuswpp.exe has been compiled successfully!" -ForegroundColor Green
    Write-Host "Location: $outputExe" -ForegroundColor Green
    
    # 6. Clean up temporary package folder
    if (Test-Path $buildTempDir) {
        Remove-Item $buildTempDir -Recurse -Force | Out-Null
    }
} else {
    if (Test-Path $buildTempDir) {
        Remove-Item $buildTempDir -Recurse -Force -ErrorAction SilentlyContinue | Out-Null
    }
    Write-Error "Compilation failed. csc.exe exited with code $($process.ExitCode)."
}
