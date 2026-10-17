# 构建指南 | Build Guide

## 工程 | Project

- 项目文件 | Project file: `source/HowToFishTrainer/HowToFishTrainer.csproj`
- 目标平台 | Target: WPF / .NET 8 / Windows x64
- 发布入口 | Build entry point: `build/Build-Release.ps1`
- 本地构建所需引用 | References required for a local build: `patched/HowToFishTrainer.Core.dll`, `references/HowToFishTrainer.Memory.dll`, plus the embedded Runtime/Harmony assemblies in the project directory. These binary files are excluded from this public source repository; obtain them only from sources you are authorized to use.

## 环境 | Prerequisites

需要 Windows、PowerShell 和 .NET 8 Windows Desktop SDK。目录中的打包说明提到一个位于本机其他工作目录的便携 SDK；该 SDK 不包含在仓库中，不能假设克隆后该路径存在。构建前请安装官方 .NET 8 SDK，并检查项目引用及脚本中是否仍有本机绝对路径。

Build on Windows with PowerShell and the .NET 8 Windows Desktop SDK. The delivery notes refer to a portable SDK stored in another local workspace; it is not included in this repository, so that path will not exist on a fresh clone. Install the official .NET 8 SDK and check project references and scripts for machine-specific absolute paths before building.

## 发布模式 | Package modes

```powershell
& '.\build\Build-Release.ps1'
& '.\build\Build-Release.ps1' -PackageMode CompressedSingleFile
& '.\build\Build-Release.ps1' -PackageMode Folder
& '.\build\Build-Release.ps1' -PackageMode FrameworkDependent
```

默认模式生成 Windows x64 自包含单文件，无需用户另装 .NET。压缩单文件在启动时解压；目录版必须整体分发；框架依赖版要求目标电脑安装 .NET 8 Desktop Runtime x64。正式分发前应按当前交付说明核验输出与嵌入资源。

The default mode creates a Windows x64 self-contained single file and does not require a separate .NET installation. The compressed single-file build extracts on startup; distribute the folder build as a whole; the framework-dependent build requires the .NET 8 Desktop Runtime x64. Verify outputs and embedded resources against the current delivery notes before distribution.

## 当前限制 | Current limitations

此工作区没有安装 .NET SDK。打包说明中的便携 SDK 位于仓库外的本机路径；项目依赖的 DLL、图标和品牌 PNG 也未纳入此公共源码仓库。因此尚未在干净环境验证本仓库能否直接重建。请从获授权来源准备这些依赖，并检查本机绝对路径。部分旧 UI 合并或签收脚本可能引用历史目录或历史 Runtime，不应视为通用构建入口。

No .NET SDK is installed in the current workspace. The portable SDK mentioned in the packaging notes is outside the repository. The DLLs, icon, and brand PNG required by the project are also excluded from this public source repository, so a clean clone has not been verified as directly rebuildable. Obtain these dependencies only from sources you are authorized to use, and check for machine-specific absolute paths. Some older UI merge or finalization scripts may reference historical directories or runtimes and should not be treated as general build entry points.
