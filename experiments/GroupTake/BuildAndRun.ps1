param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug', [switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'Visual Studio MSBuild with Windows App SDK build tools is required.' }
$project = Join-Path $repoRoot 'src/GroupTake.Poc.WinUI/GroupTake.Poc.WinUI.csproj'
& $msbuild $project /restore /p:Configuration=$Configuration /p:Platform=x64 /m /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }
if (-not $BuildOnly) {
    $exe = Join-Path $repoRoot "src/GroupTake.Poc.WinUI/bin/x64/$Configuration/net10.0-windows10.0.19041.0/win-x64/GroupTake.Poc.WinUI.exe"
    # Same unpackaged Windows App SDK self-contained deployment as PhotoCuller; .NET 10 runtime required.
    $appProcess = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
    Write-Host "GroupTake POC launched (PID: $($appProcess.Id)). Verify its top-level window."
}
