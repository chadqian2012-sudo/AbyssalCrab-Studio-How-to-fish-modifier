# 当前交付验证范围 | Current Verification Scope

## r21 最终包通过离线检查，真实操作仍待验收

2026-11-06：主代理使用最终默认包内的程序运行 233 项行为检查：悬浮面板 54、预设边界 42、数字绑定 7、体验功能 130，全部通过。同一包内程序通过 76 项界面检查，生成 73 张离屏图，绑定错误记录为空。短窗口检查覆盖 620×380 和 600×380：搜索与摘要同时展开后，快捷区仍可滚动；长名称、搜索提示和多行错误也已检查。

The primary agent ran 233 behavior assertions against the final default-package application: overlay 54, preset boundaries 42, numeric binding 7 and experience features 130. All passed. The same packaged application passed 76 UI checks with 73 off-screen renders and no binding errors. Checks at 620×380 and 600×380 cover reachable shortcuts with search and the summary expanded, long labels, search hints and multiline errors.

早期离屏检查复现了搜索、摘要和长错误提示挤占正文的问题。摘要移入原快捷项滚动区；底部状态限制为一行高度，并在原工具提示中保留全文。仅设置不自动换行仍会被错误消息自带的换行撑高，因此再限定文本高度，相关多行回归已通过。

Early renders reproduced search, summary and long errors consuming the content area. The summary now uses the existing shortcut scroll area. The footer is limited to a single-line height and retains full text in its existing tooltip. Disabling automatic wrapping alone did not constrain embedded line breaks, so the text height is also bounded; the multiline regression now passes.

另修正两处问题：`Home,End` 会被枚举解析误认成方向键，现由原热键解析入口拒绝逗号组合；保存失败的原提示仅在预设区域，快捷面板和设置页看不到，现由原保存方法同时通知悬浮窗底部。保存失败提示用 r20 包内代码先复现失败，修正后与恢复写入重试一起通过。保存预设时的多余刷新已删除，仍由原集合变化事件更新快捷项。

Two other defects were corrected: enum parsing could turn `Home,End` into an arrow key, so the original parser rejects commas; save errors previously appeared only in the preset area, outside the shortcut/settings pages, so the save owner now also updates the overlay footer. The save-feedback regression first failed against r20 and now passes along with the retry case. Redundant preset refresh was deleted; the existing collection event still updates shortcuts.

两种包的 450 个有效文件逐字节一致，品牌检查通过；所有报告的应用哈希与实际包一致。Runtime 与 Harmony 未改。编译无错误，现有源码警告保留；NuGet 漏洞数据读取失败，不能称为安全审计通过。

Both package variants contain 450 byte-identical active files and pass branding checks; report hashes match the packaged application. Runtime and Harmony are unchanged. Compilation succeeded with existing source warnings; NuGet vulnerability-data retrieval failed, so this does not establish a completed security audit.

桌面保持 r17c，前一候选为 r20-final。本轮没有操作游戏或发送桌面输入。不同 DPI 模式的游戏与悬浮窗之间是否存在位置偏差，仅为代码推断，未作猜测修补；与真实热键、焦点、保存提示及小窗口操作一起保留在 O-01～O-05。60 项人工/游戏验收仍未完成。

The desktop remains on r17c, with r20-final retained as the previous candidate. No game or desktop input was sent. A possible placement mismatch between game and overlay DPI modes remains an unverified code-level concern and received no speculative patch. It joins real hotkey, focus, save-feedback and short-window operation in O-01 through O-05; all 60 manual/live acceptance items remain open.

## r20 两种候选包通过离线核验，真实操作仍待验收

2026-11-06：主代理使用最终默认包提取的应用程序集重新执行 101 项行为检查：悬浮面板 52、预设与重连边界 42、数字绑定 7，全部通过。包内程序集另通过 64 项界面检查，生成 65 张离屏图，WPF 绑定错误文件为空。各报告中的应用哈希与最终包一致；这些是本轮实际重跑结果，没有将 r19 的旧记录计入。

