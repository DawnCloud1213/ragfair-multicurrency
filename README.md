# Ragfair Multi Currency Prices

跳蚤市场「新增报价」窗口的多货币参考价模组：把市场**最低 / 平均 / 最高**价按 **卢布、美元、欧元**三行并排显示在报价输入框左侧，点一下就填进对应货币的报价框。

- 游戏：SPT 4.1.6
- 环境：BepInEx 5.4.23.5 + Harmony 2.9.0，net472
- 类型：**纯客户端 UI 模组**，不碰交易事务，Fika 联机无同步影响
- 状态：v0.4.5 实机验收通过

## 效果

报价窗口右半边分两块：

**上半部分**：物品模型 + 出售数量（原版的三行参考价自动隐藏）

**下半部分**：参考价与报价输入框并排

```
         最低        平均        最高        报价
      [40 006 ₽]  [48 350 ₽]  [58 009 ₽]   [输入数量 ₽] [₽] ✕
      [   $364 ]  [   $440 ]  [   $527 ]   [输入数量 $] [$] ✕
      [   €308 ]  [   €372 ]  [   €446 ]   [输入数量 €] [€] ✕
```

- 报价输入框缩到行宽右侧 40%
- 三列参考价占左侧 60%，带「最低 / 平均 / 最高 / 报价」表头
- **点击任意价格**：先清空另外两种货币的报价，再把该数值填入对应货币的输入框
- 拖动窗口时整块跟着走

## 安装

1. 把 `dist\BepInEx\plugins\RagfairMultiCurrency\RagfairMultiCurrency.dll` 复制到
   `<游戏目录>\BepInEx\plugins\RagfairMultiCurrency\`
2. 重启游戏

> 游戏运行时也能部署：`build.ps1` 检测到 dll 被占用会自动改名归档再放新文件，重启游戏后生效。

## 构建

需要 Windows 自带的 .NET Framework `csc.exe`，并指定 SPT 客户端目录：

```powershell
$env:SPT_GAME_DIR = "X:\your\SPT\client\folder"
powershell -ExecutionPolicy Bypass -File build.ps1 -Deploy
```

## 配置（游戏内按 F12）

| 配置 | 默认 | 说明 |
|---|---|---|
| `General.Enabled` | true | 总开关 |
| `General.Scope` | AddOfferWindowOnly | 只在新增报价窗口生效 / All 全部市场价面板 |
| `Rates.RoublesPerUsd` | 110 | 1 美元 = 多少卢布 |
| `Rates.RoublesPerEur` | 130 | 1 欧元 = 多少卢布 |
| `Display.ShowUsd` | true | 显示美元行 |
| `Display.ShowEur` | true | 显示欧元行 |
| `Display.Decimals` | 0 | 换算后保留几位小数 |
| `Display.Colorize` | true | 给数值上色 |
| `Display.UsdColor` | #4CD964 | 美元颜色 |
| `Display.EurColor` | #5B8DEF | 欧元颜色 |
| `Display.HideVanillaPrices` | true | 隐藏原版参考价与「要求」标题 |
| `Layout.FontSize` | 14 | 参考价字号（14 = 与原版一致） |
| `Layout.LineSpacing` | 1.0 | 行距倍率 |
| `Debug.DumpLayout` | false | 重新 dump 面板层级（改布局时才开） |
| `Debug.LogPrices` | true | 每次取价写日志 |

`Layout.*` 支持游戏内 F12 实时调整，改完切一下物品重新取价即生效，不用重启。

## 工作原理

```
AddOfferWindow.SetPrices()
  → ItemMarketPricesPanel.UpdatePrices(templateId)
  → RagFair.GetMarketPrices(templateId, CG_UpdatePrices)      异步
  → CG_UpdatePrices(ItemMarketPrices result)                  ← 模组 Postfix 挂这里
  → 原版把 result.min/avg/max（卢布）写进三个 label
  → 模组按汇率换算，写进自己建的三行价格格
```

### 点击填价（IL 反编译实测）

```
AddOfferWindow.PricesPanelOnOnPriceClick(float p)
    → _rubRequirementView.SetValue(p.ToString("F0"))

RequirementView.SetValue(string s)
    → AtInput(s)
        → 去掉空格/逗号 → int.TryParse → Mathf.Clamp(0, 100000000)
        → _inputField.text = 格式化后的数字
        → _onQuantityChange.Invoke()      ← 报价重算
