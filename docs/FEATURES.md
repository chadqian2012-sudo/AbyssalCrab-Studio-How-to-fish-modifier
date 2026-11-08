# 功能指南 | Feature Guide

## r23 实体标注共享显示参数，攻击筛选保持独立 | Entity overlay shares display settings

武器战斗中的“实体标注”卡由主窗口和悬浮页共用。辅助瞄准或魔法子弹至少一项开启时，显示距离内的所有可攻击活体和有效队友都可标注，墙后仍显示；是否显示队友不受两项“锁定队友”攻击开关控制。两项功能都关闭后隐藏标注。

The main window and floating page share one **Entity overlay** card. While either aim assist or magic bullets are enabled, all attackable living entities and valid teammates within the display distance are marked, including targets behind walls. Displaying teammates is independent of both teammate-targeting switches. Turning both combat features off hides the marks.

每个目标始终有红框；有可用骨骼时叠加骨骼，不替换红框。从当前准心投影位置到目标的连线宽 1 像素，不透明度为 55%。只有真实可用的骨骼才绘制连线，不虚构缺失骨骼。标注距离默认 200 米，允许 1–1000 米小数，与辅助瞄准和魔法子弹各自的攻击距离独立。

Every marked target retains a red box, with available bones overlaid rather than replacing it. Lines from the current projected aiming point to the target use 1-pixel strokes at 55% opacity. Missing bones are not invented. Display distance defaults to 200 m and accepts decimals from 1–1000 m, independently of the two attack-distance settings.

编辑距离后点击“应用显示距离”，只更新绘制参数，不重发瞄准开关。无效数字草稿保留且不能单独提交；开启原有瞄准功能仍使用共享的最后有效距离，不因标注草稿无效而失败。成功提示显示本次回包确认的距离，不是持续读取游戏状态。预设与显式恢复上次会话会恢复该参数；启动或恢复参数本身不会开启瞄准功能。

**Apply display distance** updates rendering only and sends no combat-toggle request. Invalid drafts remain editable and cannot be submitted separately; enabling either combat feature uses the shared last valid distance, so an invalid display draft does not block it. Success reports the distance confirmed by that request, not continuous live-state monitoring. Presets and explicit session restoration include this setting; startup and parameter restoration do not themselves enable aiming.

配置沿用现有参数文件的 `General.entityOverlayDistance`，保存最后有效值，不改变文件版本。两个原有战斗配置请求带上可选 `entityOverlayDistance`；独立应用使用 `view.entity_overlay.configure {distance}`。重连及失败仍沿用原有处理规则。实机画面、遮挡与负载表现待 C-15 验收。

The existing preference schema stores the last valid value in `General.entityOverlayDistance`. Both combat configuration requests carry optional `entityOverlayDistance`; independent apply uses `view.entity_overlay.configure {distance}`. Existing reconnect and failure rules remain in effect. Actual rendering, occlusion behavior and load still require C-15 manual acceptance.

## r22 小窗口保留操作空间与当前设置 | Shared game panel

在主窗口勾选“游戏悬浮窗”，切回已连接的游戏窗口后显示 AC 按钮。拖动按钮调整位置，点击或按 `Home` 展开；“收起”或 `Esc` 返回游戏。设置中可改成 `F8`、`Ctrl+Home` 等快捷键。`Ctrl+Shift+F12` 保留为紧急停用，面板顶部也有同一操作的按钮。

Enable the game panel in the main window, then return to the connected game. Drag the AC launcher to move it; click it or press `Home` to expand. Collapse or press `Esc` to return to the game. The hotkey is configurable; `Ctrl+Shift+F12` remains emergency stop and is also available as a visible button.

- **快捷面板**：在设置中为面板取名、新建，再添加功能。普通功能可直接编辑和操作；复杂功能进入完整页面。使用上下箭头调整快捷项顺序。
- **全部功能**：与主窗口共用分类、设置及执行状态。主窗口修改的数字草稿会同步显示，切换分类不会重建功能对象。窄窗分类横向滚动，选中项自动进入视口；普通功能和角色手感纵向单列。物品、鱼类、战斗、传送等复杂设置保留完整布局，在内容区域滚动。复杂设置保留足够高度，底部操作和目录末尾都可滚动到达。
- **传送点**：直接传送本机角色，沿用安全落点设置。即使主窗口选中过其他成员，快捷传送也不会传送该成员。管理坐标入口可保存当前位置和编辑传送点。
- **参数预设**：保存当前参数、载入、重命名、删除及加入快捷面板。载入先停用持续功能，再载入参数；不会自动生成、传送、加钱或下注。
- **设置**：保存面板、快捷项顺序、热键、位置和大小。重启从收起状态开始。切换到其他应用或最小化游戏时隐藏，不强行切回游戏。