The primary agent reran 101 behavioral assertions against the application extracted from the final default package: overlay 52, preset/reconnect boundaries 42, and numeric binding 7. All passed. The packaged assembly also passed 64 UI checks with 65 off-screen renders and an empty WPF binding-error file. Reported application hashes match the final package; these are current reruns, not reused r19 results.

默认与压缩包的 450 个有效文件逐字节一致，两包品牌检查通过；Runtime 与 Harmony 保持 r19 的内容。编译没有错误，仍有原来的 68 项源码警告，警告内容集合与 r19 一致。NuGet 因访问被拒未能取得漏洞数据，因此不能将构建成功称为完整安全扫描通过。

All 450 active payload entries are byte-identical between the default and compressed variants, and both pass branding checks. Runtime and Harmony retain their r19 contents. Compilation succeeded with the existing 68 source warnings; the warning-content set matches r19. NuGet could not retrieve vulnerability data because access was denied, so successful compilation does not establish a completed security scan.

本轮复用已有搜索和功能对象。检查覆盖：搜索结果打开分类、加入当前面板、清空及无匹配结果、失效引用、离线传送禁用、复杂功能设置可访问，以及持续功能摘要。打开分类和加入面板均不发送游戏执行命令。搜索只进入对应分类，不承诺自动滚动并高亮某张卡片。另先复现了无匹配结果时搜索文字被清空的问题，再将两处窗口的输入和结果选择分开；新增检查确认两边共享的搜索文字均能保留。

This change reuses the existing search and feature objects. Checks cover category navigation, adding results to the selected panel, empty/no-match searches, missing references, disabled offline teleports, accessible complex-feature settings and the continuous-feature summary. Navigation and adding a shortcut send no game execution commands. Search opens the matching category without promising automatic scrolling to and highlighting an individual card. A check first reproduced search text being cleared for unmatched input; separating input from result selection in both windows then passed checks that preserve their shared text.

代码复核还发现两处问题并修正：快捷数值输入现在共用主窗口的编辑许可，载入预设等待时不能输入会被稍后覆盖的草稿；快捷传送的会话提示与实际传送使用同一个条件，不再把空闲状态当成可连接。离线检查包含预设等待、完成、取消及迟到回复；取消后新输入的草稿须保留。

Review also corrected two issues: shortcut numeric editors now share the main window's edit permission while a preset is pending, and teleport availability uses the same session condition as execution instead of confusing idle with connected. Offline cases cover pending, completed and cancelled preset loads, including late replies; new drafts entered after cancellation must survive.

Runtime 和 r19 相同；桌面仍为 r17c，r19 保留作前一候选。真实键盘/输入法搜索、快捷键与焦点、状态切换观感和预设等待时的双窗口操作仍记在 O-01～O-05，原有 60 项人工/游戏验收状态没有改变。本轮未操作桌面或游戏，离屏图不代表真实游戏验收。

Runtime is unchanged from r19. The desktop remains on r17c, with r19 kept as the previous candidate. Real keyboard/IME search, hotkeys and focus, status presentation and preset editing across both windows remain in O-01 through O-05; the existing 60 manual/live acceptance items retain their status. No desktop or game input was sent; off-screen renders are not live-game acceptance.


## r19 的小窗布局与共享操作通过离线检查

2026-11-06：从最终默认包提取的应用程序集通过 93 项既有行为检查：悬浮面板 44、预设与重连边界 42、数字绑定 7。更新后的界面验证通过 40 项检查，生成 47 张离屏图，未出现 WPF 绑定错误。检查包括全部十一类窄窗页面、宽窗列数、末尾分类可达、滚动时选中背景立即跟随、无效数字草稿跨尺寸保留、空面板、长名称、复杂页底部及多分类目录可达。检查读取的程序集哈希与实际包一致。

The assembly extracted from the final default package passed 93 existing behavioral assertions: overlay 44, preset/reconnect boundaries 42, and numeric binding 7. The UI fixture passed 40 checks with 47 off-screen renders and no binding errors. It verifies all eleven narrow pages, wide-layout columns, access to the final category, immediate selection-background tracking during scrolling, unfinished numeric drafts across resizing, empty panels, long names, complex-page bottom access and long catalog categories. Runner reports identify the actual packaged assembly hash.

