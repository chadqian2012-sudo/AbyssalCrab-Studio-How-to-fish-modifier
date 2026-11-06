# 功能指南 | Feature Guide

## r19 悬浮面板在小窗中更易操作 | Shared game panel

在主窗口勾选“游戏悬浮窗”，切回已连接的游戏窗口后显示 AC 按钮。拖动按钮调整位置，点击或按 `Home` 展开；“收起”或 `Esc` 返回游戏。设置中可改成 `F8`、`Ctrl+Home` 等快捷键。`Ctrl+Shift+F12` 保留为紧急停用，面板顶部也有同一操作的按钮。

Enable the game panel in the main window, then return to the connected game. Drag the AC launcher to move it; click it or press `Home` to expand. Collapse or press `Esc` to return to the game. The hotkey is configurable; `Ctrl+Shift+F12` remains emergency stop and is also available as a visible button.

- **快捷面板**：在设置中为面板取名、新建，再添加功能。普通功能可直接编辑和操作；复杂功能进入完整页面。使用上下箭头调整快捷项顺序。
- **全部功能**：与主窗口共用分类、设置及执行状态。主窗口修改的数字草稿会同步显示，切换分类不会重建功能对象。窄窗分类横向滚动，选中项自动进入视口；普通功能和角色手感纵向单列。物品、鱼类、战斗、传送等复杂设置保留完整布局，在内容区域滚动。复杂设置保留足够高度，底部操作和目录末尾都可滚动到达。
- **传送点**：直接传送本机角色，沿用安全落点设置。即使主窗口选中过其他成员，快捷传送也不会传送该成员。管理坐标入口可保存当前位置和编辑传送点。
- **参数预设**：保存当前参数、载入、重命名、删除及加入快捷面板。载入先停用持续功能，再载入参数；不会自动生成、传送、加钱或下注。
- **设置**：保存面板、快捷项顺序、热键、位置和大小。重启从收起状态开始。切换到其他应用或最小化游戏时隐藏，不强行切回游戏。

Custom panels mix features, complete pages, saved local-player teleports, and named presets. Both windows share parameter drafts and execution state. Renames update references; unavailable entries are shown as unavailable. Preset loading disables continuous features and loads parameters without executing one-shot actions. Layout and shortcuts persist, while startup always begins collapsed. Windowed and borderless modes are supported; exclusive fullscreen and live focus behavior still require manual acceptance.

窄窗改用单列时，参数仍由同一对象保存。放大窗口会恢复原来的多列布局。展开采用短暂淡入和轻微缩放，遵循减少动态效果设置；收起和紧急停用不等待动画。

In narrow views, category navigation scrolls horizontally and ordinary controls use a single column without replacing their parameter objects. Complex pages keep their complete layout within a scrollable content area, with enough height for catalog rows. The category list scrolls independently when it is long. Wider windows retain the multi-column layout. Expansion fades and gently scales in, respecting reduced motion; collapse and emergency stop do not wait for animation.

## 高速时只取消手持物的走路摆动 | Held movement bob

当前走路或冲刺的**已生效倍率超过 1 倍**时，普通持物和工具的移动摆动量归零。没有应用的输入框草稿不参与判断。回到 1 倍、停用或退出会话后，由原生逻辑计算摆动。

Only the local player's held-item/tool movement bob is suppressed when the currently applied walk or sprint multiplier exceeds 1. Unapplied text drafts do not affect this decision. Native calculation resumes at 1x or after disabling/session exit. Turning sway, leaning, breathing, aiming, firing, reloading, casting, pickup/drop, camera motion, footsteps, collision and slope behavior are preserved.

雷达还有持物物理追随后的归位路径，本轮没有改碰撞、固定物品坐标或放宽归位阈值。离线检查不能证明高速雷达已完全稳定；若仍闪动，需单独记录并继续定位。

The radar also has a physical-follow recovery path. This change does not pin transforms, disable collision, or relax recovery thresholds. Offline checks cannot establish that all radar flicker is fixed; any remaining flicker must be recorded separately during live acceptance.

本指南依据交付目录中的说明整理，概述 V1.1.37 的功能及其边界。软件界面和当前源码为准。

This guide summarizes V1.1.37 features and their boundaries from the delivery documents. The application UI and current source remain authoritative.

## 分类与搜索 | Categories and Search

主界面有 11 个顶部分类。分类可以收起，功能说明默认折叠。全局搜索支持功能名称、说明、功能 ID 和分类，并可定位到匹配分类。

The main window has 11 top-level categories. Categories can be collapsed, and feature descriptions are collapsed by default. Global search matches feature names, descriptions, feature IDs, and categories, then navigates to a match.

## 参数预设 | Parameter Presets

可在分类顶部的“预设与快捷入口”保存当前有效参数，最多 50 套。同名保存会更新该方案。恢复预设前会先停止持续功能；恢复不会触发生成、传送、下注、加钱或游戏保存等一次性命令。辅助瞄准等持续功能不会因此自动启动。