Custom panels mix features, complete pages, saved local-player teleports, and named presets. Both windows share parameter drafts and execution state. Renames update references; unavailable entries are shown as unavailable. Preset loading disables continuous features and loads parameters without executing one-shot actions. Layout and shortcuts persist, while startup always begins collapsed. Windowed and borderless modes are supported; exclusive fullscreen and live focus behavior still require manual acceptance.

小窗口里，持续功能摘要与快捷项共用滚动区域，展开摘要后仍能滚动到下面的操作。底部状态保持固定的一行高度；长文字或带换行的错误消息不会撑高区域，悬停可查看完整内容。传送点、分组和预设的长名称换行显示。

In short panels, the continuous-feature summary shares the shortcut scroll area, so controls remain reachable after expansion. The footer has a fixed single-line height; long or multiline errors cannot expand it, and the tooltip retains the full text. Long point, group and preset names wrap.

r22 的传送点和参数预设页保留列表的可见高度，内容较多时从外层滚动到全部按钮。“全部功能”页在小窗口展开“预设与快捷入口”后，也能滚动到搜索框和下方功能卡；较高窗口不额外出现外层滚动条。页面长状态限制高度，悬停可查看全文，避免多行错误挤掉编辑区域。快捷面板选择框和列表也可悬停查看完整面板名称。

In r22, teleport and preset pages keep a finite visible list area and let the outer page scroll to all buttons. In short windows, the full-feature page can also scroll to its search field and feature cards after the preset shortcut area expands; taller windows do not gain an unnecessary outer scrollbar. Status height is bounded so multiline errors cannot consume the editors, while tooltips retain the full message. Panel selectors and lists also expose complete panel names through tooltips.

悬浮窗展开后，点击顶部搜索按钮或按 `Ctrl+F`。可按名称、说明、分类或 ID 搜索，选择结果后点击“打开分类”或“加入面板”。打开分类不会自动滚动并高亮具体卡片，也不会执行该功能；复杂功能进入其完整设置分类。主窗口与悬浮窗都用独立输入框填写搜索文字，再从结果列表选择。没有匹配结果时保留文字，方便继续修改；清空搜索或没有匹配结果时，两按钮不可用。搜索下方会提示未输入、已找到结果或没有匹配。`Ctrl+F` 只在展开面板收到按键时处理，不占用全局快捷键。

While the panel is expanded, use the search button or `Ctrl+F`. Search by name, description, category or ID, select a result, then open its category or add it to the selected panel. Opening a category does not execute the feature or scroll to and highlight a particular card; complex features lead to their full settings category. Both windows use a separate text field and result selector. Unmatched text stays visible for further editing; both buttons are disabled for an empty search or no matching result. The hint distinguishes empty input, matching results and no matches. `Ctrl+F` is local to the expanded panel, not a global hotkey.

快捷项直接显示原功能状态。断线时传送项会提示当前会话不可传送；操作进行中或引用已被删除时，也有对应提示，不能把“没有操作进行中”误当成已经连接。暂时不可执行的复杂功能仍能打开设置。快捷页可展开“当前持续功能开关”查看摘要；该摘要只反映程序记录的开关，不证明游戏中实际生效。载入预设等待期间，主窗口和快捷面板都不能编辑参数，结束后恢复编辑。

Shortcut status comes from the original feature state. Teleport entries report when the current session cannot teleport; pending operations and deleted references have separate messages. An idle operation is not treated as proof of a connection. Complex-feature settings remain accessible while execution is unavailable. Expand the continuous-feature summary on the shortcut tab to inspect the app's recorded switches; this is not proof of live-game effects. Both windows prevent parameter editing while a preset load is pending and allow it again when the operation ends.

热键使用单个按键，可用 `+` 加修饰键，例如 `Ctrl+Home`。`Home,End` 等逗号组合无效，不会被注册成另一个按键。`Ctrl+F` 保留给面板内搜索，不能设为展开热键。快捷面板或位置设置保存失败时，状态区会显示失败原因；未写入的设置保留在内存，后续保存可重试。保存恢复后，错误提示清除，显示最新操作结果；失败期间载入预设也不会让旧保存错误永久残留。

Use one hotkey with optional `+` modifiers, such as `Ctrl+Home`. Comma combinations such as `Home,End` are invalid and are not registered as another key. `Ctrl+F` is reserved for panel search and cannot become the expansion hotkey. If panel or position settings cannot be saved, the status areas report the failure and pending settings remain in memory. A successful retry clears the warning and shows the latest operation result, including when a preset was loaded while saving was unavailable.

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

导入采用合并：同组同名的旧坐标保持原值，只追加新点。重复导入不会取消当前选择、重建已有快捷卡或清除未完成的坐标草稿。

Imports merge points: an existing group/name retains its saved coordinates, and only new points are appended. Importing duplicates does not clear the current selection, rebuild existing shortcut objects or discard unfinished coordinate drafts.

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
