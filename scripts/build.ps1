param(
    [string]$Dotnet = 'dotnet',
    [string]$JavaHome = '',
    [string]$AndroidSdk = '',
    [switch]$Interop
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$previousJavaHome = $env:JAVA_HOME
$previousAndroidHome = $env:ANDROID_HOME
Push-Location $projectRoot
try {
    if ($Dotnet -eq 'dotnet' -and (Test-Path -LiteralPath '.tools\dotnet\dotnet.exe')) {
        $Dotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
    }
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    if ($JavaHome) { $env:JAVA_HOME = $JavaHome }
    elseif (Get-Command java -ErrorAction SilentlyContinue) {
        # Prefer the Java executable on PATH when JAVA_HOME points at an older JDK.
        $javaExecutable = (Get-Command java).Source
        $detectedJavaHome = Split-Path -Parent (Split-Path -Parent $javaExecutable)
        if (Test-Path -LiteralPath (Join-Path $detectedJavaHome 'bin\javac.exe')) { $env:JAVA_HOME = $detectedJavaHome }
    }
    if ($AndroidSdk) { $env:ANDROID_HOME = $AndroidSdk }
    elseif (Test-Path -LiteralPath '.tools\android-sdk') { $env:ANDROID_HOME = Join-Path $projectRoot '.tools\android-sdk' }
    & $Dotnet test LegacyDisplay.sln --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Windows tests failed' }
    & $Dotnet build windows-studio\LegacyDisplay.Studio --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Studio build failed' }
    New-Item -ItemType Directory -Force -Path artifacts | Out-Null
    & $Dotnet publish windows-agent\LegacyDisplay.Agent -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts\agent --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Agent publish failed' }
    Push-Location android-client
    try {
        & .\gradlew.bat :app:assembleDebug :app:lintDebug :app:writePocClasspath --console=plain
        if ($LASTEXITCODE -ne 0) { throw 'Android build/tests/lint failed' }
    } finally { Pop-Location }
    if ($Interop) {
        $javaExecutable = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin\java.exe' } else { 'java' }
        & python scripts/test-interop.py --dotnet $Dotnet --java $javaExecutable
        if ($LASTEXITCODE -ne 0) { throw 'Interoperability check failed' }
    }
    Copy-Item -LiteralPath android-client\app\build\outputs\apk\debug\app-debug.apk -Destination artifacts\LegacyDisplay-v0.2-debug.apk
    & $Dotnet publish windows-studio\LegacyDisplay.Studio -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts\studio --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Studio publish failed' }
    & python scripts/collect-notices.py
    if ($LASTEXITCODE -ne 0) { throw 'Third-party notices collection failed' }
    Copy-Item -LiteralPath LICENSE -Destination artifacts\agent\LICENSE.txt
    Copy-Item -LiteralPath LICENSE -Destination artifacts\studio\LICENSE.txt
    Copy-Item -LiteralPath docs\INTEGRATIONS.md -Destination artifacts\studio\INTEGRATIONS.md
    Copy-Item -LiteralPath docs\STUDIO.md -Destination artifacts\studio\LEIA-ME.md
    Copy-Item -LiteralPath docs\ACTIONS.md -Destination artifacts\studio\ACTIONS.md
    Copy-Item -LiteralPath docs\MININGROUTINE.md -Destination artifacts\studio\MININGROUTINE.md
    New-Item -ItemType Directory -Force -Path artifacts\studio\layouts | Out-Null
    Copy-Item -LiteralPath protocol\examples\dashboard.json,protocol\examples\dashboard-gpu.json,protocol\examples\dashboard-mining.json -Destination artifacts\studio\layouts
    $studioSmoke = Start-Process -FilePath (Join-Path $projectRoot 'artifacts\studio\LegacyDisplay.Studio.exe') -ArgumentList @('--smoke-test', ('"{0}"' -f (Join-Path $projectRoot 'artifacts\Studio-preview.png'))) -WindowStyle Hidden -Wait -PassThru
    if ($studioSmoke.ExitCode -ne 0) { throw 'Published Studio smoke test failed' }
    $actionsSmoke = Start-Process -FilePath (Join-Path $projectRoot 'artifacts\studio\LegacyDisplay.Studio.exe') -ArgumentList @('--actions-smoke-test', ('"{0}"' -f (Join-Path $projectRoot 'artifacts\Actions-preview.png'))) -WindowStyle Hidden -Wait -PassThru
    if ($actionsSmoke.ExitCode -ne 0) { throw 'Published actions editor smoke test failed' }
    Compress-Archive -LiteralPath artifacts\agent,artifacts\studio -DestinationPath artifacts\LegacyDisplay-Windows.zip -Force
} finally {
    $env:JAVA_HOME = $previousJavaHome
    $env:ANDROID_HOME = $previousAndroidHome
    Pop-Location
}
