# 发布说明 | Release Notes

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