```

所以模组只需要「按货币找到 `RequirementView`（`_currency` 字段）→ 调 `SetValue`」；
清空其它货币同样是 `SetValue("")` → `AtInput` 判空 → `ResetRequirementInformation()`。

### 布局挂载点（实机 dump 得到）

```
AddOfferWindow        rect=1920x1080  全屏拉伸容器（不能用来定位）
  Inner               rect=1200x970   真正的对话框，居中
    Contents          rect=1200x951   HorizontalLayoutGroup
      Right Part      rect=524x951    VerticalLayoutGroup
        RequirementPart  要求区块，VerticalLayoutGroup
          PreviewPanel   物品预览区（上半部分）
          Requirement (RUB/USD/EUR)   报价行
            Quantity   行容器，InputField + Currency 图标
```

- 价格格挂在每行 `Quantity` 容器下，锚定在左侧 `0% / 20% / 40%` 三列
- 原版 `InputField` 的 `anchorMin.x` 从 0 改为 0.6，输入框缩到右侧 40%
- 表头挂在卢布行 `Quantity` 的上方（`offset 18~38px`）

### 隐藏原版文本的方式

不靠文字内容猜，按 `EFT.UI.LocalizedText.localizationKey` 精确匹配：

| key | 中文文本 |
|---|---|
| `ragfair/REQUIREMENTS` | 要求 |
| `ragfair/(per one item)` | (每一件物品) |
| `ragfair/(per all items)` | (全部物品) |

这样在任何语言环境下都不会误伤无关文本。

### 关键 API（反射 `Assembly-CSharp.dll` 实测）

| 类型 | 成员 | 说明 |
|---|---|---|
| `EFT.UI.Ragfair.ItemMarketPricesPanel` | `_lowestLabel` / `_averageLabel` / `_maximumLabel` / `_pricesPanel`（public） | 原版价格面板 |
| `EFT.UI.Ragfair.ItemMarketPrices` | `min` / `avg` / `max`（public float，卢布） | 价格数据 |
| `EFT.UI.ClickableUIText` | `_textMessage`（public `TMPro.TextMeshProUGUI`）、`SetText(...)` | 可点击文本控件 |
| `EFT.UI.Ragfair.RequirementView` | `_inputField` / `_currency` / `SetValue(string)` / `AtInput(string)` | 报价输入框 |
| `EFT.UI.LocalizedText` | `localizationKey`（public） | 本地化 key |
| `EFT.InventoryLogic.CurrencyUtil` | `GetCurrencyChar(ECurrencyType)` | ₽ / $ / € 符号 |

## 日志

`<游戏目录>\BepInEx\LogOutput.log`，搜 `[RMC]`：

- `hook fired; ... rawRoubles min/avg/max = ...` — 每次取价与换算
- `row cells created for RUB/USD/EUR` — 价格格创建
- `requirement text: "..."  key="..."  hide=...` — 原版文本的隐藏判定依据
- `cleared other currencies, filled USD with 342` — 点击填价结果

## 版本历史

| 版本 | 内容 |
|---|---|
| v0.1.0 | 把换算值追加到原版每行末尾 —— 文本超宽自动换行，挤成一坨，废弃 |
| v0.2.0 | 改成独立 6 行块，但挂到了全屏的 `AddOfferWindow` 根节点，跑到屏幕左下角 |
| v0.2.1 | 改挂 `Labels`（价格面板父级），锚定右上，实机验收通过 |
| v0.3.0 | 6 行改成可点击控件，点击按货币填入对应报价框 |
| v0.4.0 | 改成要求区的 3×3 九宫格（布局占用空间，体验一般） |
| v0.4.1 | 改为「输入框缩短 + 左侧三列价格」，上半原版参考价隐藏 |
| v0.4.2 | 加表头（最低/平均/最高/报价） |
| v0.4.3 ~ v0.4.4 | 隐藏「要求」「(每一件物品)」标题 |
| v0.4.5 | 改为按 localizationKey 精确隐藏，杜绝误伤；点击填价时清空其它货币 |

## 已知限制

- 汇率是配置项（默认 110 / 130），不自动读取游戏内交易者换汇报价
- 只在客户端显示，队友视角看不到
