---
version: alpha
name: noodle-pet
description: "拉麵（Windows 桌寵）的介面：沿用全域 Linear 深色系 token，只補上錯誤色、差異底色與焦點外框。桌寵角色與垂降的麵條是插畫素材，不受 UI token 約束。"

colors:
  primary: "#5e6ad2"
  on-primary: "#ffffff"
  primary-hover: "#828fff"
  primary-focus: "#5e69d1"
  ink: "#f7f8f8"
  ink-muted: "#d0d6e0"
  ink-subtle: "#8a8f98"
  ink-tertiary: "#62666d"
  canvas: "#010102"
  surface-1: "#0f1011"
  surface-2: "#141516"
  surface-3: "#18191a"
  surface-4: "#191a1b"
  hairline: "#23252a"
  hairline-strong: "#34343a"
  hairline-tertiary: "#3e3e44"
  semantic-success: "#27a644"
  semantic-danger: "#eb5757"

typography:
  headline:
    fontFamily: Microsoft JhengHei UI
    fontSize: 28px
    fontWeight: 600
    lineHeight: 1.20
  card-title:
    fontFamily: Microsoft JhengHei UI
    fontSize: 22px
    fontWeight: 500
    lineHeight: 1.25
  body:
    fontFamily: Microsoft JhengHei UI
    fontSize: 16px
    fontWeight: 400
    lineHeight: 1.50
  body-sm:
    fontFamily: Microsoft JhengHei UI
    fontSize: 14px
    fontWeight: 400
    lineHeight: 1.50
  caption:
    fontFamily: Microsoft JhengHei UI
    fontSize: 12px
    fontWeight: 400
    lineHeight: 1.40
  eyebrow:
    fontFamily: Microsoft JhengHei UI
    fontSize: 13px
    fontWeight: 500
    lineHeight: 1.30
  button:
    fontFamily: Microsoft JhengHei UI
    fontSize: 14px
    fontWeight: 500
    lineHeight: 1.20
  mono:
    fontFamily: Cascadia Mono
    fontSize: 13px
    fontWeight: 400
    lineHeight: 1.50

rounded:
  xs: 4px
  sm: 6px
  md: 8px
  lg: 12px
  pill: 9999px

spacing:
  xxs: 4px
  xs: 8px
  sm: 12px
  md: 16px
  lg: 24px
  xl: 32px
  xxl: 48px

components:
  bubble:
    backgroundColor: "{colors.surface-1}"
    textColor: "{colors.ink}"
    typography: "{typography.body-sm}"
    rounded: "{rounded.lg}"
    padding: 16px
  settings-group:
    backgroundColor: "{colors.surface-1}"
    textColor: "{colors.ink}"
    typography: "{typography.body-sm}"
    rounded: "{rounded.lg}"
    padding: 14px 16px
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    typography: "{typography.button}"
    rounded: "{rounded.md}"
    padding: 8px 14px
  button-secondary:
    backgroundColor: "{colors.surface-1}"
    textColor: "{colors.ink}"
    typography: "{typography.button}"
    rounded: "{rounded.md}"
    padding: 8px 14px
  button-danger:
    backgroundColor: "{colors.surface-1}"
    textColor: "{colors.semantic-danger}"
    typography: "{typography.button}"
    rounded: "{rounded.md}"
    padding: 8px 14px
  text-input:
    backgroundColor: "{colors.canvas}"
    textColor: "{colors.ink}"
    typography: "{typography.body-sm}"
    rounded: "{rounded.md}"
    padding: 8px 12px
  segment-selected:
    backgroundColor: "{colors.surface-3}"
    textColor: "{colors.ink}"
    typography: "{typography.button}"
    rounded: "{rounded.pill}"
    padding: 6px 14px
---

## Overview

沿用全域 DESIGN.md（linear.app 基底）的深色 token：canvas 打底、surface 階梯分層、hairline 框線、lavender `{colors.primary}` 只用在主要按鈕、選取狀態與焦點。這份只記和全域不同的地方。

## 補充的 token

- `{colors.semantic-danger}` #eb5757：輸入錯誤、移除連結這類破壞性動作。全域沒有錯誤色；在 surface-1 上對比 5.6:1。
- 差異檢視的底色：success／danger 加 15% 不透明度（success-tint、danger-tint），只當底色，上面的字仍用 ink。
- 焦點外框：`{colors.primary-focus}` 50% 不透明度、2px，只在鍵盤操作時出現。

## 對比（WCAG AA）

- 內文 ink、ink-muted、ink-subtle 在 canvas／surface-1 上都 ≥ 5.8:1；ink-tertiary（3.3:1）不拿來放文字。
- 開關關閉時用 ink-subtle 外框與圓點，元件邊界才有 3:1（hairline-strong 只有 1.5:1）。
- 主要按鈕白字在 primary 上 4.8:1。

## Windows 上的取捨

- 字型：Linear 的字型拿不到。中文介面以 Microsoft JhengHei UI 為主（數字和中文同一套字重，不會一粗一細），倒數計時的純數字用 Segoe UI 等寬數字。WPF 沒有字距設定，不做負字距。
- 桌寵、泡泡是透明置頂視窗：不加陰影，深色卡片配 hairline-strong 框線在任何桌布上都看得清楚。
- 系統關掉動畫效果時，泡泡直接出現、不探頭、不垂降。
