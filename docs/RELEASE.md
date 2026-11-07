# 发布说明 | Release Notes

## r22b 修复停用后重新开启 | r22b pending-operation fix

2026-11-07：修复老虎机档位回执迟到时可能越过紧急停用继续开启的问题；普通参数预设、切世界及退出也使用同一取消入口。停用失败时仍能手动重试。悬浮窗复用本次事件的游戏窗口位置和 DPI，减少重复读取。Runtime 与 r22 相同。最终离线行为检查 432 项通过；真实游戏验收仍待完成，原有 60 项 TODO 不勾选。

Cancels pending slot-machine work before emergency stop, parameter presets, world changes or closing; failed clears still allow manual retry. Overlay placement reuses current event geometry/DPI. Runtime is unchanged from r22. All 432 final-package offline behavior assertions passed; the 60 manual/game acceptance items remain open.

## r22 已完成离线交付并更新桌面 | r22 delivered with desktop update

2026-11-07：r22 修正小窗口传送点、预设和全部功能页面、搜索热键冲突、重复导入取消选择，以及保存恢复后残留错误提示。沿用玻璃主题、共享参数和现有 Runtime。交付包含默认单文件版、压缩单文件版、对应源码、验证记录及人工验收清单。最终包通过 240 项行为检查和 94 项界面检查，两包的 450 个有效文件内容一致。

On 2026-11-07, r22 corrects short teleport, preset and full-feature pages, search-hotkey conflicts, selection loss during duplicate imports, and stale errors after saving recovers. Glass styling, shared parameters and the existing Runtime are retained. Delivery includes default and compressed single-file builds, matching source, verification records and the manual acceptance list. The final package passed 240 behavior assertions and 94 UI checks; both variants contain 450 byte-identical active files.

桌面现有快捷方式已按用户要求更新至 r22。目标、图标、空启动参数及目标文件哈希均已读回核对。旧快捷方式已备份，r17c 程序保持原样。更新过程没有启动程序或游戏，也不表示游戏内效果已验收；60 项人工/游戏检查继续保留未完成。

The existing desktop shortcut now points to r22 as requested. Its target, icon, empty arguments and target-file hash were read back and verified. The previous shortcut is backed up and the r17c executable remains unchanged. The update did not launch the application or game and does not establish live-game acceptance; all 60 manual/live checks remain open.

r21 保留为上一候选，r17c 保留为回退版本。悬浮窗体验设置使用版本 2；r17c 遇到新版文件会提示版本较新并保护原文件，不能读取新版面板设置。数字参数格式未改变。需要完整恢复旧体验设置时，使用升级前的独立备份，不删除当前配置。真实测试前应关闭修改器并重启游戏，避免遗留旧注入版本。

r21 remains the previous candidate and r17c remains the rollback version. Overlay experience settings use version 2. r17c protects the newer file and reports its version instead of loading the new panel settings. Numeric preference format is unchanged. Full experience-setting rollback requires a separate pre-upgrade backup; preserve the current configuration. Restart the game before live testing to remove any previously injected Runtime.

## r18b 历史候选说明 | Historical r18b candidate

2026-11-06：扩展游戏悬浮窗，共享主窗口功能与参数；支持自定义快捷面板、传送点、预设和紧急停用。当前生效的走路/冲刺倍率超过 1 时，取消本机普通持物及工具的移动摆动；保留其他动作、碰撞和已有坡面处理。新运行时基于校验过的 r17c 增量生成。

The 2026-11-06 candidate adds shared game-panel controls, custom shortcuts, local teleports, presets and emergency stop. Movement bob is suppressed for local held items and tools when the active walk/sprint multiplier exceeds 1. Other animations, collision and previous slope handling are unchanged. The Runtime is an audited incremental patch of r17c.

首次使用需在主窗口开启“游戏悬浮窗”。关闭修改器并重启游戏后再验证新 Runtime。当前仅提供默认和压缩候选包及验收清单；不替换桌面 r17c，也不将离线结果当作实机通过。

Enable the game panel in the main window. Restart the game before testing the new Runtime. Default and compressed candidate packages are provided for acceptance; r17c remains the desktop version. Offline checks do not establish live acceptance.

体验设置升级至版本 2，以保存悬浮布局和快捷项；旧版本 1 可自动读取。回退 r17c 时它会保护新版体验文件、提示版本较新，不会覆盖文件。此时旧版不能读取新面板/体验设置；原数字参数文件的格式未改变。需要完整旧体验设置时使用升级前的独立备份，不要删除当前新版文件。

Experience settings migrate from version 1 to 2. On rollback, r17c protects rather than overwrites the newer experience file, so it cannot read the new experience/layout settings. The numeric preference format is unchanged. Preserve a separate pre-upgrade experience backup when full rollback of those settings is required.

## V1.1.37 · 2026-10-17

当前正式交付包含默认整合版和压缩单文件版，均面向 Windows x64。默认版适合日常使用；压缩版体积较小，启动时会先解压。完整变更记录见根目录《交付说明.md》及本目录中的专题说明。

The current delivery includes the default integrated build and compressed single-file build for Windows x64. The default build is recommended for everyday use; the compressed build is smaller and extracts on startup. See the root `交付说明.md` and topic guides in this directory for the full change history.

### 主要内容 | Highlights

- 保留 11 个分类与 55 项功能，并持续改进玻璃材质、减少动画、键盘焦点和导航动效。 / Retains 11 categories and 55 features, with continued refinements to glass materials, reduced motion, keyboard focus, and navigation animation.
- 修复辅助瞄准在连接代次切换时的旧请求状态；新会话要求手动重新启用。 / Fixes stale aim-assist request state across connection generations; the new session requires manual re-enabling.
- 窗口圆角裁剪根据实际内容尺寸计算，适配最大化与恢复。 / Window corner clipping follows actual content size and supports maximize/restore.
- 保留开镜锁定、提前量和头部锁定选项及参数记忆。 / Retains ADS target lock, lead compensation, head-lock options, and parameter persistence.
- 增加预设、搜索、收藏、最近生成配置、传送点标记/备份、战斗诊断和状态组件。 / Adds presets, search, favorites, recent generation settings, teleport markers/backup, combat diagnostics, and a status widget.
- 大陀螺曲线与海鸥自动补充带有手动控制、上限和停止条件。 / Gyroscope curves and seagull replenishment have manual controls, limits, and stop conditions.

## 发布资产 | Release assets

将正式 EXE 作为 GitHub Release 附件，而不是提交到 Git 历史。当前默认整合版约 166 MB，压缩单文件版约 74 MB。发布前请确认文件来自当前交付目录中的 `release/`，并核验名称、大小和 SHA-256。

Attach the official EXE files to a GitHub Release instead of committing them to Git history. The current default integrated build is about 166 MB; the compressed single-file build is about 74 MB. Before publishing, confirm the files come from the current delivery's `release/` folder and verify their names, sizes, and SHA-256 hashes.

本仓库含有 MIT License。该许可证不替代第三方组件各自适用的许可证；发布第三方 DLL 前，请先核实并遵守对应授权条件。

This repository includes an MIT License. It does not replace licenses that may apply to third-party components. Verify and follow the applicable terms before publishing third-party DLLs.
