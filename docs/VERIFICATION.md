# 当前交付验证范围 | Current Verification Scope

## r32 满批生成的实际数量、清理和帧率待实测

首次试用本版时，从托盘退出旧修改器并重启游戏。在自己的房主测试存档里分批进行下列检查。

- [ ] 用一种趣味物品和一种趣味动物各发起一波 150 个。核对每波登记数、实物、事件原有动作和清理后的剩余数量。海鸥飞靶应在登记完成后开始计时。
- [ ] 在常规生成页分别生成 100 件普通物品、100 条活鱼和 100 条死鱼。确认每次只执行一批，最终计数为 100，并检查原有鱼类和配件设置。
- [ ] 在同一位置记录生成前、满批时、清理后的帧率。若出现断线或数量不足，先核对场上实际剩余数量，再决定是否重新生成。

## 手记保存与搜索的主要提速来自 C# 优化

同一台 Windows x64 电脑上，用 r30 桌面程序集和候选程序集测试。四份旧格式手记由 r30 生成，分别含 200、4,000、20,000 条单场鱼获，以及分在 100 个场次的 20,000 条鱼获。独立进程依次运行三种模式，再倒序重复。以下区间是两轮各自的中位数，单位为毫秒。测试测量应用逻辑，未计 WPF 绘制。

- **实际保存并替换已有文件**包含旧文件校验、备份与写盘。2 万条分在 100 局时，r30 为 266–275，优化后的 C# 为 122–128，开启 Rust 为 130–147。两条新路径输出的文件完全相同，大小从 8.93 MB 降到 6.66 MB。
- **首次切到另一场**会建立该场次的时间排序。相同的 100 局样本中，r30 为 13.36–13.94，优化后的 C# 为 0.57–0.61，Rust 为 0.168–0.172。Rust 在这一步更快，默认仍使用 C#。
- **连续输入搜索词**会复用场次索引。100 局样本的当前场有 200 条鱼获，r30 为 13.35–14.00，两条新路径均约 0.12–0.13。若 2 万条都在当前场，r30 为 28.46–30.51，两条新路径为 10.90–11.69。文字匹配沿用同一套 .NET 语言规则。

