# S4 Audit Report: Slimming Tier 2 (Evidence & Findings)

This document records the evidence, static audits, and actions taken during Section S4 (Slimming, Tier 2) on branch `chore/stale-ui-and-slim`.

---

## 1. XAML Resource Dictionaries Audit

We performed a complete cross-referencing audit of all 56 resource keys across `Styles/FontSizes.xaml`, `Styles/Thickness.xaml`, `Styles/TextBlock.xaml`, and `Styles/InstrumentTheme.xaml` against every view and control in `LockScreenGif`.

### Findings:
1. **`FontSizes.xaml` (8 keys):**
   - Keys: `LargeFontSize`, `MediumFontSize`, `AppFontSizeTitle`, `AppFontSizeSubtitle`, `AppFontSizeBodyStrong`, `AppFontSizeBody`, `AppFontSizeCaption`, `AppFontSizeReadout`.
   - All 8 keys are **100% duplicated** in `InstrumentTheme.xaml`.
   - Because `App.xaml` merges `InstrumentTheme.xaml` last, the definitions in `FontSizes.xaml` are immediately superseded.
2. **`TextBlock.xaml` (7 styles):**
   - Styles: `PageTitleStyle`, `AppPageTitleStyle`, `SectionTitleStyle`, `AppSectionTitleStyle`, `SettingTitleStyle`, `SettingDescriptionStyle`, `BodyTextStyle`.
   - All 7 styles are **100% duplicated** in `InstrumentTheme.xaml`.
3. **`Thickness.xaml` (39 keys):**
   - 19 modern scale primitives (`AppSpacingXXS`–`AppSpacingXL`, `AppPageMargin`, `AppMarginPage`, `AppPageMaxWidth`, `AppCardSpacing`, `AppPadding*`, `AppSetting*`) are duplicated in `InstrumentTheme.xaml`.
   - 20 backward-compatible margin definitions (`LargeTopMargin`, `LargeTopBottomMargin`, `MediumTopMargin`, `MediumTopBottomMargin`, `MediumLeftRightMargin`, `MediumBottomMargin`, `SmallLeftMargin`, `SmallLeftRightMargin`, `SmallTopMargin`, `SmallRightMargin`, `SmallTopBottomMargin`, `XSmallLeftMargin`, `XSmallTopMargin`, `XSmallLeftTopRightBottomMargin`, `XXSmallTopMargin`, `XXSmallLeftTopRightBottomMargin`, `NavigationViewContentGridBorderThickness`, `NavigationViewContentGridCornerRadius`, `MenuBarContentMargin`, `SettingsPageHyperlinkButtonMargin`):
     - **Code & XAML references:** Exactly **0**.
     - These were dead template leftovers from legacy Windows Template Studio boilerplate.

### Safety Check with Frozen Installer:
- We inspected `Installer/LockscreenGif.aip` lines 684–687 and discovered that `FontSizes.xbf`, `Thickness.xbf`, and `TextBlock.xbf` are explicitly tracked components in `Styles_Dir`.
- Completely deleting those `.xaml` files from the project would cause MSBuild to omit the `.xbf` outputs from `artifacts/app/Styles/`, which would break the frozen MSI build.
- **Action Taken:** We removed the 20 dead unreferenced keys from `Thickness.xaml`. `Thickness.xbf` continues to compile cleanly for the installer while eliminating dead boilerplate.

---

## 2. NuGet Dependencies Audit

We audited all 9 package references in `LockScreenGif/LockscreenGif.csproj`:

| Package | Version | Usage Status | Shipped in Installer (`LockscreenGif.aip`) | Verdict |
|---|---|---|---|---|
| `CommunityToolkit.Mvvm` | 8.4.0 | Active (ViewModels, Commands) | Yes | Keep |
| `FFMpegCore` | 5.4.0 | Active (Media indexing & pipeline) | Yes | Keep |
| `Gifski.Net` | 1.2.0 | Active (GIF rendering & p/invoke) | Yes | Keep |
| `Microsoft.Extensions.Hosting` | 10.0.2 | Active (DI Host in `App.xaml.cs`) | Yes | Keep |
| `Microsoft.WindowsAppSDK` | 1.8.260101001 | Active (WinUI 3 core) | Yes | Keep |
| `WindowsDisplayAPI` | 1.3.0.13 | Active (Multi-monitor wallpaper) | Yes | Keep |
| `WinUIEx` | 2.9.0 | Active (WindowEx, window lifecycle) | Yes | Keep |
| `System.DirectoryServices.AccountManagement` | 10.0.2 | Passive / Legacy user check | **Yes** (3 DLLs tracked by GUID in AIP) | **Proposal in S5** |
| `Microsoft.Xaml.Behaviors.WinUI.Managed` | 3.0.1 | Passive / Unreferenced in modern XAML | **Yes** (2 DLLs tracked by GUID in AIP) | **Proposal in S5** |

### Critical Finding on Frozen Area:
Because `Installer/LockscreenGif.aip` is explicitly marked **FROZEN** under ground rule #2:
Removing `System.DirectoryServices.AccountManagement` or `Microsoft.Xaml.Behaviors.WinUI.Managed` from `LockscreenGif.csproj` without touching `LockscreenGif.aip` would produce missing file errors during the Advanced Installer build. Therefore, these packages remain intact in code, and an explicit proposal to update the installer layout is presented in S5.

---

## 3. Remote Stale Branches Audit

Inspection of remote branches on `SubhamPro11/lockscreengtf` revealed 7 stale Copilot branches:
- `remotes/origin/copilot/fix-check-failing-job`
- `remotes/origin/copilot/fix-failing-github-actions-build-job`
- `remotes/origin/copilot/fix-failing-github-actions-job`
- `remotes/origin/copilot/fix-github-actions-build-job`
- `remotes/origin/copilot/fix-github-actions-build-job-again`
- `remotes/origin/copilot/fix-github-actions-build-job-another-one`
- `remotes/origin/copilot/fix-github-actions-build-job-yet-again`

These branches were created during earlier automated fix attempts and can be safely pruned from GitHub origin when desired.
