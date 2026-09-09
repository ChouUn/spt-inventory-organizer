#Requires -Version 7
# 把 Plugin 的构建产物复制到 SPT 的 BepInEx/plugins/ChouUn.InventoryOrganizer/。
param(
    [string]$SptDir = $env:SPT_DIR,
    [string]$Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
if (-not $SptDir) { throw 'SPT_DIR 未设置：请指向 SPT 4.1 安装目录' }
# 游戏运行时 DLL 被占用，Copy-Item 会半途失败留下混合版本，所以先拒绝。
if (Get-Process EscapeFromTarkov -ErrorAction SilentlyContinue) {
    throw '游戏正在运行，关闭后再部署'
}

$src = Join-Path $PSScriptRoot "../src/Plugin/bin/$Configuration"
$dst = Join-Path $SptDir 'BepInEx/plugins/ChouUn.InventoryOrganizer'
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item -Path (Join-Path $src '*') -Destination $dst -Recurse -Force
Get-ChildItem $dst | Select-Object Name, Length
