param(
    [string]$Destination = (Join-Path $env:USERPROFILE '.cache/GroupTake.Poc/models/bisenet'),
    [ValidateSet('resnet18','resnet34')][string[]]$Models = @('resnet18','resnet34')
)
$ErrorActionPreference = 'Stop'
$hashes = @{
    resnet18 = '0d9bd318e46987c3bdbfacae9e2c0f461cae1c6ac6ea6d43bbe541a91727e33f'
    resnet34 = '5b805bba7b5660ab7070b5a381dcf75e5b3e04199f1e9387232a77a00095102e'
}
$sizes = @{ resnet18 = 53205364; resnet34 = 93632554 }
$directory = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $directory | Out-Null
foreach ($model in $Models) {
    $path = Join-Path $directory "$model.onnx"
    if (Test-Path -LiteralPath $path) {
        if ((Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -ne $hashes[$model]) { throw "Existing $model hash mismatch. File was not overwritten." }
        Write-Output $path
        continue
    }
    $temporary = "$path.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        Invoke-WebRequest -Uri "https://github.com/yakhyo/face-parsing/releases/download/weights/$model.onnx" -OutFile $temporary
        if ((Get-Item -LiteralPath $temporary).Length -ne $sizes[$model] -or
            (Get-FileHash -LiteralPath $temporary).Hash.ToLowerInvariant() -ne $hashes[$model]) { throw "$model model integrity check failed." }
        [IO.File]::Move($temporary, $path, $false)
        Write-Output $path
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
}
# Official release/weights: https://github.com/yakhyo/face-parsing/releases/tag/weights
# This POC uses the upstream model as a mask predictor, never as a face pixel generator.
