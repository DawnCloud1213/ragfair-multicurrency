# Ragfair Multi Currency Prices

在跳蚤市场「新增报价」窗口里，把市场最低价 / 平均价 / 最高价同时换算成美元、欧元显示。

- 游戏：SPT 4.1.6（EFT build 40743 档）
- 环境：BepInEx 5.4.23.5 + Harmony 2.9.0，net472
- 类型：**纯客户端 UI 模组**，不碰交易事务，Fika 联机不影响同步

## 当前版本 0.1.0

第一版目标是把数据链跑通：在原版每行价格后面追加两种货币。

```
最低价   87,000 ₽   $790   €730
平均价   92,000 ₽   $836   €772
最高价   99,000 ₽   $900   €831
```

## 安装

```
build.ps1 -Deploy
```

或手动把 `dist\BepInEx\plugins\RagfairMultiCurrency\RagfairMultiCurrency.dll`
复制到：

```
D:\free games\EFT_0821\BepInEx\plugins\RagfairMultiCurrency\
```

## 配置（游戏内按 F12）

| 配置 | 默认 | 说明 |
|---|---|---|
| `General.Enabled` | true | 总开关 |
| `General.Scope` | AddOfferWindowOnly | 只在新增报价窗口生效 / All 全部市场价面板 |
| `Rates.RoublesPerUsd` | 110 | 1 美元 = 多少卢布 |
| `Rates.RoublesPerEur` | 130 | 1 欧元 = 多少卢布 |
| `Display.ShowUsd` | true | 显示美元 |
| `Display.ShowEur` | true | 显示欧元 |
| `Display.Decimals` | 0 | 换算后保留几位小数 |
| `Display.Colorize` | true | 给美元/欧元上色 |
| `Display.UsdColor` | #4CD964 | 美元颜色（TMP 颜色标签） |
| `Display.EurColor` | #5B8DEF | 欧元颜色 |
| `Debug.DumpLayout` | true | 第一次触发时把面板层级写进日志（用来设计三列排版） |
| `Debug.LogPrices` | true | 每次取价都把 min/avg/max 和换算结果写日志 |

## 工作原理（已核实）

```
AddOfferWindow.SetPrices()
  → ItemMarketPricesPanel.UpdatePrices(templateId)
  → RagFair.GetMarketPrices(templateId, CG_UpdatePrices)     异步
  → CG_UpdatePrices(ItemMarketPrices result)                 ← 模组挂在这
  → 原版把 result.min/avg/max 写进三个 label
  → 模组 Postfix 在同一行后面追加换算值
```

关键事实（反射 `Assembly-CSharp.dll` 得到）：

- `ItemMarketPricesPanel` 的 `_lowestLabel` / `_averageLabel` / `_maximumLabel` 都是 **public** 字段
- `ClickableUIText._textMessage` 是 **public** 的 `TMPro.TextMeshProUGUI`
- `ItemMarketPrices.min / avg / max` 是 **public** float，单位卢布
- 价格面板是竖向 3 行（最低 / 平均 / 最高），所以只能横向加列

## 日志

```
D:\free games\EFT_0821\BepInEx\LogOutput.log
```

搜 `[RMC]`：
- `[RMC] raw roubles min/avg/max = ...` — 每次取价
- `[RMC] ===== market price panel layout dump =====` — 面板层级（第一次用）

## 后续计划

1. 用 dump 出来的真实尺寸，把单行内联升级成真正的三列表格
2. 可选：把汇率切换成读取游戏内真实换汇报价（S2 方案）

## 构建

```
powershell -ExecutionPolicy Bypass -File build.ps1 -Deploy
```
