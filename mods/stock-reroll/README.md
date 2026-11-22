# 商品刷新 / Stock Reroll

## 当前版本与发布说明

当前源码版本 **1.0.8**，对应正式版 **0.46D / Steam Build 26525552**。启用[兼容补丁](../stock-reroll-better-merchants/README.md)后，可用同一个快捷键刷新 Better Merchants 的夜间店铺，沿用成交保护和刷新收费设置。[Steam 创意工坊条目](https://steamcommunity.com/sharedfiles/filedetails/?id=3828934641)

- “刷新收费”默认关闭。开启后，困难及以上难度对同次来访的成功刷新依次收取 10、20、30……信用点，顾客和杰克逊夜间商店均适用。
- 余额不足或刷新失败时保留原商品，不扣费、不涨价。
- 适配当前游戏中改名的七处顾客供货回调。

完整变更见 [change.log](change.log)。

Stock Reroll supports Better Merchants night shops through the [optional compatibility patch](../stock-reroll-better-merchants/README.md). [Changelog](change.log)

## 简体中文

刷新当前顾客和杰克逊夜间商店的未购买商品，保留顾客、交易条件和已购物品。默认成交后禁止刷新，可在游戏内的 Mods 配置中调整。无法安全刷新时保留原商品。

刷新期间会阻止供货回调自动弹出交易／批发页，便于连续按 F5。刷新结束后，正常点击顾客、放置物品打开交易页的行为照常生效；主动打开的交易或议价窗口仍须先关闭才能刷新。

开启“成交后禁止刷新”时，成功结算一次鉴定也会禁止刷新本次顾客的剩余物品，无需重新打开交互窗口。仅查看物品或被拒绝的估价不会触发此限制。

Mods 配置新增 **允许其他 Mod 回调刷新（有风险）**（`AllowModCallbacks`），默认开启，应用后立即生效。开启后会重放当前顾客到店及已触发对白中的其他 Mod 回调，也支持 Mod 包装原版供货的情况，例如机核协议的小偷供货。尚未触发的对白仍不会提前执行，成交后的刷新限制仍由原配置控制。

此选项可能重复奖励、扣费、推进剧情，或丢失没有重新生成的 Mod 商品。生成失败或没有新商品时仍恢复原库存，但其他 Mod 已改变的非商品状态无法回滚；如不希望重放这些回调，可关闭此选项。

供货回调最初按正式版 Steam Build 26284217 审计；2026-10-31 使用 **Build 26317870** 复核当前程序集的 110 个白名单签名、供货调用和剧情状态写入屏障，并运行正式版源码关联的离线回归。这不代替实际顾客、交易与库存的原生测试。覆盖超科技走私贩、退休水产养殖户、黄牛贩子、分类拾荒者／小偷、付费配送员、随机鉴定顾客、彩票代表，以及贝利、屠夫、乔治、内利斯和程大爷的售货回访。刷新只补货，不重复顾客介绍、来访计数或首次赠品；剧情奖励和免费配送不作为可刷新商品。

**快捷键**

- 默认 **F5** 刷新商品。
- 在 Mods 配置的 **Hotkey** 中修改，支持 Ctrl、Alt、Shift 组合键，例如 **Ctrl+R**；设为 **None** 可禁用。
- 交易或议价窗口打开时不能刷新；文字输入、原生改键捕获及捕获完成当帧不响应刷新快捷键。

## English

Reroll unpurchased goods from the current customer or Dr. Jackson's night shop while keeping the visitor, trade terms and purchased items. Rerolls are blocked after a transaction by default; this can be changed in the in-game Mods settings. If a reroll cannot be performed safely, the original goods are kept.

Supply callbacks cannot automatically open the trade or wholesale panel during a reroll, so F5 can be pressed repeatedly. Normal customer clicks and item placement can open the panel once the reroll ends. Close any trade or bargaining panel you opened before rerolling.

With "Block after transaction" enabled, a paid appraisal also blocks rerolls of that visitor's remaining items, even before reopening an interaction panel. Viewing items or submitting a rejected estimate does not trigger this lock.

The Mods settings now include **Allow Mod callbacks (risky)** (`AllowModCallbacks`), enabled by default and effective as soon as settings are applied. It replays other Mods' callbacks from the current customer's arrival and reached dialogue, including wrapped native suppliers such as Mechcore Protocol's thief stock. Future dialogue remains untouched, and the existing transaction setting still controls rerolls after a transaction.

This option may repeat rewards, charges or story effects, or lose Mod goods that are not regenerated. Failed or empty generation still restores the original inventory, but non-stock changes made by another Mod cannot be rolled back. Disable this option if you do not want these callbacks replayed.

Supply callbacks were originally audited on full-release Steam Build 26284217. On 2026-10-31, the current **Build 26317870** assembly was checked for all 110 allowed callback signatures, stock calls and story-state write guards, alongside offline regressions linked to official production sources. Actual visitors, trades and inventories still require native tests. Coverage includes the Hypertech Smuggler, Retired Aquafarmer, Scalper, specialized scavengers and thieves, paid couriers, random appraisal clients, the lottery representative, and sale visits by Bailey, the Butcher, Georgio, Nellis and Grandpa Cheng. Rerolls only restock goods without repeating introductions, visit counters or first-visit gifts. Story rewards and free deliveries are excluded.

**Hotkeys**

- Press **F5** to reroll stock by default.
- Change **Hotkey** in the Mods settings. Ctrl, Alt and Shift combinations such as **Ctrl+R** are supported; use **None** to disable the shortcut.
- Rerolls are blocked while trade or bargaining panels are open. The shortcut is ignored while typing, during native key binding capture, and on the frame capture ends.
