# 发布说明 | Release Notes

## r18 是待实机验收候选 | r18 remains a candidate

2026-11-05：扩展游戏悬浮窗，共享主窗口功能与参数；支持自定义快捷面板、传送点、预设和紧急停用。当前生效的走路/冲刺倍率超过 1 时，取消本机普通持物及工具的移动摆动；保留其他动作、碰撞和已有坡面处理。新运行时基于校验过的 r17c 增量生成。

The 2026-11-05 candidate adds shared game-panel controls, custom shortcuts, local teleports, presets and emergency stop. Movement bob is suppressed for local held items and tools when the active walk/sprint multiplier exceeds 1. Other animations, collision and previous slope handling are unchanged. The Runtime is an audited incremental patch of r17c.

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
