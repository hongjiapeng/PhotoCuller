param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'GroupTake.Poc/models/face_detection_yunet_2023mar.onnx'))
$ErrorActionPreference = 'Stop'
$expected = '8F2383E4DD3CFBB4553EA8718107FC0423210DC964F9F4280604804ED2552FA4'
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $destinationPath) {
    if ((Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash -ne $expected) { throw 'Existing model hash mismatch. Choose another destination; it will not be overwritten.' }
    Write-Output $destinationPath
    return
}
New-Item -ItemType Directory -Force -Path (Split-Path $destinationPath -Parent) | Out-Null
$temporaryPath = "$destinationPath.$([Guid]::NewGuid().ToString('N')).tmp"
try {
    Invoke-WebRequest -Uri 'https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx' -OutFile $temporaryPath
    if ((Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash -ne $expected) { throw 'Downloaded model hash mismatch.' }
    [IO.File]::Move($temporaryPath, $destinationPath, $false)
    Write-Output $destinationPath
} finally { if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath } }
# Optional legacy experiment only. Model/license: https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet (MIT).
