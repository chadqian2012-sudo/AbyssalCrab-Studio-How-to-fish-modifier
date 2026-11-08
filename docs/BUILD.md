# 构建指南 | Build Guide

## 工程 | Project

- 项目文件 | Project file: `source/HowToFishTrainer/HowToFishTrainer.csproj`
- 目标平台 | Target: WPF / .NET 8 / Windows x64
- 发布入口 | Build entry point: `build/Build-Release.ps1`
- r23 运行时补丁入口 | Runtime patch entry point: `build/Build-TargetOptions.ps1`
- 本地构建所需引用 | References required for a local build: `patched/HowToFishTrainer.Core.dll`, `references/HowToFishTrainer.Memory.dll`, plus the embedded Runtime/Harmony assemblies in the project directory. These binary files are excluded from this public source repository; obtain them only from sources you are authorized to use.

## 环境 | Prerequisites

需要 Windows、PowerShell 和 .NET 8 Windows Desktop SDK。脚本从 PATH 查找 dotnet，或接受 `-DotnetPath` 指定便携 SDK。离线依赖缓存可用 `-PackageCache` 指定；无需已清理的历史 SDK 下载清单。

Build on Windows with PowerShell and the .NET 8 Windows Desktop SDK. The script resolves dotnet from PATH or accepts `-DotnetPath`; `-PackageCache` selects an existing NuGet cache. The SDK is not bundled. Historical SDK-download manifests are no longer required.

## 发布模式 | Package modes

```powershell
& '.\build\Build-Release.ps1'
& '.\build\Build-Release.ps1' -PackageMode CompressedSingleFile
& '.\build\Build-Release.ps1' -PackageMode Folder
& '.\build\Build-Release.ps1' -PackageMode FrameworkDependent
& '.\build\Build-Release.ps1' -DotnetPath '<SDK>/dotnet.exe' -OutputDirectory '<output>' -OutputName 'AbyssalCrab Studio.exe'
```

默认模式生成 Windows x64 自包含单文件，无需用户另装 .NET。压缩单文件在启动时解压；目录版必须整体分发；框架依赖版要求目标电脑安装 .NET 8 Desktop Runtime x64。正式分发前应按当前交付说明核验输出与嵌入资源。

The default mode creates a Windows x64 self-contained single file and does not require a separate .NET installation. The compressed single-file build extracts on startup; distribute the folder build as a whole; the framework-dependent build requires the .NET 8 Desktop Runtime x64. Verify outputs and embedded resources against the current delivery notes before distribution.

## r23 从已核对的 r22b Runtime 制作候选 | Build r23 from the verified r22b Runtime

r23 使用显式依赖参数，从 r22b Runtime 制作实体标注补丁，不依赖旧 r11 目录。先准备获授权的游戏程序集、Harmony、dnlib 和父 Runtime，再执行：

The r23 patch takes explicit dependency paths and starts from the r22b Runtime, replacing the former r11 directory assumption. Supply authorized game assemblies, Harmony, dnlib and the parent Runtime, then run:

```powershell
& '.\build\Build-TargetOptions.ps1' -DotnetPath '<SDK>/dotnet.exe' -GameManagedDirectory '<game>/How to Fish_Data/Managed' -DnlibPath '<dnlib>/dnlib.dll' -ParentRuntimePath '<authorized-r22b>/HowToFishTrainer.Runtime.dll'
```

构建脚本仅读取游戏程序集。为复核成品，可在生成两种 EXE 后运行下列命令。脚本会解包、比较所有文件，并核对每份成品内嵌的 Runtime 与 Harmony。

The game assemblies are read-only inputs. After publishing both variants, compare their extracted payloads and verify that both contain the current embedded Runtime and Harmony.

```powershell
python '.\build\compare_packaged_payloads.py' '<default.exe>' '<compressed.exe>' '<verification>'
python '.\build\check_branding.py' '<default.exe>' '<verification>/single-file/assemblies'
python '.\build\check_branding.py' '<compressed.exe>' '<verification>/compressed-single-file/assemblies'
```

