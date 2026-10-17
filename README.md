# AbyssalCrab Studio

**A Windows game assistant for How to Fish / 面向《How to Fish》的 Windows 游戏助手**

## 简介 | Overview

AbyssalCrab Studio 是一款面向《How to Fish》的 Windows 桌面助手。它采用 WPF 界面，并通过游戏内 Runtime Bridge 提供玩家、钓鱼、战斗、传送、物品和趣味事件相关控制。主界面包含 11 个功能分类，并提供搜索、参数预设、收藏、最近生成配置、传送点管理、辅助瞄准选项和状态组件。

AbyssalCrab Studio is a Windows desktop assistant for *How to Fish*. It uses a WPF interface and an in-game runtime bridge for player, fishing, combat, teleport, item, and fun-event controls. The application has 11 feature categories, with search, parameter presets, favorites, recent generation settings, teleport-point management, aim-assist options, and a status widget.

## 当前版本 | Current release

当前交付版本为 **V1.1.37**（2026-10-17）。请参阅根目录的 [交付说明](交付说明.md) 以及 [docs/](docs/) 中的中英双语指南。

The current delivery is **V1.1.37** (2026-10-17). See the [delivery notes](交付说明.md) and the bilingual guides in [docs/](docs/).

主要体验功能包括：全局搜索、最多 50 套参数预设、物品/鱼类收藏与最近配置、可导入/导出的传送标记、角色手感参数、战斗诊断、状态组件、大陀螺速度曲线和带上限的可选海鸥自动补充。完整说明见 [功能指南 | Feature guide](docs/FEATURES.md)。

Highlights include global search, up to 50 parameter presets, item/fish favorites and recent settings, importable/exportable teleport markers, player-feel controls, combat diagnostics, a status widget, gyroscope speed curves, and optional seagull replenishment with hard limits. See the [feature guide](docs/FEATURES.md) for details.

## 下载 | Download

Windows x64 自包含程序应作为仓库 **Releases** 附件发布；当前源码提交不包含 EXE。默认整合版适合日常使用；压缩单文件版体积较小，但会在启动时解压。EXE 不应提交到 Git 历史。

The Windows x64 self-contained application should be published as an asset on the repository's **Releases** page; the current source commit does not include EXE files. The default integrated build is recommended for everyday use. The compressed single-file build is smaller but extracts its payload on startup. EXE files belong in Release assets, not in Git history.

## 构建 | Build

工程采用 **WPF / .NET 8 / Windows x64**。构建入口为 `build/Build-Release.ps1`。请先阅读 [构建指南 | Build guide](docs/BUILD.md)，其中说明依赖项和当前工作区限制。

The project targets **WPF / .NET 8 / Windows x64**. The build entry point is `build/Build-Release.ps1`. Read the [build guide](docs/BUILD.md) for dependencies and current workspace limitations.

## 验证范围 | Verification scope

2026-10-17 的交付说明记录了离线模拟运行时、隔离 UI 预览、包完整性及启动检查。最新一轮没有重新进行游戏内注入、生成、传送或联机实机测试。离屏检查不代表真实游戏验证、GPU 帧率保证或队友端画面验证。详见 [验证说明 | Verification notes](docs/VERIFICATION.md)。

The 2026-10-17 delivery notes record offline simulated-runtime checks, isolated UI preview, package integrity, and startup checks. The latest round did not repeat in-game injection, generation, teleport, or multiplayer testing. Off-screen checks do not establish live-game behavior, GPU performance, or what teammates see. See [verification notes](docs/VERIFICATION.md).

## 数据与隐私 | Data and privacy

用户设置保存在本机 `%LOCALAPPDATA%\AbyssalCrab Studio\HowToFishTrainer` 下。预设及体验设置与游戏存档分开；安全位置等临时位置数据只在当前会话有效，切换世界后清除。

User settings are stored locally under `%LOCALAPPDATA%\AbyssalCrab Studio\HowToFishTrainer`. Presets and experience settings are separate from game saves. Temporary location samples are session-only and are cleared when the world changes.

## 目录 | Repository layout

- `source/HowToFishTrainer/` — WPF 应用源码和 XAML 主题；独立授权的二进制依赖未提交 / WPF application source and XAML themes; separately licensed binary dependencies are not committed
- `build/` — 保留的构建脚本和运行时支持代码；排除生成物 / retained build scripts and runtime support code; generated output is excluded
- `patched/`, `references/` — 本地构建所需的二进制引用；需从获授权来源单独准备 / binary references required for a local build; obtain them separately from an authorized source
- `release/` — 本地发布文件；EXE 作为 Release 附件发布 / local release files; publish executables as Release assets
- `docs/` — 中英双语使用、构建、发布及验证指南 / bilingual usage, build, release, and verification guides

## 许可证 | License

仓库包含创建者添加的 MIT License。它不自动替代第三方组件各自适用的许可证；获取或分发第三方 DLL 前请核实并遵守其授权条件。为避免将第三方二进制误纳入 MIT 授权范围，本次源码提交不包含 DLL、EXE、图标或 PNG 文件。

This repository contains the MIT License selected by its owner. It does not replace licenses that may apply to third-party components; verify and follow those terms before obtaining or redistributing third-party DLLs. DLL, EXE, icon, and PNG binaries are excluded from this source commit to avoid implying that third-party binaries are covered by the MIT License.
