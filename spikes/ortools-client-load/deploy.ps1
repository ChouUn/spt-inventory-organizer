#Requires -Version 7
# 把构建产物复制到 SPT 的 BepInEx/plugins/ChouUn.Spike.OrTools/。
param(
    [string]$SptDir = $env:SPT_DIR,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
if (-not $SptDir) { throw 'SPT_DIR 未设置：请指向 SPT 4.1 安装目录' }

$src = Join-Path $PSScriptRoot "bin/$Configuration"
$dst = Join-Path $SptDir 'BepInEx/plugins/ChouUn.Spike.OrTools'
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item -Path (Join-Path $src '*') -Destination $dst -Recurse -Force
Get-ChildItem $dst | Select-Object Name, Length
