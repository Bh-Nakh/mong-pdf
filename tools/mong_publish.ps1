# 몽 시리즈 공용 자동 배포 스크립트 (GitHub Actions 전용). 원본은 브랜딩\앱연동\mong_publish.ps1, 각 앱 저장소 tools\ 에 복사해 쓴다.
#
# 소스가 푸시되면 .github\workflows\build.yml 이 빌드한 뒤 이 스크립트를 부른다.
# 소스의 버전이 지금 배포된 버전(가장 최근 Release 의 <앱ID>-latest.json)보다 높을 때만, 그 앱 저장소에
# 버전별 새 Release(태그 v<버전>)를 만들어 업데이트 파일과 <앱ID>-latest.json 을 올린다. 버전이 같거나 낮으면 아무것도 올리지 않는다.
# 앱은 releases/latest/download/ 주소를 보므로 가장 최근 Release 가 곧 최신 버전이다. 옛 Release 는 지우지 않고 내역으로 남긴다.
# -Tag 를 주면(손으로 시험할 때만) 그 태그의 시험용 Release(미리보기)에 올린다.
# 변경 내용(note)은 푸시한 마지막 커밋 제목을 쓴다.
#
# 예)  ./tools/mong_publish.ps1 -App mongzip -VersionFile src/AssemblyInfo.cs -VersionPattern 'AssemblyFileVersion\("(\d+\.\d+\.\d+)' `
#        -File 'dist/*-Setup.exe' -AssetName MongZip-Setup.exe -Kind setup
#      -Extra 'setup/*.exe=MongExplorer-Setup.exe' : 업데이트 파일 말고 함께 올릴 파일(새 설치용 등). 여러 개 가능.
# 필요: 환경변수 GH_TOKEN(워크플로의 github.token, contents: write). 로컬에서 쓰지 않는다.
param(
  [Parameter(Mandatory)][string]$App,
  [Parameter(Mandatory)][string]$VersionFile,
  [Parameter(Mandatory)][string]$VersionPattern,
  [Parameter(Mandatory)][string]$File,
  [Parameter(Mandatory)][string]$AssetName,
  [ValidateSet('exe','setup')][string]$Kind = 'setup',
  [string[]]$Extra = @(),
  [string]$Repo = $env:GITHUB_REPOSITORY,
  [string]$Tag = ''
)
$ErrorActionPreference = 'Stop'

function Norm([string]$v) { $p = @($v.Split('.')); while ($p.Count -lt 2) { $p += '0' }; [version]($p -join '.') }

function Newest([string]$glob) {
  $f = Get-ChildItem -Path $glob -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
  if (-not $f) { throw "빌드 결과 파일이 없습니다: $glob" }
  $f
}

# 1) 소스 버전
$m = Select-String -LiteralPath $VersionFile -Pattern $VersionPattern | Select-Object -First 1
if (-not $m) { throw "버전을 찾을 수 없습니다: $VersionFile / $VersionPattern" }
$Version = $m.Matches[0].Groups[1].Value
$testMode = [bool]$Tag
if (-not $testMode) { $Tag = "v$Version" }
Write-Host "앱: $App   소스 버전: $Version   저장소: $Repo ($Tag)"

# 2) 지금 배포된 버전
$work = Join-Path ([IO.Path]::GetTempPath()) ("mong-publish-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
$published = '0'
# 실제 배포: 가장 최근 Release 의 json. 시험: 지정한 태그의 json.
$relArgs = @(); if ($testMode) { $relArgs = @($Tag) }
& gh release download @relArgs -R $Repo -p "$App-latest.json" -D $work --clobber 2>$null
$cur = Join-Path $work "$App-latest.json"
if ($LASTEXITCODE -eq 0 -and (Test-Path $cur)) {
  $published = (Get-Content -LiteralPath $cur -Raw | ConvertFrom-Json).version
  Remove-Item -LiteralPath $cur
}
& gh release view $Tag -R $Repo --json tagName 2>$null | Out-Null
$hasRelease = ($LASTEXITCODE -eq 0)
Write-Host "배포된 버전: $published"
$global:LASTEXITCODE = 0
if ((Norm $Version) -le (Norm $published)) {
  Write-Host "버전이 올라가지 않아 배포하지 않습니다."
  exit 0
}

# 3) 올릴 파일과 latest.json
$main = Newest $File
$assetPath = Join-Path $work $AssetName
Copy-Item -LiteralPath $main.FullName $assetPath
$sha = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLower()
$note = (& git log -1 --format=%s).Trim()
$manifest = [ordered]@{ version = $Version; kind = $Kind; file = $AssetName; sha256 = $sha; size = [int64]$main.Length; note = $note }
$jsonPath = Join-Path $work ($App + '-latest.json')
[IO.File]::WriteAllText($jsonPath, ($manifest | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding($false)))
Write-Host "json: $(Get-Content -LiteralPath $jsonPath -Raw)"

$extraPaths = @()
foreach ($e in $Extra) {
  $i = $e.LastIndexOf('=')
  if ($i -lt 1) { throw "-Extra 형식은 '경로패턴=올릴이름' 입니다: $e" }
  $src = Newest $e.Substring(0, $i)
  $dst = Join-Path $work $e.Substring($i + 1)
  Copy-Item -LiteralPath $src.FullName $dst
  $extraPaths += $dst
}

# 4) 올리기 (Release 가 없으면 만든다. 시험용은 '최신'이 되지 않게 미리보기로 만든다. 실제 배포는 변경 내용을 본문에 적는다)
if (-not $hasRelease) {
  $flags = @('--title', $Tag, '--notes', $note)
  if ($testMode) { $flags += '--prerelease' } else { $flags += '--latest' }
  & gh release create $Tag -R $Repo @flags
  if ($LASTEXITCODE -ne 0) { throw "Release 를 만들 수 없습니다: $Tag" }
}
# 새 설치용 파일과 업데이트 파일을 먼저, latest.json 을 마지막에 올린다 (앱이 json 을 먼저 보고 옛 파일을 받지 않게)
& gh release upload $Tag -R $Repo @extraPaths $assetPath --clobber
if ($LASTEXITCODE -ne 0) { throw "파일 업로드에 실패했습니다" }
& gh release upload $Tag -R $Repo $jsonPath --clobber
if ($LASTEXITCODE -ne 0) { throw "latest.json 업로드에 실패했습니다" }
Write-Host "배포했습니다: $App $Version  https://github.com/$Repo/releases/tag/$Tag"
