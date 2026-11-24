# AbyssalCrab Studio

**A Windows game assistant for How to Fish / 面向《How to Fish》的 Windows 游戏助手**

下载 [r26 功能介绍网页](docs/index.html) 后，可用浏览器离线查阅功能目录和操作说明。

## 简介 | Overview

AbyssalCrab Studio 是一款面向《How to Fish》的 Windows 桌面助手。它采用 WPF 界面，并通过游戏内 Runtime Bridge 提供玩家、钓鱼、战斗、传送、物品和趣味事件相关控制。主界面包含 11 个功能分类，并提供搜索、参数预设、收藏、最近生成配置、传送点管理、辅助瞄准选项和状态组件。

AbyssalCrab Studio is a Windows desktop assistant for *How to Fish*. It uses a WPF interface and an in-game runtime bridge for player, fishing, combat, teleport, item, and fun-event controls. The application has 11 feature categories, with search, parameter presets, favorites, recent generation settings, teleport-point management, aim-assist options, and a status widget.

## r24 飞靶与锁定提示 | Timed gull challenge and lock indicators

在武器战斗中，金色角标标出实际瞄准或魔弹引导的目标。按住右 Ctrl 可暂时隐藏标注。在趣味小功能里可以用当前枪械挑战一波海鸥，比较首中用时和击落数。此次交付为离线候选，桌面默认版仍为 r23。[玩法与控制](docs/FEATURES.md) · [验收记录](docs/VERIFICATION.md)

Gold corners indicate the actual aim or recently guided target. Hold right Ctrl to hide the marks, or start a timed gull round with the equipped gun in Fun Events. The desktop remains on r23 while this candidate awaits in-game acceptance.

## r23 候选新增共享实体标注，实机验收仍待完成 | r23 entity-overlay candidate

2026-11-08：武器战斗新增一张共享“实体标注”卡。辅助瞄准或魔法子弹开启时，标注显示距离内所有可攻击活体和有效队友，墙后仍显示。红框始终保留；有可用骨骼时叠加骨骼，并从当前准心投影位置连线到目标。准心连线宽 1 像素，不透明度为 55%。显示距离默认 200 米，可独立调整为 1–1000 米小数，不改变两个功能的攻击距离或锁定队友开关。

On 2026-11-08, the r23 candidate adds one shared **Entity overlay** card to combat settings. While aim assist or magic bullets are enabled, it marks all attackable living entities and valid teammates within the display distance, including targets behind walls. The red box remains visible; available bones are drawn over it, with a line from the current projected aiming point to the target. Aim-to-target lines use 1-pixel width and 55% opacity. Display distance defaults to 200 m and accepts decimals from 1–1000 m, independently of attack range and teammate-targeting switches.

默认版和压缩版已解包核对，450 个文件逐项一致。成品通过 55 项运行时检查、573 项应用行为检查和 116 项离屏界面检查。本轮没有启动游戏。首次试用请重启游戏，按 C-15 检查骨骼、多人显示和高密度场景性能。桌面继续使用 r22b。[功能指南](docs/FEATURES.md) · [验证记录](docs/VERIFICATION.md)

Both packages contain 450 identical payload files. Offline checks passed for the candidate runtime (55 checks), packaged application behavior (573), and off-screen UI (116). Restart the game before trying the candidate. In-game rendering, multiplayer and performance at higher entity counts remain open under C-15. The desktop continues to use r22b.

## r22b 历史记录：修正停用后的迟到开启 | r22b history

2026-11-07：紧急停用、预设切换、切换世界和退出现在先撤销老虎机尚未完成的操作，避免迟到的档位回复追加开启命令；清锁失败仍保留连接和已确认状态。悬浮窗定位复用本次读取的位置及 DPI，轮询和拖动等路径不再立即重复查询。最终包通过 432 项离线行为检查；实际热键和游戏效果仍待人工验收。

The r22b patch cancels pending slot-machine work through the existing stop owner before clearing or leaving the session. A delayed rarity reply cannot enqueue a later enable, and clear failures retain the connection and confirmed state. Overlay placement reuses the current event's client geometry and DPI instead of immediately querying again. The final package passed 432 offline behavior assertions; real input and gameplay still require manual acceptance.

## r22 交付记录 | r22 delivery history

历史候选 **r22（2026-11-07）**。重复导入传送点保留选择与草稿；保存恢复后清除旧错误。小窗口的传送点、预设及全部功能页可滚动到操作项，长状态限制高度并保留全文提示。`Ctrl+F` 保留给面板搜索。玻璃主题、动画、共享参数与 Runtime 保留。最终包通过 240 项行为检查和 94 项界面检查，生成 95 张离屏图，绑定错误记录为空。两种包的 450 个有效文件内容一致。桌面快捷方式已按用户要求更新至 r22；真实游戏验收未进行。见 [功能指南](docs/FEATURES.md) 与 [验证说明](docs/VERIFICATION.md)。

Historical candidate: **r22 (2026-11-07)**. Duplicate teleport imports preserve selection and drafts, while successful save retries clear old warnings. Short teleport, preset and full-feature pages scroll to their controls; status height is bounded with full-text tooltips. `Ctrl+F` remains panel search. Glass styling, animation, shared parameters and Runtime are retained. The final package passed 240 behavior assertions and 94 UI checks with 95 off-screen renders and no binding errors. Both package variants contain 450 byte-identical active files. The desktop shortcut now points to r22 as requested; live-game acceptance remains pending. See [usage](docs/FEATURES.md) and [verification](docs/VERIFICATION.md).

上一候选 **r21（2026-11-06）** 修正了搜索、持续功能摘要和长提示挤占小窗口操作区的问题。其最终包通过 233 项行为检查和 76 项离屏界面检查。该记录仅对应 r21，不算作 r22 的包内核验。

The previous **r21 candidate (2026-11-06)** corrected search, the continuous-feature summary and long messages consuming short-window content. Its final package passed 233 behavior assertions and 76 off-screen UI checks. Those results belong to r21 and are not counted as r22 package verification.

r17c 的紧急停用、队友选项和会话恢复修复继续保留，历史检查结果见 [会话恢复说明](docs/SESSION_RESTORE.md)。桌面现有快捷方式的目标、图标、空启动参数及目标文件哈希已读回核对。旧快捷方式已备份，r17c 程序保持原样供回退；更新不代表实机验收完成。

The r17c emergency-stop, teammate-option and session-restore fixes are retained; their historical checks are in [session restoration](docs/SESSION_RESTORE.md). The existing shortcut's target, icon, empty launch arguments and target-file hash were read back and verified. The old shortcut is backed up and the unchanged r17c executable remains available for rollback. This update does not establish live-game acceptance.

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