## 历史 r18 补丁有独立版本前提 | Historical r18 patch prerequisites

r18 持物修复从哈希已核对的 r17c Runtime 制作增量补丁，不重新拼接历史版本：

```powershell
& '.\build\Build-HeldMotionRuntime.ps1' -DotnetPath '<SDK>/dotnet.exe' -GameManagedDirectory '<game>/How to Fish_Data/Managed' -DnlibPath '<dnlib>/dnlib.dll' -ParentRuntimePath '<authorized-r17c>/HowToFishTrainer.Runtime.dll'
python '.\build\refresh_extraction.py' '<candidate.exe>' '<verification>/assemblies'
python '.\build\compare_packaged_payloads.py' '<default.exe>' '<compressed.exe>' '<verification>'
python '.\build\check_branding.py' '<default.exe>' '<verification>/single-file/assemblies'
```

补丁脚本先执行行为检查及原方法保留核查，成功后才更新本工作副本中的 Runtime。审计输出位于 `build/staging/held-motion`。游戏程序集只读。提取脚本自行解析单文件包，不再依赖已删除的外部解包目录，也不执行输入 EXE。

The r18 patch requires the verified r17c parent Runtime, authorized game assemblies, Harmony and dnlib. It runs behavior checks and an existing-method preservation audit before updating this worktree's embedded Runtime. Audit outputs are under `build/staging/held-motion`; game assemblies remain read-only. The bundle inspection script is now self-contained and does not execute the input EXE.

## 公共源码仍需单独准备二进制依赖 | Public source still requires separate binary dependencies

公共仓库不附带 .NET SDK。打包说明中的便携 SDK 位于仓库外的本机路径；项目依赖的 DLL、图标和品牌 PNG 也未纳入此公共源码仓库。因此尚未在干净环境验证本仓库能否直接重建。请从获授权来源准备这些依赖，并检查本机绝对路径。部分旧 UI 合并或签收脚本可能引用历史目录或历史 Runtime，不应视为通用构建入口。

The public repository does not bundle the .NET SDK. The portable SDK mentioned in the packaging notes is outside the repository. The DLLs, icon, and brand PNG required by the project are also excluded from this public source repository, so a clean clone has not been verified as directly rebuildable. Obtain these dependencies only from sources you are authorized to use, and check for machine-specific absolute paths. Some older UI merge or finalization scripts may reference historical directories or runtimes and should not be treated as general build entry points.


## 历史 r16b 与独立手记 | Historical r16b and standalone journal

独立手记项目为 `source/AnglerCompanion/AnglerCompanion.csproj`，共享修改器内的手记源码。r16b 的本地验证使用 .NET 8 Windows Desktop SDK；公共源码不包含 DLL、图标、品牌图片、程序包或游戏程序集。构建前请准备项目文件列出的获授权依赖。旧打包脚本具有版本前提，不能用它们任意组合历史 Runtime。干净克隆重建和真实游戏验收尚未完成。

The standalone journal project is `source/AnglerCompanion/AnglerCompanion.csproj` and shares journal sources with the trainer. Local r16b validation used the .NET 8 Windows Desktop SDK. DLLs, icons, branding images, distributables and game assemblies are excluded from this source publication. Supply authorized dependencies required by the project files; historical packaging scripts have version-specific assumptions. A clean-clone rebuild and live-game acceptance remain pending.

## r23 构建证据附录 | r23 build evidence appendix

- 父 Runtime / Required parent Runtime: r22b, SHA-256 `b5bf1fb7fa166ec4c500d03231594862af4784d01805497446d425f99206f210`.
- 当前 Runtime 审计 / Current Runtime audit: `build/staging/entity-overlay/runtime-audit.json`.
- 离线检查及完整哈希 / Offline checks and complete hashes: see the r23 appendix in [VERIFICATION.md](VERIFICATION.md).
- 两种包的逐文件比对 / Final-package comparison: `build/package-audit-r23/package-payload-equivalence.json`.
