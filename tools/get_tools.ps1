# 배포 빌드에 필요한 도구(ConfuserEx2 1.6.0, MIT)를 tools\confuser 에 내려받는다. 해시가 다르면 중단한다.
$ErrorActionPreference = "Stop"
$dir = Join-Path $PSScriptRoot "confuser"
$url = "https://github.com/mkaring/ConfuserEx/releases/download/v1.6.0/ConfuserEx-CLI.zip"
$sha256 = "A00DE7CDDC740F7EDB1BAAB4C6C9073553DCC88F7E873D15B7FD34DDD33753D7"

if (Test-Path (Join-Path $dir "Confuser.CLI.exe")) { Write-Host "ConfuserEx already present."; exit 0 }
$zip = Join-Path $env:TEMP ("confuser-" + [Guid]::NewGuid().ToString("N") + ".zip")
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest $url -OutFile $zip -UseBasicParsing
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash
    if ($hash -ne $sha256) { throw "ConfuserEx download hash mismatch: $hash" }
    New-Item -ItemType Directory -Force $dir | Out-Null
    Expand-Archive $zip $dir -Force
    Write-Host "ConfuserEx installed to $dir"
}
finally {
    if (Test-Path $zip) { Remove-Item $zip }
}