两种单文件包包含 450 个逐字节一致的有效文件；Runtime 和 Harmony 与 r18b 相同。编译没有错误，原工程的 68 项警告仍在。三名子代理分别完成视觉、布局和独立审查；复核后修正了输入框文字截断、页签选中样式影响正文，分类滚动时背景追赶，以及生成目录和分类清单高度不足的问题。

Both single-file variants contain 450 byte-identical active payloads. Runtime and Harmony are unchanged from r18b. Compilation succeeded with the existing 68 warnings. Three parallel agents handled visual design, compact layout and independent review; follow-up fixes corrected clipped input text, selected-tab styling leaking into content, delayed navigation highlighting during scrolling, and undersized catalog/category list viewports.

本轮仅改现有窗口、共享页面及手感区的显示。展开动画复用主题时长，减少动态效果时直接显示；收起和清理仍立即执行。没有改游戏命令、参数模型或注入代码。离屏渲染没有打开可见窗口，没有发送桌面输入，也没有读取正式用户配置。

Changes are limited to the existing window, shared view and player-feel presentation. Expansion uses the existing theme duration and skips motion when reduced motion is enabled; collapse and cleanup remain immediate. Game commands, parameter models and injection code are unchanged. Off-screen rendering did not show desktop windows, send desktop input or load formal user preferences.

真实动画观感、键鼠焦点、快捷键冲突和多显示器 DPI 仍需人工检查，继续记在 O-01～O-03。七类复杂页面仍需在内容区横纵滚动，并未改成全套单列。桌面保持 r17c；r18b 保留为前一候选。以下 r18b 检查是历史证据，没有把其全部检查计为本轮重跑。

Live animation, focus/input behavior, hotkey conflicts and multi-monitor DPI remain manual acceptance items O-01 through O-03. Seven complex pages still scroll within their content area on both axes when needed. The desktop stays on r17c and r18b remains the previous candidate. The r18b results below are historical evidence, not a claim that every earlier test was repeated for r19.

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

## r20 证据附录 | r20 evidence appendix

- 包内行为结果 / Packaged behavior results: `build/verification-r20-final/overlay/results.json`, `build/verification-r20-final/presets/results.json`, and the numeric runner's `results.json` under `build/verification-r20-final/numeric/`.
- 界面及包一致性 / UI and payload comparison: `build/verification-r20-final/ui/ui-verification.json`, `binding-errors.txt`, `build/package-audit-r20/package-payload-equivalence.json`, and each variant's `branding-results.json`.
- 实际应用程序集 SHA-256 / Tested application SHA-256: `d9526d2b3f402c63038d4699db4937cafe0197918af475bfbe56cc4dcfbc9863`.

报告和图像随本地候选交付保存，不将生成物提交到源码历史。This generated evidence is retained with the local candidate delivery, not committed to source history.

## r21 证据附录 | r21 evidence appendix

- 包内行为结果 / Packaged behavior results: `build/verification-r21-final/overlay/results.json`, `presets/results.json`, and the numeric/experience runner results under the same directory.
- 包内界面检查 / Packaged UI check: `build/verification-r21-final/ui/ui-verification.json` (v8), `binding-errors.txt` and 73 rendered PNGs.
- 文本高度约束 / Footer text bound: `StatusWidget.xaml` uses `MaxHeight="16"` for the status text and retains the original full-text tooltip.
- 两包内容与品牌 / Payload and branding: `build/package-audit-r21/package-payload-equivalence.json` and each variant's `branding-results.json`.
- 修复前复现 / Before-fix evidence: `build/r21-save-repro/`, `build/ui-r21-first/`, `build/ui-r21-second/`, and `build/ui-r21-multiline-repro/`.
- 实际应用程序集 SHA-256 / Tested application SHA-256: `35cb1513fd2a8e0dcf14dc8191dda4b5cfee4bc95e8cdaa78021c91a52efe9fd`.

生成的报告和图像随本地候选交付保存，不提交到源码历史。Generated reports and images are retained with the local candidate, not committed to source history.