Save up to 50 sets of valid parameters from **Presets and shortcuts** at the top of a category. Saving with an existing name updates that preset. Continuous features are stopped before applying a preset. Applying one does not trigger one-shot commands such as generation, teleport, betting, currency changes, or game saves. Continuous features such as aim assist do not start automatically.

## 收藏、最近记录和搜索 | Favorites, Recent Settings, and Search

物品和鱼类目录支持收藏、最近生成视图及配置恢复。收藏可保存配件、皮肤和数值设置。恢复配置只载入参数，不会直接生成；成功生成后才记录最近项。最近搜索最多保留 12 条。

Item and fish catalogs support favorites, recent generations, and configuration restore. Favorites can include attachments, skins, and numeric settings. Restoring a configuration only loads settings; it does not generate an item. A recent entry is recorded only after successful generation. Up to 12 recent searches are retained.

## 传送点与位置 | Teleport Points and Locations

传送点支持分组、备注、收藏、颜色、图标及 JSON 导入/导出。导入文件最多 4 MB、2,000 个点，并在应用前校验全部内容。安全位置或死亡前位置只会填入编辑框，仍需用户手动确认传送。临时位置仅在当前会话有效，切换世界后清除。死亡前采样默认关闭；开启后每两秒进行只读采样，记录可能与倒地位置相差一个采样间隔。

Teleport points support groups, notes, favorites, colors, icons, and JSON import/export. Imports are limited to 4 MB and 2,000 points, and all content is validated before changes are applied. A safe or pre-death location only fills the editor; the user must initiate teleport manually. Temporary locations are session-only and cleared on world changes. Pre-death sampling is off by default; when enabled, read-only samples are taken every two seconds, so a saved point can differ from the actual downed position by one sampling interval.

## 角色手感和趣味事件 | Player Feel and Fun Events

移动、冲刺、跳跃和抛竿参数在对应分类顶部的角色手感面板调整。滑块与数字框共用同一参数；输入校验通过后，点击应用才写入。

Movement, sprint, jump, and cast parameters are adjusted in the player-feel panels at the top of their categories. The slider and numeric input share one parameter. A valid value is applied only after clicking **Apply**.

大陀螺转速范围为每秒 2–15 圈。曲线可选固定、10 秒缓慢加速或 4 秒周期脉冲；大陀螺仍需手动开启。海鸥自动补充默认关闭；波数上限为 1–20（默认 3），每波 8 只，并设有冷却和并发限制。出错、清理、断线、世界或预设切换、紧急停用时会停止补充。

Gyroscope speed ranges from 2 to 15 revolutions per second. Curves include fixed speed, a 10-second gradual ramp, and a 4-second pulse cycle; the gyroscope still requires manual activation. Seagull replenishment is off by default; its wave limit is 1–20 (default 3), with 8 birds per wave, cooldown, and request exclusion. Errors, cleanup, disconnection, world or preset changes, and emergency shutdown stop replenishment.

## 战斗与辅助瞄准 | Combat and Aim Assist

辅助瞄准启用后，只在开镜时转向最大距离内最近的无遮挡目标；不要求目标接近准星。目标失效时会重新选择，松开瞄准后解除。提前量和头部锁定为可选项。无可见头部目标时会等待，不回退到身体瞄准。连接变化或重启后，运行开关保持关闭，需手动开启；不会自动开火。

When enabled, aim assist turns toward the nearest unobstructed target within range only while aiming down sights; the target need not be near the crosshair. It reacquires when a target becomes invalid and disengages when aiming stops. Lead compensation and head lock are optional. If no visible head target is available, head lock waits instead of falling back to the body. The runtime switch stays off after connection changes or restart and must be enabled manually. It does not fire automatically.

战斗诊断显示只读武器、目标和近期跟踪数据。观察诊断会关闭辅助瞄准与魔法子弹并阻止重新启用；退出诊断后需手动开启。

Combat diagnostics show read-only weapon, target, and recent tracking data. Observation diagnostics turn off aim assist and magic bullets and prevent them from being re-enabled; they must be enabled manually after leaving diagnostics.

## 状态组件和本地配置 | Status Widget and Local Settings

可选状态组件显示连接、持续功能、当前预设/自定义参数、分类和紧急停用提示。它只在游戏或助手处于前台时显示。关闭组件等价于隐藏，再次勾选可重新显示。

The optional status widget shows connection state, active continuous features, the current preset or custom settings, category, and emergency-shutdown hint. It is visible only while the game or assistant is foregrounded. Closing it hides it; selecting the option again shows it.

用户参数位于 `%LOCALAPPDATA%\AbyssalCrab Studio\HowToFishTrainer\user-preferences.json`。预设、收藏、最近记录、搜索、传送标记、状态组件和自动化选项保存在同目录的 `experience-settings.json`。传送点仍使用 `settings.json`；材质偏好单独保存。

User parameters are stored in `%LOCALAPPDATA%\AbyssalCrab Studio\HowToFishTrainer\user-preferences.json`. Presets, favorites, recent items, searches, teleport markers, the status widget, and automation options are stored in `experience-settings.json` in the same directory. Teleport points continue to use `settings.json`; material preferences are stored separately.
