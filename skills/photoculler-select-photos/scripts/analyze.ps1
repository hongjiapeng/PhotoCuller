[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $PhotoDirectory,

    [string] $OutputPath,

    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'

if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)) {
    throw 'The current PhotoCuller CLI requires the Windows OpenCV native runtime.'
}

$resolvedPhotoDirectory = [System.IO.Path]::GetFullPath($PhotoDirectory)
if (-not [System.IO.Directory]::Exists($resolvedPhotoDirectory)) {
    throw "Photo directory does not exist: $resolvedPhotoDirectory"
}

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..\..\..'))
$cliProject = Join-Path $repositoryRoot 'src\PhotoCuller.Cli\PhotoCuller.Cli.csproj'
if (-not [System.IO.File]::Exists($cliProject)) {
    throw "PhotoCuller CLI project was not found: $cliProject"
}

$isTemporaryOutput = [string]::IsNullOrWhiteSpace($OutputPath)
if ($isTemporaryOutput) {
    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'PhotoCuller.Skill'
    [System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $resolvedOutputPath = Join-Path $temporaryRoot (
        'analysis-{0}.json' -f [System.Guid]::NewGuid().ToString('N'))
}
else {
    $resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
    if ([System.IO.File]::Exists($resolvedOutputPath)) {
        throw "Refusing to overwrite an existing report: $resolvedOutputPath"
    }
}

if (-not $SkipBuild) {
    & dotnet build $cliProject --configuration Release --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw "PhotoCuller CLI build failed with exit code $LASTEXITCODE."
    }
}

& dotnet run `
    --project $cliProject `
    --configuration Release `
    --no-build `
    -- `
    analyze $resolvedPhotoDirectory `
    --output $resolvedOutputPath

if ($LASTEXITCODE -ne 0) {
    if ($isTemporaryOutput -and [System.IO.File]::Exists($resolvedOutputPath)) {
        [System.IO.File]::Delete($resolvedOutputPath)
    }
    throw "PhotoCuller analysis failed with exit code $LASTEXITCODE."
}

if (-not [System.IO.File]::Exists($resolvedOutputPath)) {
    throw "PhotoCuller reported success but did not create: $resolvedOutputPath"
}

$resolvedOutputPath
