# 恢复上次会话 / Restore last session

连接游戏世界后，在“预设与快捷入口”中点击“恢复上次会话”。新版至少连接一次并正常退出后才有开关快照；旧版没有记录的开关不能追溯。启动及连接均保持功能关闭，普通“恢复参数”预设的行为不变。

参数仍保存到现有 `user-preferences.json`，使用稳定功能 ID 和参数名。新增的 `session` 字段保存持续开关意图，不保存运行时成员 ID。参数变化合并保存，断开及关闭清理前保留快照，启动时另取独立副本，避免本次编辑或自动保存污染上次会话。

点击恢复会等待 `locks.disable_all` 成功，再恢复最后有效参数、持续开关和已保存的倍率。包括普通开关、辅助瞄准、魔法子弹、大陀螺、准星、战斗反馈、老虎机、轮盘、死亡位置观察和自动补充设置；观察模式仍排斥瞄准。每项复用原有功能 owner，确认后才发布启用状态；部分失败显示未全部恢复及原因，断开、切世界或关闭会停止余下步骤。

生成、传送、加钱及其他成员操作属于一次性动作，不会重放；成员作用范围参数保留，实际应用须重新选择当前成员。若上次保存了自动补充开关，手动恢复后该自动功能可以按现有条件产生新波次。无效数字草稿不持久化，恢复最后有效值。纯视觉偏好及传送点继续由已有配置文件管理，未改动主题、分类和动画方案；新增按钮使用现有玻璃卡片及按钮样式。

本次同时修正手记结束场次的保存失败：保留原场次 ID、退出采样时间和原因，保存成功才发布结束状态，重试不能把离开游戏后的时间计入原局。它记录的是周期检查观察到退出的时间，并非精确的进程退出瞬间。

代码与单文件载荷检查属于离线证据，真实窗口交互和游戏效果仍由 TODO 的 P-17、J-02 等项目验收。根据用户最新要求交付桌面快捷方式时，仍标记实机/人工验收未完成。

2026-11-03 复核修正：恢复过程中请求抛出异常或切换世界后，当前连接的辅助瞄准仍应允许手动配置；紧急停用会取消正在进行的会话/预设恢复，阻止后续保存开关继续开启。清锁返回失败时明确提示失败，不声称游戏中的持续效果已全部停止。紧急停用热键沿用 Ctrl+Shift+F12，窗口转发到同一个应用层流程，保留连接握手期间原有桥接清锁通道。

手记启动时若发现上次异常退出的未结束场次，也复用同一待保存结束记录。文件暂时被占用时，保留最后一次已保存的心跳时间作为结束时间；解除占用后先补存结束记录，再开始新场次或添加鱼获。

## English

After connecting to a game world, expand the preset toolbar and choose **Restore last session**. The new version must have connected and exited normally at least once to record switch states. Older versions did not record those states. Startup and connection do not automatically enable saved features; ordinary parameter presets remain parameter-only.

The existing preferences file gains an additive `session` dictionary keyed by stable feature IDs. A separate startup snapshot prevents later editor changes from replacing the session selected for restoration. Restore first waits for all locks to stop, then loads valid parameters and applies saved persistent switches and multipliers through their existing owners. Individual failures are reported. Disconnecting, changing worlds or closing stops subsequent restore commands.

Generation, teleportation, currency additions and member operations are never replayed. Runtime member identifiers are not persisted. A saved automatic refill switch may resume its normal conditional waves after this explicit action. Existing appearance, animation, categories and separate visual/teleport files remain in use.

The journal fix retains an unsaved game-exit completion, including its original session ID, observed timestamp and reason. Retries cannot extend that session or attach new catches to it. Desktop delivery is explicitly requested; offline checks still do not establish live game acceptance or manual UI acceptance. These remain in the acceptance ledger.

The November 3 review also fixes aim controls remaining detached after a failed restore or a world change. Emergency stop cancels an in-progress restore before clearing locks, so later saved features cannot resume from that operation. A rejected clear is reported as a failure. The existing hotkey and bridge-only connection path are preserved.
