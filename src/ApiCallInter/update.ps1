param(
    [int]$OldPid,
    [string]$SrcDir,
    [string]$AppDir,
    [string]$LogPath
)
Start-Transcript -Path $LogPath -Append
try {
    Write-Output "等待旧进程 $OldPid 退出..."
    Wait-Process -Id $OldPid -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800

    $exe = Join-Path $AppDir 'ApiCallInter.exe'
    if (Test-Path $exe) { Rename-Item $exe 'ApiCallInter.old' -Force }

    # appsettings.json 不覆盖，保留用户自定义（host 等）
    Get-ChildItem -Path $SrcDir -Recurse -Exclude 'appsettings.json' |
        ForEach-Object {
            $dest = Join-Path $AppDir $_.FullName.Substring($SrcDir.Length).TrimStart('\')
            $destDir = Split-Path $dest -Parent
            if (!(Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
            Copy-Item $_.FullName $dest -Force
        }

    Remove-Item (Join-Path $AppDir 'ApiCallInter.old') -Force -ErrorAction SilentlyContinue
    Remove-Item $SrcDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item ($SrcDir + '.zip') -Force -ErrorAction SilentlyContinue

    Write-Output "启动新版本..."
    Start-Process $exe
} finally { Stop-Transcript }
