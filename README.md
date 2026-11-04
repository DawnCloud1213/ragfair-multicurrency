# Ragfair Multi Currency Prices

在跳蚤市场「新增报价」窗口右上角，把市场最低 / 平均 / 最高价同时换算成**美元、欧元**显示。

- 游戏：SPT 4.1.6
- 环境：BepInEx 5.4.23.5 + Harmony 2.9.0，net472
- 类型：**纯客户端 UI 模组**，不碰交易事务，Fika 联机无同步影响
- 状态：v0.2.1 实机验收通过

## 效果

原版三行（卢布）**完全不动**，下方追加独立 6 行：

```
最低价  30 734 ₽
平均价  37 666 ₽
最高价  45 530 ₽
USD 最低   $279
USD 平均   $342
USD 最高   $414
EUR 最低   €236
EUR 平均   €290
EUR 最高   €350
```

拖动新增报价窗口时，追加块跟着一起走。

**每一行都能点**：点 `USD 平均 $342` 就把 342 填进美元报价输入框，点 `EUR 最低 €236` 就填欧元框——走的是游戏自己的 `RequirementView.SetValue`，和点原版卢布价格完全同一条链路（会自动触发报价重算）。

## 安装

```
powershell -ExecutionPolicy Bypass -File build.ps1 -Deploy
```

或手动把 `dist\BepInEx\plugins\RagfairMultiCurrency\RagfairMultiCurrency.dll`
复制到 `D:\free games\EFT_0821\BepInEx\plugins\RagfairMultiCurrency\`。

> 游戏运行时也能部署：`build.ps1` 检测到 dll 被占用会自动改名归档再放新文件，**重启游戏**后生效。

## 配置（游戏内按 F12）

| 配置 | 默认 | 说明 |
|---|---|---|
| `General.Enabled` | true | 总开关 |
| `General.Scope` | AddOfferWindowOnly | 只在新增报价窗口生效 / All 全部市场价面板 |
| `Rates.RoublesPerUsd` | 110 | 1 美元 = 多少卢布 |
| `Rates.RoublesPerEur` | 130 | 1 欧元 = 多少卢布 |
| `Display.ShowUsd` | true | 显示美元 3 行 |
| `Display.ShowEur` | true | 显示欧元 3 行 |
| `Display.Decimals` | 0 | 换算后保留几位小数 |
| `Display.Colorize` | true | 给数值上色 |
| `Display.UsdColor` | #4CD964 | 美元颜色 |
| `Display.EurColor` | #5B8DEF | 欧元颜色 |
| `Layout.Gap` | 4 | 与价格面板的垂直间距（像素） |
| `Layout.VerticalNudge` | 0 | 额外垂直微调，正数下移 |
| `Layout.FontSize` | 14 | 追加块字号（14 = 与原版价格一致） |
| `Layout.LineSpacing` | 1.0 | 行距倍率 |
| `Debug.DumpLayout` | false | 重新 dump 面板层级（改锚点时才开） |
| `Debug.LogPrices` | true | 每次取价写日志 |

`Layout.*` 支持游戏内 F12 实时调整，改完切一下物品重新取价即生效，不用重启。

## 工作原理

```
AddOfferWindow.SetPrices()
  → ItemMarketPricesPanel.UpdatePrices(templateId)
  → RagFair.GetMarketPrices(templateId, CG_UpdatePrices)     异步
  → CG_UpdatePrices(ItemMarketPrices result)                 ← 模组 Postfix 挂这里
  → 原版把 result.min/avg/max（卢布）写进三个 label
  → 模组按汇率换算，写入自己克隆出来的一块 TMP 文本
```

### 实测布局（`Debug.DumpLayout` 输出，供改锚点参考）

```
AddOfferWindow        rect=1920x1080  全屏拉伸容器（不能用来定位！）
  Inner               rect=1200x970   真正的对话框，居中
    Contents          rect=1200x951   HorizontalLayoutGroup
      Right Part      rect=524x951    VerticalLayoutGroup（加子对象会被重排）
        ...
        PreviewPanel  rect=502x251    物品预览区，无 LayoutGroup
          Labels      rect=486x235    ItemInfoWindowLabels，无 LayoutGroup ← 挂这里
            Prices    rect=160x57     ItemMarketPricesPanel + VerticalLayoutGroup
```

追加块挂在 `Labels` 下、锚定右上角、位于 `Prices` 下方 —— 既跟着窗口移动，又不会被任何布局组重排。

### 关键 API（反射 `Assembly-CSharp.dll` 实测）

| 类型 | 成员 | 说明 |
|---|---|---|
| `EFT.UI.Ragfair.ItemMarketPricesPanel` | `_lowestLabel` / `_averageLabel` / `_maximumLabel`（public） | 三个价格 label |
| `EFT.UI.Ragfair.ItemMarketPrices` | `min` / `avg` / `max`（public float，卢布） | 价格数据 |
| `EFT.UI.ClickableUIText` | `_textMessage`（public `TMPro.TextMeshProUGUI`） | 文本控件 |
| `EFT.UI.Ragfair.RagFair` | `GetMarketPrices(string, Action<ItemMarketPrices>)` | 价格来源 |
| `EFT.InventoryLogic.CurrencyUtil` | `GetCurrencyChar(ECurrencyType)` | ₽ / $ / € 符号 |

## 日志

`D:\free games\EFT_0821\BepInEx\LogOutput.log`，搜 `[RMC]`：

- `hook fired; ... rawRoubles min/avg/max = ...` — 每次取价与换算
- `extra price block created under "Labels" at y=...` — 追加块创建位置
- `===== market price panel layout dump =====` — 面板层级（`DumpLayout=true` 时）

## 版本历史

| 版本 | 内容 |
|---|---|
| v0.1.0 | 把换算值追加到原版每行末尾 —— 文本超宽自动换行，三行挤成一坨，废弃 |
| v0.2.0 | 改成独立 6 行块，但挂到了全屏的 `AddOfferWindow` 根节点，跑到屏幕左下角 |
| v0.2.1 | 改挂 `Labels`（价格面板父级），锚定右上，实机验收通过 |
| v0.3.0 | 每行改成可点击的 `ClickableUIText`，点击按货币填入对应报价输入框；字号默认降到 14 |

### 点击填价的实现依据（IL 反编译实测）

```
AddOfferWindow.PricesPanelOnOnPriceClick(float p)
    → _rubRequirementView.SetValue(p.ToString("F0"))

RequirementView.SetValue(string s)
    → AtInput(s)
        → 去掉空格/逗号 → int.TryParse → Mathf.Clamp(0, 100000000)
        → _inputField.text = 格式化后的数字
        → _onQuantityChange.Invoke()      ← 报价重算
```

所以模组只需要「按货币找到 `RequirementView`（`_currency` 字段）→ 调 `SetValue`」，剩下的原版全包。

## 已知限制

- 汇率是配置项（默认 110 / 130），不自动读取游戏内交易者换汇报价
- 只在客户端显示，队友视角看不到

## 构建

```
powershell -ExecutionPolicy Bypass -File build.ps1 -Deploy
```
