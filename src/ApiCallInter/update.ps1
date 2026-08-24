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
    $srcExe = Join-Path $SrcDir 'ApiCallInter.exe'
    if (Test-Path $exe) { Rename-Item $exe 'ApiCallInter.old' -Force }

    # appsettings.json 不覆盖，保留用户自定义（host 等）。
    # -ErrorAction Stop：复制失败必须让 catch 接管回滚，而不是带着半截文件继续删旧版
    $copyOk = $true
    try {
        Get-ChildItem -Path $SrcDir -Recurse -Exclude 'appsettings.json' -ErrorAction Stop |
            ForEach-Object {
                $dest = Join-Path $AppDir $_.FullName.Substring($SrcDir.Length).TrimStart('\')
                $destDir = Split-Path $dest -Parent
                if (!(Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
                Copy-Item $_.FullName $dest -Force -ErrorAction Stop
            }
    } catch {
        Write-Output ("复制失败：" + $_.Exception.Message)
        $copyOk = $false
    }

    # 删旧版(.old)前校验：新 exe 已复制到目标且大小与源一致；源缺失/复制半截均视为失败
    if ($copyOk -and (Test-Path $srcExe) -and (Test-Path $exe) -and ((Get-Item $srcExe).Length -eq (Get-Item $exe).Length)) {
        Remove-Item (Join-Path $AppDir 'ApiCallInter.old') -Force -ErrorAction SilentlyContinue
        Remove-Item $SrcDir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item ($SrcDir + '.zip') -Force -ErrorAction SilentlyContinue

        Write-Output "启动新版本..."
        Start-Process $exe
    } else {
        # 校验失败回滚：恢复旧 exe 并拉起，绝不留下打不开的安装
        Write-Output "升级文件校验失败，回滚旧版本..."
        if (Test-Path $exe) { Remove-Item $exe -Force -ErrorAction SilentlyContinue }
        Rename-Item (Join-Path $AppDir 'ApiCallInter.old') 'ApiCallInter.exe' -Force -ErrorAction SilentlyContinue
        Start-Process $exe
    }
} finally { Stop-Transcript }
