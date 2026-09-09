# OR-Tools 客户端加载实验

验证 Google.OrTools 能否在 SPT 4.1 客户端（Unity 2022 Mono + BepInEx 5）内加载并求解。
背景与结论见
[cpsat-runtime-feasibility.md](../../docs/reports/cpsat-runtime-feasibility.md)。

## 构建与部署

用 Windows 的 .NET SDK。`SPT_DIR` 指向 SPT 安装目录，不写进仓库。

```powershell
dotnet build ChouUn.Spike.OrTools.csproj -c Release -p:SPT_DIR=$env:SPT_DIR
pwsh ./deploy.ps1 -SptDir $env:SPT_DIR
```

## 观察

启动游戏后看 `BepInEx/LogOutput.log` 中本插件的 `RESULT:` 行。
原生 DLL 加载方式由配置 `NativeLoadStrategy` 控制，
文件在 `BepInEx/config/com.chouun.spike.ortools.cfg`，可选
`SetDllDirectory`（默认）、`Preload`、`None`（基线）。