四份文件合计每个模式核对 180 组查询。三种模式的鱼获 ID、显示顺序、手记摘要和图鉴结果相同。r30 可无损读回新文件。另测了坏数据、未来版本、缺少 Rust 库和不兼容的库，并验证了原子替换失败时保留之前的手记。离线复跑方式见[构建说明](BUILD.md#同一批手记可以复跑三组对照)。

候选通过 633 项应用行为检查与 172 项离屏界面检查。内置和独立手记均实测了补记、删除、保存被拒后继续搜索。全局及悬浮窗搜索使用同一索引。输入 Rust、投影或两个完整选项名可找到「实体标注」。

- [ ] 在实际窗口使用中文输入法，观察大手记连续输入、切换场次和滚动时的响应。离线计时未计入输入法和屏幕绘制。

## Rust 投影和选敌已通过离线对照，游戏画面待验收

投影测试通过最终运行模块调用 Rust DLL。对同一批 4,096 个点，在带平移的相机、非对称透视和偏移视口下，两种计算对点的显示与隐藏判断一致，可见定位框边缘坐标相差小于 0.001 像素。Rust 单元测试还覆盖正交相机、相机后方、骨骼穿过近裁切面、部分出屏的红框和无效输入。另从 r29 取出原 C# 方框、骨骼线段和定位框计算，在三种合成相机上比较 90,000 条输入。显隐全部一致，显示坐标最大差为 0 像素。真实 Unity 相机仍待画面对照。

用游戏自带的 Mono 离线运行原版和新选敌代码。目标、瞄点和遮挡使用替身。5,006 批输入的最终目标和遮挡检查顺序一致，其中 1,748 批触发了同级等距回退。使用没有同键的输入计时，每次扫描的 C# / Rust 中位耗时分别为：24 个目标 2.66 / 2.18 微秒，64 个目标 8.66 / 5.64 微秒，128 个目标 18.71 / 11.00 微秒。计时包括整理候选和调用 Rust。游戏引擎读取、真实射线耗时和整帧耗时仍需实机测量。

运行模块与 r29 逐方法核对，6 个既有方法有改动，其余 1,236 个保留。离线检查覆盖 Rust 库加载失败时保留上次选择、两个开关分别应用、界面同步、预设恢复及切回原 C#。最终桌面程序集通过 629 项行为检查、172 项离屏界面检查，120 张界面图对应的绑定错误记录为空。

- [ ] 在同一存档、同一相机位置轮流应用 C# 和 Rust 投影。对照第一人称、第三人称、开镜、屏幕边缘和贴近相机的骨骼、定位框及准心连线。
- [ ] 单开 Rust 选敌。分别观察 Boss、动物、队友同时在场，目标被遮挡、死亡及同级等距时的实际锁定。队友开关分别验证。
- [ ] 在相同场景分别测量 24、64、128 个实体时的帧耗时，记录两项开关各自的影响。游戏中切回 C# 后再确认一次画面和锁定结果。

## r29 候选已经完成代码核对，真实弹道和 Rust 画面仍需实测

本轮只做代码和离线检查。每颗弹丸分别保存目标与已追加伤害的实体；保留 Boss 优先和队友开关。普通弹丸与即时命中枪械均经过原生方法参数核对。目标的非物理速度采样使用实体自身位置，避免同一实体的不同瞄准点污染共享速度记录。

本地候选的结果：24 项弹丸/速度模拟、38 项原生接口与计算对比、628 项成品桌面行为、170 项离屏界面检查全部通过。118 张离屏渲染图的绑定错误记录为空。两种单文件成品的 450 个内部文件逐一一致。补丁保留 1,192 个既有方法原样，只修改允许清单中的 21 个方法；新增 29 个方法。

在相同合成线段输入下，24 个实体时 C# 与 Rust 的中位耗时为 0.027 / 0.0073 毫秒；64 个为 0.096 / 0.031 毫秒；128 个为 0.209 / 0.109 毫秒。Rust 在该计算阶段更快，但本次最多仅节省约 0.10 毫秒，不能据此承诺明显的整场游戏收益。

离线模拟执行本轮实际方法体，游戏对象、物理和伤害接口使用替身。验证分别保留目标、单颗弹丸失去目标后重新选择、飞行中取消队友、溅射与弹道范围重合去重、即时命中回调、嵌套发射恢复、257 个受击对象不被固定缓冲区截断、停用，以及头部/身体采样不产生假速度。它不能证明 Unity 物理、联网伤害或 Boss 死亡事件已经实机通过。

计算对比调用最终 Runtime 中的 C# 方法和真实 Rust DLL，并实际执行补丁后的原生加载入口。相同输入的结果在 0.001 像素内一致。采用每个模拟实体 37 条线段的合成场景；24、64、128 个实体分别测试。计时包含每批固定托管数组和调用 Rust 的开销，不包含骨骼读取、世界投影、GPU 绘制与游戏 Mono 环境，不能换算成游戏帧率。

原版 C# 绘制路径保留并保持默认。Rust 仅负责裁剪与细线几何生成；Unity 对象仍由 C# 在主线程读取。默认版与压缩版均核对实际嵌入的 Runtime、Harmony、Rust 内容。成品不自动连接游戏进行验收，也不替换当前桌面版本。

以下项目全部保留为待实机验收，不能用离线结果勾选：

- [ ] 重启游戏，仅使用 `Ac Test Saving One`。先保持三个新增范围开关关闭，对照 r28b 的普通魔法子弹、开镜触发、无后坐力、原生伤害与紧急停用。
- [ ] 多颗子弹同时飞向多个移动目标。有效目标不被更近目标抢走；原目标死亡、离场或被掩体阻断后，只有失效子弹重新选敌。新选敌仍是 Boss、动物、玩家顺序，同类优先最近。
- [ ] 非物理驱动目标在移动、跳跃和转身时切换头部/身体锁定，同时开启辅助瞄准和魔法子弹。确认提前量没有因为共享速度记录而跳动。
- [ ] 分别测试溅射、加粗弹道及两者同时开启。测试普通弹丸、霰弹和即时命中武器，核对半径边缘、墙后范围、多碰撞体去重及同颗弹丸不重复追加伤害。水面命中和原生撞墙仍需观察。
- [ ] 在房主测试世界打开强制击杀。核对普通动物、Boss、无敌阶段、尸体/掉落物与任务进度；同时观察伤害反馈和击杀归属。代码已核对原生生命值和死亡入口，尚不能代替这些游戏结果。
- [ ] 两名玩家分别验证队友未勾选、勾选、子弹飞行中取消。未勾选时不应受到本功能新增范围效果；始终不击杀自己。核对普通友伤规则和房主强制击杀同步，非房主应收到明确的无法同步错误。
- [ ] 分别切换原版 C#、批量 C#、批量 Rust。核对真实骨骼、红框、准心连线、黄角标、遮挡、开镜、第一/第三人称、近裁切和不同分辨率；魔法子弹范围圈保持原样。
- [ ] 在相同位置和视角分别记录 24、64、128 个实体下的三种绘制方式的帧耗时、帧率、遮挡和峰值；不得只比较离线线段计算成绩。范围伤害也需单独观察大量实体死亡时的峰值。
- [ ] 核对主窗口和悬浮窗中的新增选项、中文小数输入、重启/预设恢复、断线换房和紧急停用。重启游戏后手动应用 Rust；切回原版 C#，确认画面与输入恢复正常。

No live game was started or controlled. Fake-engine projectile tests, actual native loading, numerical parity, packaged WPF behavior and off-screen layout are separate evidence. All gameplay, rendering, multiplayer and frame-rate items above remain manual acceptance work; the preceding desktop release is retained.

## r28b 清理召唤生物还需要在游戏里核对来源、同步和帧率

已核对游戏的两条召唤路径和实体退出时的联网回调。每次召唤只登记游戏实际生成的对象，清理前再次核对该次生成记录。模拟场景混入 1,024 条召唤鱼和 256 条同种野生鱼。召唤鱼全部被清理，野生鱼全部保留。另测了同一对象和网络编号被再次使用、鱼被钩住或捡起、换房、紧急停用和生成来源失效。

候选包通过 58 项清理模拟检查、620 项桌面行为检查及 164 项离屏界面检查。默认与压缩 EXE 内的 450 个文件逐一一致，程序实际嵌入了本版 Runtime。首次体验时请从托盘退出旧修改器，并重启游戏加载新运行时。

- [ ] 在房主测试存档中找到野生活鱼或海鸥，再用鱼类生成或「海鸥迁徙」召唤同种活体。混放普通掉落物和死鱼，手持一件，再点击清理。分别核对召唤活体消失、同种野生活体仍在、手持物保留。
- [ ] 连续使用趣味事件和普通生成，清理后让游戏再次自然生成同种动物，再用本按钮和原有的撤回按钮核对。新的野生活体应保留。飞靶回合中清掉的鸟不应计作击落。重新读档后再核对一次。
- [ ] 两名玩家同房，在大量掉落物清理期间捡起一件、再丢下一件，双方确认最终状态一致。另测中途紧急停用、断开和换岛，确认没有继续清理新世界。
- [ ] 在同一位置和视角记录大量掉落物清理前、清理中和清理后的帧率或帧耗时，留意首次点击和批次间的卡顿。

## r27 的离线修复仍有三项需要在真实环境确认

从 r27 默认版中提取的程序通过全部 613 项行为检查和 163 项离屏界面检查。114 张界面图的绑定错误记录为空。默认版和压缩版中的 450 个内部文件逐项一致，内嵌游戏运行时与 r26 相同。

在独立的临时文件上，r26 可复现两类数据丢失。恢复过程中断时，使用正斜杠的备份清单可能导致原存档被删除。较新版体验设置和钓鱼手记更改字段结构时，r26 可能覆盖新文件。r27 的回归检查对比原文件和备份的字节，并模拟写入和回滚中断。

数值输入现在同时接受点号和 Windows 当前区域的小数点。德语区域可输入 `1,25` 或 `1.25`。同一规则用于倍率、大陀螺转速和手记鱼重。数值范围、整数要求及配置文件的数字格式沿用原有约定。

- [ ] 使用中文输入法在主窗口及游戏悬浮窗搜索“转速”。选字时测试方向键、回车与 Esc。选字结束后再次使用方向键选中结果，打开转速框并输入，留意游戏是否误收按键。
- [ ] 在测试世界中纳入一次慢连接、一次同世界断线重连、一次完全退出并重开游戏。观察连接提示和已开启的持续功能，确认新进程没有继承旧进程的开关。另测断线期间更换房间。
- [ ] 在使用逗号小数点的 Windows 环境下，用倍率框和手记各输入一个小数，核对显示、送出的数值和重启后的记录。如需核对游戏存档恢复，仅使用测试存档及独立备份。

The offline checks exercise interrupted restores, forward-version data protection, localized numerical input, text composition and game-process transitions. Actual IME candidate windows, focus inside the game overlay and connection changes in a live game remain manual checks.

如果同一游戏进程在断线期间切换了单人存档，而且游戏返回了相同的场景句柄，现有接口无法区分两个存档。已送入游戏的单次操作也不能由桌面程序撤回。岛屿加载的独立回归覆盖了旧成功和旧失败回执晚到的情形，确保它们无法覆盖新场景的提示或解除新请求的忙碌状态。

## 功能搜索的键盘焦点与中文输入法仍待人工确认 | Search focus and Chinese IME need a desktop check

离线界面检查逐项打开了 11 个分类中的 85 个搜索目标。搜索“红框”能定位显示距离，搜索“大陀螺”能定位转速输入框并保留正在编辑的草稿。两个打包版本的 450 个内部文件逐项一致。从成品提取的程序通过了全部 600 项行为检查和 145 项界面检查，界面绑定错误为零。

All 85 search destinations across the 11 categories were located in the rendered interface. The entity-overlay and spin-speed cases also checked the editor's position and the unfinished spin-speed draft. All 450 payloads match between the default and compressed executables. The packaged application passed all 600 behavior checks and 145 off-screen UI checks with no binding errors.

- [ ] 在主窗口按 `Ctrl+F`，用中文输入法输入“转速”。确认选字用的方向键、Enter 和 Esc 先交给输入法。选字结束后，用方向键选“大陀螺”，按 Enter 应看到转速输入框和短暂描边。能编辑时，直接输入应落在这个框里。
- [ ] 在展开的小悬浮窗重复操作，搜索“红框”并打开“实体标注”。确认距离输入框在窗口内可见，主窗口没有被拉到前台；搜索与输入时游戏角色没有移动或攻击。

In the main window, check Chinese IME candidate selection before testing arrow keys and Enter on the search results. Confirm that the spin-speed editor receives input when enabled. Repeat in a small overlay with the entity-display distance field. Verify that the field stays visible, the main window stays in the background, and text entry does not trigger movement or attacks.

## 系统托盘的真实桌面交互仍需人工确认 | Tray desktop acceptance remains manual

托盘版沿用 r24 维护版的 Runtime。本轮只允许代码和离线检查；自动测试使用独立参数目录与预览模式，不连接游戏或向桌面发送输入。

r25b 默认版和压缩版的 450 个内部文件逐个一致。从最终包运行的 596 项行为检查与 141 项界面检查全部通过；生成 114 张图，未记录界面绑定错误。

隐藏检查连续两次调用窗口的关闭方法，确认窗口未被销毁、轮询仍开启、功能开关和模拟连接均保留。测试没有显示主窗口，也没有点击真实托盘。

重复打开时，置前权限交给拥有唤醒事件的进程。离线检查使用随机名称；实际窗口能否出现在游戏前方仍需手动确认。

退出检查使用本机测试管道：先发出会话检查，再请求退出。即使检查返回失败，退出仍能收到停用回执。等待期间手记定时器已停止，唤醒入口已撤下，再点退出不会重复发命令。离开前的开关仍可用于下次手动恢复。测试不创建游戏连接，无法证明游戏已经停用效果。

- [ ] 启动后同时出现主窗口和托盘图标，点 × 隐藏后功能继续生效，左键图标恢复窗口；从游戏悬浮窗再次打开之前隐藏的手记。
- [ ] 隐藏后快速连开两次同版本，应能在游戏前方看见原窗口，且没有第二份连接。选择退出后若在等待回执时重开，应提示稍后再试；退出完成后能正常启动。
- [ ] 托盘音效、悬浮窗和材质与主界面同步，紧急停用后游戏内效果确实停止。
- [ ] 托盘退出保存原开关，下一次可手动恢复；手记任务忙碌时拒绝退出，退出后不残留进程或图标。
- [ ] 资源管理器重启后图标恢复；Windows 注销或关机正常结束进程。

Offline checks do not establish native tray clicks, foreground activation, Explorer restart recovery, Windows shutdown, or in-game continuity. These remain manual acceptance items.

## 维护版的离线表现与 r24 修订版一致，游戏手感待核对

从默认包提取的程序通过 596 项行为检查和 123 项离屏界面检查。114 张截图与 r24 修订版相同。两版注入的 Runtime 与 Harmony 内容一致。单独读取这两个内嵌文件时，测试线程的托管内存分配减少 8.75 MiB；这项测量不涉及游戏帧率。

- [ ] 测试世界中首次连接，再断线重连。检查需要装备的功能能否显示待命，在装备可用后恢复，以及连接失败时的提示。
- [ ] 在主窗口和悬浮窗连续切换分类、滚动后重选，并试一次“减少动态效果”。核对选中背景停在当前分类。

The maintenance build passed offline behavior and UI checks. First connection, reconnection, equipment-wait feedback, and navigation motion still require in-game acceptance.

## r24 代码修订已通过离线检查 | Post-r24 code checks

海鸥自动补充已开启时，清空波次上限或输入无效的每波数量会暂停生成。两项都填好后，下次检测到海鸥耗尽即可继续。相同操作下，旧版误关开关，修正数字后生成了 0 波；修订版收到 1 波确认。

实体框现在直接绘制四条一像素的边。第一个可见框才查询准心，同帧的其余连线共用结果。与已交付 r24 逐方法比较，运行时只改变了实体绘制方法。应用离线检查 596 项通过（原有 593 项加 3 项补群检查），运行时检查 75 项通过（原有 74 项加 1 项准心检查）。

- [ ] 游戏中编辑两格数量，确认输入期间暂停，改正后只补一波，到达上限后停止。
- [ ] 在第一、第三人称观察贴屏边的框线和移动目标连线；比较空场景及 24、64、128 个实体时的帧耗时。

The rebuilt app passed all 596 offline behavior checks with the revised runtime embedded. All 75 runtime checks passed. Frame time and appearance still need in-game measurement.

## r24 的成品已通过离线检查，实机结果待记录 | r24 candidate verification

2026-11-09，在 r23 上新增锁定角标、右 Ctrl 临时隐藏和一轮 60 秒的海鸥飞靶挑战。两种发布包均已解包，450 个有效文件逐项相同。两份主程序都嵌入本次运行时。

- 运行时 74 项通过。直接执行候选 DLL 的计分、截止时间、重复击落和弹丸编号算术；对照当前游戏程序集检查枪械发射、伤害、预制体筛选及生成登记/回滚入口。相对 r23 保持 1159 个原方法，改变 17 个，新增 25 个。
- 成品应用行为 593 项通过。其中 150 项是体验功能检查，包括重复开局、丢失回执、断线、换世界和旧回包。真实本机测试管道把回复延迟 14 秒，新开局成功，普通趣味操作仍使用 12 秒时限。低帧率下游戏能否完成 50 只海鸥的实际登记仍待验收。
- 离屏界面 123 项通过，生成 114 张图，未发现 WPF 绑定错误。检查包括两处界面共享数量、非法草稿不能开局及小窗滚动后可操作。

The packaged application passed 593 behavior checks and 123 off-screen UI checks. The runtime passed 74 checks spanning executable scoring math and static inspection of the installed game's native weapon and entity interfaces. A local named-pipe fixture delayed both replies by 14 seconds: the challenge start succeeded while a normal wave retained its original timeout. Both packages have 450 identical entries. No game process was launched or injected.

本轮新增 C-16、C-17、C-18 后，人工验收共 64 项。优先核对开镜、开枪与角标是否同步，按住与松开右 Ctrl 的效果，以及本机命中、其他玩家击落和飞靶离场时的最终计分。

### r24 证据附录 | Evidence appendix

- Runtime audit and checks: `build/staging/combat-challenge/runtime-audit.json`, `build/staging/combat-challenge/verification/entity-overlay-verification.json`.
- Application and off-screen checks: `build/behavior-r24/behavior-manifest.json`, `build/ui-r24-packaged/ui-verification.json`.
- Package comparison: `build/package-audit-r24/package-payload-equivalence.json`.
- Runtime SHA-256: `66c58648ef441d98c3b0212414fc409571a5f1cd85293b39e1bf096828bc75b6`.
- Application SHA-256: `2af44ab3346cc4090b3542e7ef33b3a681bc9d31df81955aee06df2a157d657a`.
- single-file SHA-256: `23ee5db898edd70c81735bf3e0ed61c08a8ac99dd289f9e3601da4f1ea670a3c`.
- compressed-single-file SHA-256: `11c394508cf496adea987ba78e486d0de13c5d3b3f083647e3e0b5b8e85691de`.

## r23 两种候选包的内容一致，游戏画面待验收 | r23 package verification

2026-11-08，默认版和压缩版解出的 450 个文件逐项相同。两份主程序都嵌入经核对的新 Runtime 和 Harmony。以下检查直接使用候选运行时或从成品提取的应用程序集。

- **运行时 55 项通过。** 执行候选 DLL 内的几何裁剪代码，并静态核对队友筛选、双开关生命周期、独立距离命令、模型替换和边界回退。与 r22b 逐方法比对，1129 个原方法保持，16 个改变，31 个新增。
- **应用行为 573 项通过。** 包括原有 432 项行为检查、60 项目标选项与距离设置检查、81 项手记操作检查。各套检查加载的主程序与最终包提取件哈希一致。
- **离屏界面 116 项通过。** 生成 112 张图。主窗和悬浮窗共用同一个距离输入，小数、越界草稿、应用按钮及小窗口滚动均已检查。

Both bundles produced 450 identical payloads, including the new embedded Runtime and Harmony. The 55 runtime checks exercise pure clipping math and inspect generated code. Comparison with r22b found 1129 unchanged methods, 16 changed and 31 added. The extracted application passed 573 behavior checks and 116 off-screen UI checks, generating 112 images.

本轮没有运行或注入游戏。首次测试须重启游戏。C-15 记录了骨骼与框的贴合、原生准心对齐、挡墙后和多人显示、失焦清理，以及同场 24、64、128 个实体时的帧耗时。共 61 项人工验收仍待完成。

No game process was launched or injected. Restart the game before first use. C-15 covers camera and skeleton alignment, occluded targets, multiplayer, focus transitions, and live frame cost at 24, 64 and 128 entities. All 61 manual items remain open.

## 历史 r22b 的迟到开启回归已修复 | Historical r22b regression verification

新增测试在旧 r22 上复现了 GitHub PR #1 指出的老虎机迟到开启问题。修正后，包内程序通过 106 项会话恢复、86 项参数及原有 240 项行为检查，合计 432 项。紧停成功、失败、抛错、普通预设、切世界、退出和失败后手动重试均有对应断言。取消放在既有停止入口，由老虎机已有连接代数撤销未完成操作；保留连接和已确认的开关，未增加重连或读回命令。

New assertions reproduced the outstanding PR #1 slot-machine race against r22. The corrected packaged application passed 106 session, 86 preference and 240 existing behavior assertions (432 total), including successful, failed and throwing clears, presets, world changes, closing and manual retry. Cancellation belongs to the existing stop owner and slot-machine epoch; it retains the connection and confirmed switch without extra reconnects or proof reads.

最终包也通过94项离屏UI检查，生成95张图，无绑定错误；两包450个有效文件内容一致。悬浮窗轮询、拖动、缩放和展开复用同一次位置/DPI 读取。每轮轮询的重复读取由两次减为一次，这是调用路径核对，未测量真实帧率。UI 使用既有离屏检查；实机热键、DPI、焦点与游戏行为仍在原 60 项人工验收中。本轮没有启动可见软件窗口或操作游戏。

The final package also passed 94 off-screen UI checks with 95 renders and no binding errors; both variants have 450 byte-identical active files. Overlay polling, dragging, resizing and expansion reuse their current geometry/DPI read. Polling performs one query instead of two; this is a source-path count, not a measured frame-rate claim. Existing off-screen UI checks are reused. Actual hotkeys, DPI, focus and gameplay remain within the 60 open manual acceptance items. No visible app window or game input was used.

### r22b 证据附录 | Evidence appendix

`build/verification-r22b-final/` contains the final packaged reports; `build/merge-slot-repro/before/` preserves the failing r22 result. Bundle/branding comparisons are under `build/package-audit-r22b/`. The local delivery includes the same reports and hashes.

## 历史 r22 最终包通过离线检查，桌面快捷方式已更新

2026-11-07：主代理使用最终默认包内的程序运行 240 项行为检查：悬浮面板 61、预设边界 42、数字绑定 7、体验功能 130，全部通过。本轮先用 r21 复现了 4 项失败；新增检查使用真实主窗口数据对象和未显示的 WPF 列表、文本控件，验证选择及状态通知。没有操作游戏或可见桌面。

On 2026-11-07, the primary agent ran 240 assertions against the final default-package application: overlay 61, preset boundaries 42, numeric binding 7 and experience features 130. All passed. Four regression cases had first failed against r21. New cases use the actual main view model and non-displayed WPF lists and text controls to verify selection and binding notifications without game or desktop input.

导入传送点现在仅追加新点。重复导入保留选择、旧快捷卡对象及坐标草稿；混合导入保留旧坐标并保存新点。保存错误改由已有设置存储器的警告字段记录，正常操作结果不再被错误文字覆盖。恢复写入后清除警告，两个状态区收到更新；失败期间载入预设，恢复后显示新预设结果。预设完成时不再把显示用的保存警告复制成操作结果。

Teleport imports now append only new points. Duplicate imports retain selection, shortcut objects and coordinate drafts; mixed imports preserve old coordinates and persist additions. Save errors use the existing settings store's warning field instead of replacing operation feedback. Successful retries clear the warning and notify both status areas. Preset completion copies the operation result, so a temporary save warning cannot remain as the result after recovery.

界面子代理使用最终包内程序重跑 94 项离屏检查，全部通过，生成 95 张图，绑定错误记录为空。小窗口传送点、预设和全部功能页均能滚动到操作区。600×380 下同时打开搜索及“预设与快捷入口”，搜索框和数字编辑仍可到达；多行状态限制高度并保留全文工具提示。较高窗口不增加多余外层滚动条。`Ctrl+F` 保留给面板搜索。界面和行为报告记录的应用哈希一致。

The UI subagent reran all 94 off-screen checks against the final packaged application. All passed, with 95 renders and no binding errors. Short teleport, preset and full-feature pages can scroll to their controls. At 600×380, search and numeric editors remain reachable with search and the preset shortcut area expanded. Multiline status height is bounded and full tooltips remain available; taller windows avoid an extra outer scrollbar. `Ctrl+F` remains panel search. UI and behavior reports identify the same application hash.

默认和压缩包的 450 个有效文件逐字节一致，两包品牌检查通过；Runtime 和 Harmony 保留原内容。主代理已按用户要求将桌面现有快捷方式更新至 r22，读回目标、图标、空参数及文件哈希均符合预期。旧快捷方式已备份，r17c 程序哈希不变；r21 保留为上一候选。没有因此启动程序或游戏。真实热键、焦点、滚动手感和游戏表现仍待人工验收；原有 60 项均未勾选。

The default and compressed packages contain 450 byte-identical active files and pass branding checks; Runtime and Harmony retain their previous contents. As requested, the primary agent updated the existing desktop shortcut to r22 and verified its target, icon, empty arguments and file hash. The previous shortcut is backed up, the r17c executable hash is unchanged, and r21 remains the previous candidate. This operation did not launch the application or game. Real hotkeys, focus, scrolling and game behavior remain manual acceptance work; all 60 existing items stay open.

## 历史 r21 最终包通过离线检查，真实操作仍待验收

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

## 历史 r20 两种候选包通过离线核验，真实操作仍待验收

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


## 历史 r19 的小窗布局与共享操作通过离线检查

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

## 历史 r20 证据附录 | r20 evidence appendix

- 包内行为结果 / Packaged behavior results: `build/verification-r20-final/overlay/results.json`, `build/verification-r20-final/presets/results.json`, and the numeric runner's `results.json` under `build/verification-r20-final/numeric/`.
- 界面及包一致性 / UI and payload comparison: `build/verification-r20-final/ui/ui-verification.json`, `binding-errors.txt`, `build/package-audit-r20/package-payload-equivalence.json`, and each variant's `branding-results.json`.
- 实际应用程序集 SHA-256 / Tested application SHA-256: `d9526d2b3f402c63038d4699db4937cafe0197918af475bfbe56cc4dcfbc9863`.

报告和图像随本地候选交付保存，不将生成物提交到源码历史。This generated evidence is retained with the local candidate delivery, not committed to source history.

## 历史 r21 证据附录 | r21 evidence appendix

- 包内行为结果 / Packaged behavior results: `build/verification-r21-final/overlay/results.json`, `presets/results.json`, and the numeric/experience runner results under the same directory.
- 包内界面检查 / Packaged UI check: `build/verification-r21-final/ui/ui-verification.json` (v8), `binding-errors.txt` and 73 rendered PNGs.
- 文本高度约束 / Footer text bound: `StatusWidget.xaml` uses `MaxHeight="16"` for the status text and retains the original full-text tooltip.
- 两包内容与品牌 / Payload and branding: `build/package-audit-r21/package-payload-equivalence.json` and each variant's `branding-results.json`.
- 修复前复现 / Before-fix evidence: `build/r21-save-repro/`, `build/ui-r21-first/`, `build/ui-r21-second/`, and `build/ui-r21-multiline-repro/`.
- 实际应用程序集 SHA-256 / Tested application SHA-256: `35cb1513fd2a8e0dcf14dc8191dda4b5cfee4bc95e8cdaa78021c91a52efe9fd`.

生成的报告和图像随本地候选交付保存，不提交到源码历史。Generated reports and images are retained with the local candidate, not committed to source history.

## 历史 r22 证据附录 | r22 evidence appendix

- 修复前的 57/61 与修复后的 61/61 / Before and after behavior results: `build/r22-state-repro/before/results.json`, `build/r22-state-fixed/results/results.json`.
- 包内行为结果 / Packaged behavior results: `build/verification-r22-final/overlay/results.json`, `presets/results.json` and the numeric/experience results under the same directory.
- 包内最终界面检查 / Final packaged UI results: `build/ui-r22-packaged/ui-verification.json` (v9), `binding-errors.txt` and 95 rendered PNGs.
- 两包内容和品牌 / Payload and branding: `build/package-audit-r22/package-payload-equivalence.json` and each variant's `branding-results.json`.
- 已通过行为检查的包内应用 SHA-256 / Behavior-tested packaged application SHA-256: `c8fd9de5f09774de29fb73d0debc36d2226ed4308f2025ac04646fac6b933c51`.
- 修复前应用 SHA-256 / Before-fix application SHA-256: `35cb1513fd2a8e0dcf14dc8191dda4b5cfee4bc95e8cdaa78021c91a52efe9fd`.
- 桌面更新记录及旧快捷方式 / Desktop update record and previous shortcut: the delivery directory's `verification/desktop-delivery.json` and `verification/AbyssalCrab Studio-before-r22.lnk`.

包内界面及桌面更新已有对应记录；这些证据不代表真实游戏验收。Packaged UI and desktop changes have their corresponding records; these results do not establish live-game acceptance.

## r23 证据附录 | Evidence appendix

- Runtime: `build/staging/entity-overlay/runtime-audit.json`, `build/staging/entity-overlay/verification/entity-overlay-verification.json`.
- Package comparison: `build/package-audit-r23/package-payload-equivalence.json`.
- Behavior and off-screen UI: `build/behavior-r23/behavior-manifest.json`, `build/ui-r23-packaged/ui-verification.json`.
- Application SHA-256: `69fc1a5f3dd02411f0e6a11a6e3981604be2d17719a9a9d62d9c3bf75ca38c9e`.
- Runtime SHA-256: `36d5f71f02f9dd21ffcd37aaf7f0266732c6783c84b86802d6186ffc37db56ee`.
- Parent r22b Runtime SHA-256: `b5bf1fb7fa166ec4c500d03231594862af4784d01805497446d425f99206f210`.
- Game assembly SHA-256: `e3ec0befacc5269efdecb279f0ae72155fcde3ebaeed07e361aa15e6f7fc84b1`.
- single-file SHA-256: `9594a7ed8894086c73996c919f989c6e017832df8914f4e24aad4ebb27998f9c`.
- compressed-single-file SHA-256: `bcab66c459d6b92711960d370bc159b5b84175169721f5a8954863d0bfbbff68`.
