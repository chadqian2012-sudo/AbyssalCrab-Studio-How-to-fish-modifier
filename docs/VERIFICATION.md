# 当前交付验证范围 | Current Verification Scope

## r18b 的包内代码通过离线检查，游戏效果仍待验收

2026-11-06：实际默认包解出的程序集执行了 433 项行为检查，全部通过：悬浮控制 44、参数保存 86、上次会话恢复 82、预设与重连边界 42、体验功能 130、数字绑定 7、队友选项及范围圈 42。两种包的 450 个有效文件内容一致，均包含本轮 Runtime 和原 Harmony。原品牌检查通过。

The actual default-package assembly passed 433 offline assertions: overlay 44, preferences 86, session restoration 82, preset/reconnect boundaries 42, experience features 130, numeric binding 7, and targeting options/range 42. All 450 active payload entries match between the default and compressed packages. Both embed the current Runtime and original Harmony; branding checks passed.

持物补丁通过 55 项模拟行为检查。原 Runtime 的 1141 个方法指令保持不变，仅替换安装方法、删除旧雷达摆动处理并添加新的倍率判断及两类持物处理。编译使用本机游戏程序集，只读核对其接口；没有写入游戏文件或注入游戏。

The held-motion patch passed 55 simulated behavior assertions. The audit preserved 1141 existing method bodies, replaced the hook installer, removed the old radar-only bob handler and added the active-multiplier predicate plus two bob handlers. The installed game's assemblies were read for contract validation; game files were not modified and the candidate was not injected.

悬浮窗与主窗口的共享页面接受离屏渲染：五个页签、全部十一类页面、默认尺寸、数值草稿及双视图鱼类选择。专用验证必须以 `--ui-preview` 执行；正式配置、可见桌面窗口和游戏均不参与。

Off-screen WPF validation covers five tabs, all eleven pages, the default panel size, numeric drafts and fish selection across both views. Run the UI fixture with `--ui-preview`; formal preferences, visible desktop windows and the game are excluded.

可复现入口是 `build/verification-*/` 内显式提交的 runner 项目和 `verification/overlay-ui-run/OverlayUiCheck.csproj`。先构建 App，再构建 runner；Preferences/Experience 使用 `ABYSS_CODE_ONLY=1`，会话恢复使用 `ABYSS_SESSION_ONLY=1`。也可把解包后的 App/Core/Memory DLL 放到已构建的 runner 目录，再运行以验证实际发行内容。游戏注入、真实雷达可读性、按键是否穿透、DPI 与多显示器焦点仍不能由这些检查证明。

Committed runner projects live under `build/verification-*/` and `verification/overlay-ui-run/OverlayUiCheck.csproj`. Build the app and runners first. Use `ABYSS_CODE_ONLY=1` for Preferences/Experience and `ABYSS_SESSION_ONLY=1` for session restoration. To test release bytes, replace each runner's App/Core/Memory copies with extracted package assemblies before execution. These checks do not establish live injection, radar readability, game input isolation, or multi-monitor/DPI focus behavior.

人工验收至少覆盖：展开/收起与切应用、热键冲突、拖动缩放、多显示器；全部页面参数同步、面板与快捷项保存；本机传送、预设不执行一次性操作；四类持物在 1/1.25/5/50 倍下的动作和关闭恢复。雷达仍闪动时保留失败，不能用界面“成功”或无报错代替验收。桌面 r17c 不变。

Manual acceptance must cover window/focus/hotkey behavior, resizing and displays, shared-page state, panel persistence, local teleports, inert preset loads, and radar/guns/rods/ordinary items at 1/1.25/5/50x. Remaining radar flicker is a failure even if the UI reports success. The desktop remains on r17c.

独立复核使用用户指定的 GPT-6.1 Sol / ultra。复核后修正了功能说明框的所属窗口，避免被活动悬浮窗遮住；位置操作的成功与异常反馈统一由原有执行入口发布，删除悬浮快捷传送的重复判断。异常传送提示先复现失败，再通过新增回归；真实弹窗观感仍在人工验收范围。

An independent GPT-6.1 Sol / ultra review checked the patch. Follow-up fixes assign feature notices to the active overlay and publish both success and failure from the existing position-action owner, removing duplicate shortcut checks. The teleport-exception regression failed before the fix and passed afterward; actual dialog presentation remains manual acceptance.

## 以下记录来自此前交付 | Historical records

本页依据《核查修复与桌面更新交付说明.md》《UI与动画优化交付说明.md》和《体验功能更新交付说明.md》整理。

This page summarizes the scope described in the delivery notes for review fixes, UI and animation, and experience features.

## 已记录的检查 | Recorded checks

2026-10-17 的交付文档记录了离线受控 Runtime、隔离 `--ui-preview`、会话状态、参数恢复、功能分类、数值输入、UI 布局/动画检查，以及默认与压缩单文件包的完整性和启动检查。体验功能说明还记录了预设恢复、导入校验、自动补充上限、冷却和失败停止等模拟/隔离检查。

The 2026-10-17 delivery notes record checks using an offline controlled Runtime and isolated `--ui-preview`, covering session state, parameter restore, feature categories, numeric input, UI layout/animation, and integrity/startup of the default and compressed single-file packages. The experience-feature notes also record simulated or isolated checks for preset restore, import validation, replenishment limits, cooldown, and failure-stop behavior.

## 明确未覆盖 | Explicitly not covered

最新核查修复交付没有重新进行游戏内注入、生成、传送或联机实机测试。离屏 WPF 检查和截图不能证明最新版本的真实游戏行为、真实 GPU 帧率或队友端画面。历史实机记录不等于最新版本重新测试。

The latest review-fix delivery did not repeat in-game injection, generation, teleport, or multiplayer testing. Off-screen WPF checks and screenshots do not establish live-game behavior for the latest version, actual GPU frame rates, or what teammates see. Historical live-game records are not a retest of this release.

## 证据来源 | Source documents

- `交付说明.md` — Current delivery notes
- `核查修复与桌面更新交付说明.md` — Review fixes and desktop update
- `UI与动画优化交付说明.md` — UI and animation changes
- `体验功能更新交付说明.md` — Experience features
- `开镜锁定与帧开销优化交付说明.md` — Aim lock and frame-cost work
- `提前量与锁头交付说明.md` — Lead compensation and head lock

完整验证日志和机器报告保留在原始交付目录的 `verification/` 中；它们是体积较大的运行产物，不作为本源码仓库默认内容。

Full verification logs and machine reports remain in the original delivery directory's `verification/` folder. They are bulky generated artifacts and are excluded from the source repository by default.
