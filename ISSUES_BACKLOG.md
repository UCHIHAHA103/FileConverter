# FileConverter 用户反馈汇总与修复优化清单

> **数据来源**：原作者仓库 [Tichau/FileConverter](https://github.com/Tichau/FileConverter) 的 Issues 与 Pull Requests  
> **抓取范围**：Issues 前 10 页（≈ 130 条 Open） + 全部 7 个 Open PR + 关键已关闭 PR  
> **抓取时间**：2026-04-26  
> **维护仓库**：[UCHIHAHA103/FileConverter](https://github.com/UCHIHAHA103/FileConverter)（fork）  
> **文档用途**：本 fork 的修复路线图，按优先级推进

---

## 目录
1. [总览统计](#一总览统计)
2. [🔥 可直接合并的社区 PR（低风险高收益）](#二-可直接合并的社区-pr低风险高收益)
3. [P0 高优先级 Bug](#三-p0-高优先级-bug)
4. [P1 中优先级 Bug](#四-p1-中优先级-bug)
5. [P2 功能请求与优化建议](#五-p2-功能请求与优化建议)
6. [安装器 / 更新器 / 右键菜单问题](#六-安装器--更新器--右键菜单问题)
7. [建议的修复迭代顺序](#七-建议的修复迭代顺序)
8. [开发约定](#八-开发约定)

---

## 一、总览统计

### 1.1 Issues 分类（10 页共 ~130 条 Open）

| 大类 | 数量 | 典型 issue 编号 |
|---|---:|---|
| 🌐 语言 / 本地化失效 | **10+** | #750 #735 #690 #673 #667 #646 #609 #606 #593 #692 |
| ⚙️ 设置窗口打不开 / 设置不保存 / 数值不生效 | **8** | #741 #682 #600 #570 #529 #435 #363 #274 |
| ⚠️ 转换卡死 / 冻结 / FFmpeg 残留进程 | **7** | #749 #740 #739 #716 #703 #700 #711 |
| ❌ 格式转换失败（含 MKV/DOCX/MP3/PDF 等） | **15+** | #748 #745 #717 #715 #714 #713 #709 #705 #643 #631 #577 #572 #561 #560 #452 |
| 🐛 功能性 Bug（动图丢帧/元数据丢失/音轨错/缩放 etc） | **11** | #746 #719 #691 #676 #640 #605 #599 #568 #513 #510 #289 |
| 🖱️ 右键菜单 / Shell 扩展问题 | **7** | #692 #685 #675 #645 #633 #604 #566 #516 |
| 📦 安装 / 打包 / 升级问题 | **5** | #721 #579 #526 #450 #288 |
| 🔄 自动更新失败 | **2** | #747 #689 |
| 🚀 性能 / 队列 / 批处理增强 | **7** | #704 #701 #697 #613 #519 #518 #429 |
| 💡 格式扩展功能请求 | **15+** | #744 #743 #694 #687 #668 #636 #612 #611 #602 #601 #571 #563 #454 #446 #443 #438 #437 #427 #386 #375 #369 #267 #259 |
| 🧩 平台 / 生态扩展 | **5** | #742 #671 #669 #641 #695 #400 #364 |
| 📑 字幕 / 特殊场景 | **2** | #632 #375 |

### 1.2 Pull Requests（共 7 个 Open + 2 个已关闭但有价值）

| PR | 类型 | 关联 Issue | 合并价值 | 状态 |
|---|---|---|---|---|
| **#732** FFmpeg stdout pipe 死锁修复 | 🔴 Bug Fix | #749 #740 #739 #716 #703 #700 | ⭐⭐⭐⭐⭐ | ✅ 已合入 |
| **#702** GPU 失败自动回退软件编码 | 🟠 增强 | #713 #691 #572 | ⭐⭐⭐⭐ | ✅ 已实现 |
| **#699** UpgradeService null 异常修复（Closed 未合并） | 🟠 Bug Fix | #747 | ⭐⭐⭐⭐ | ✅ 已合入 |
| **#698** 简体中文翻译更新 | 🟢 i18n | #750 #667 #692 | ⭐⭐⭐ | ✅ 已合入 |
| **#712** 意大利语翻译更新（Closed 未合并） | 🟢 i18n | - | ⭐⭐ | ✅ 已合入（修复 XML bug） |
| **#707** 新增加泰罗尼亚语（Closed 未合并） | 🟢 i18n | - | ⭐⭐ | ✅ 已合入 |
| **#562** ALAC 编码支持 | 🟢 功能 | #437 | ⭐⭐⭐ | ✅ 已实现 |

---

## 二、🔥 可直接合并的社区 PR（低风险高收益）

这些 PR 都是别人已经写好、原作者迟迟不合并的，fork 后可以快速 review 并合入：

### 🚨 PR #732 — FFmpeg stdout pipe 死锁修复（**强烈建议第一个合并**）

**作者**：HapppppyMoon｜**日期**：2026-03-23｜**状态**：Open 未合并

**根因（摘自 PR 正文）**：
- `-progress pipe:1` 把进度数据写到 stdout
- 但 `Convert()` 只读 stderr，`RedirectStandardOutput=true` 却无人消费
- stdout 管道缓冲区填满（4–64KB）→ ffmpeg 写阻塞 → 进程挂起
- 输出文件没有 moov atom → MP4 无法播放

**改动**（仅 2 行）：
```
ConversionJob_FFMPEG.cs
  baseArgs: "-n -progress pipe:1"  →  "-n"
  RedirectStandardOutput: true     →  false
```

**一次修复 6 个 issue**：#749 #740 #739 #716 #703 #700。这是本 fork 要做的第一件事。

### 🔧 PR #699 — UpgradeService Bug 修复

**作者**：bharatvansh｜**状态**：Closed 未合并（需 cherry-pick）

修复两个 bug：
1. `CheckForUpgrade` 中 `await task` 前未判空 → NullReferenceException
2. `DownloadLatestVersionDescription` 错误引用 `Helpers.BaseURI`（编译错）→ 改为 `UpgradeService.BaseURI`

对应 issue：#747（无法更新到 2.2）。

### 🎮 PR #702 — GPU 编码失败自动回退软件编码

**作者**：bharatvansh｜**状态**：Open

新增设置项 `AutoRetrySoftwareEncodingOnGpuFailure`（默认开），NVENC/CUDA 失败时自动 libx264 重试。作者提供了截图与测试。

**修复 issues**：#713（NVENC 滤镜错）、#691（CUDA 画质破坏）、#572（CUDA 硬件错）。

### 🌏 PR #698 — 简体中文翻译更新

**作者**：sr093906｜**状态**：Open｜**仅 1 个 resx 文件改动**

中文用户翻译完善。合并前需 verify 翻译质量。

### 📼 PR #562 — ALAC 支持

**作者**：UniversalThrowawayaccount｜**状态**：Open

新增 ALAC（Apple Lossless）音频预设，实现 Issue #437。已验证 flac → m4a(ALAC) 转换成功，但缺少比特率↔质量的映射表。

### 🌐 PR #712 / #707 — 意大利语 / 加泰罗尼亚语

都是 Closed 未合并状态，纯翻译，风险极低。cherry-pick 后可补齐语言包。

---

## 三、P0 高优先级 Bug

### 3.1 ✅ 语言设置不生效（已在 fork 中修复）

> **修复版本**：v2.2.7 fork

| # | 标题 | 日期 |
|---|---|---|
| #750 | 语言设置无效（保存后窗口关闭） | 2026-04-25 |
| #735 | Language fails to update after selection | 2026-03-27 |
| #692 | 右键菜单与语言转换无作用 | 2026-01-29 |
| #690 | Language toggle is not working | 2026-01-26 |
| #673 | Language localization does not work correctly | 2025-11-26 |
| #667 | 2.1 版本中文简体使用不了 | 2025-11-12 |
| #646 | Regarding language display issues | 2025-10-02 |
| #609 | Language settings do not take effect | 2025-06-21 |
| #606 | 语言设置不生效 | 2025-06-16 |
| #593 | Language settings are not working | 2025-05-10 |
| ~~#737~~ | ~~2.2 语言无效~~（Closed 但未真修） | 2026-04-09 |

**已修复依据**：
- `Settings.cs` 第 89-107 行：`ApplicationLanguage.set` 同时设置 `CurrentCulture`、`CurrentUICulture`、`Properties.Resources.Culture`，并用 try/catch 包裹（注释引用 #750）
- `Helpers.cs` 第 226-284 行：`GetSupportedCultures()` 同时探测 `Languages/<culture>/` 和 `<exe>/<culture>/` 路径（注释引用 #593 #609 #646 #667 #673 #690 #692 #735 #737 #750）
- `Application.xaml.cs` 第 904-943 行：`OnAssemblyResolve` 处理卫星程序集加载
- `SettingsViewModel.cs` 第 504-519 行：语言变更后自动重启应用

**修复任务（T-L）**：
- [x] **T-L1** 定位 Settings.Save() → Settings.xml 写入路径 — ✅ 已实现
- [x] **T-L2** 定位启动时 Settings.Load() → ApplicationLanguage 加载路径 — ✅ 已实现
- [x] **T-L3** 复现 #750 "保存后窗口自动关闭" — ✅ 已用 try/catch 保护
- [x] **T-L4** 检查 CurrentUICulture 切换后 ResourceDictionary 是否重载 — ✅ 已设置 Resources.Culture + 重启
- [x] **T-L5** 若需重启才生效，则增加 "需重启" 提示 — ✅ 保存后自动重启
- [x] **T-L6** 合入 PR #698（简体中文）、#712（意大利语）、#707（加泰罗尼亚语）— ✅ 已合入
- [ ] **T-L7** 补齐波兰语（#638）

### 3.2 ✅ v2.2.3+ 右键菜单消失根因（已在 v2.2.7 修复）

> **修复版本**：v2.2.7（2026-04-28）

**元凶**：`Settings.default.xml` 中新增的 preset `Images to Video/To Mp4 (24fps)`（Stage 1, commit `33340d0`）。

**完整失败链**：
1. MSI deferred CA 运行 `FileConverter.exe --post-install-init`
2. 主程序加载 `Settings.default.xml` → `Settings` 类反序列化
3. `ConversionPreset.OnDeserializationComplete()` → `CoerceInputTypes()` 发现 **Mp4 OutputType 不兼容 Image 输入类别**（`Helpers.IsOutputTypeCompatibleWithCategory` 返回 false），**将该 preset 的全部 7 个 InputTypes（png/jpg/bmp/tif/tiff/jpeg/webp）全部删除**
4. `Save()` 把"清理后"的 Settings 写到 `%LocalAppData%\FileConverter\Settings.user.xml`。该 preset 的 `InputTypes` 列表为空，**序列化后完全没有 `<InputTypes>` 元素**
5. explorer/DOpus 加载 shell extension → `FileConverterExtension` 优先读 `Settings.user.xml` → 对应 preset 的 `PresetReference.InputTypes` 反序列化为 **null**（`string[]` 类型无 XmlArrayItem attribute，缺失元素 = null）
6. `CanShowMenu()` → `presetReference.InputTypes.Contains(extension)` → **NullReferenceException**
7. SharpShell/COM 捕获异常，shell ext 被认为不可用 → **整个 File Converter 右键菜单消失**

**定位过程**：基于 v2.2.2 baseline 建立 12 个独立 bisect 分支（stage2–stage9 + stage1a/1b/1c），每个 stage 单独叠加一个功能模块后 CI 构建 MSI 逐个安装测试，最终锁定 Stage 1 的 `Settings.default.xml` 改动。再通过检查安装后实际的 `Settings.user.xml` 文件，确认了 `Images to Video_To Mp4` preset 的 InputTypes 被清空的事实。

**修复措施（v2.2.7）**：
1. shell ext `CanShowMenu` / `RefreshPresetList` 添加 null-guard（保底）
2. `LoadExtensionSettingsIfNecessary` 加载后过滤掉 `InputTypes==null/empty` 的无效 preset
3. `CreateMenu` per-preset try-catch + 外层 try-catch（单个 preset 出错不影响整体）
4. 从 `Settings.default.xml` 中移除不兼容的 `Images to Video/To Mp4` preset（根治）

### 3.3 ✅ v2.2 转换卡死 / FFmpeg 残留（已在 v2.2.7 修复，PR #732 合入）

| # | 场景 |
|---|---|
| #749 | mp4→mp4 降质量、wav→mp3 卡死 |
| #740 | m4a→mp3 永不完成 |
| #739 | 视频压缩冻结 |
| #716 | mp4 低质量转换冻结 |
| #703 | 转换中途停止 |
| #700 | 长转换进度条出现后冻结 |
| #711 | mp4→mp3 卡死 |

**修复任务（T-P）**：
- [x] **T-P1** 合并 PR #732（直接修根因）— ✅ v2.2.7 已合入
- [x] **T-P2** 在取消按钮中向 ffmpeg stdin 发送 `q` 实现优雅终止；超时 `Process.Kill(entireProcessTree: true)` — ✅ v2.2.7 已实现
- [x] **T-P3** 任务完成 / 异常 / 取消三路径统一释放文件句柄 — ✅ RunAllPasses 循环顶部加 CancelIsRequested break + 关窗取消所有任务
- [ ] **T-P4** 增加取消回归测试（启动 mp4 转码 5 秒后取消，断言无 ffmpeg 残留 + 输出可删）

### 3.4 ✅ 设置不保存 / 设置窗口打不开（已在 fork 中修复）

| # | 标题 | 类型 |
|---|---|---|
| #741 | Configured CRF values not being respected | 数值不生效 |
| #682 | not save | 不保存 |
| #600 | 无法打开设置 | 打不开 |
| #570 | I can't open the settings of the File Converter | 打不开 |
| #363 | Unable to open settings | 打不开 |
| #435 | Program settings fail to open | 打不开 |
| #529 | Settings in v2.0.2 cannot be saved | 不保存 |
| #274 | Save is greyed out, and FC is ignoring xml | 不保存 |

**已修复依据**：
- `SettingsService.cs` 第 150-179 行：损坏设置文件自动备份 + 提示用户是否删除重建
- `SettingsViewModel.cs` 第 486-530 行：`SaveSettings()` 整个包裹在 try/catch，失败时显示 MessageBox
- `ConversionJob_FFMPEG.cs` 第 271+287 行：CRF 从 `ConversionPreset.GetSettingsValue<int>(VideoQuality)` 动态读取（`H264QualityToCRF = 51 - quality`），**不是硬编码**
- `SettingsService.cs` 第 224-228 行：先写临时文件再拷贝的安全保存策略

**修复任务（T-S）**：
- [x] **T-S1** 与 T-L1 共同排查 Settings 持久化链路 — ✅ 已完成
- [x] **T-S2** 启动时 settings.xml 损坏兜底：备份 + 重置到默认 — ✅ 已实现
- [x] **T-S3** CRF 预设：从配置读取而非硬编码 — ✅ 代码确认不是硬编码
- [x] **T-S4** 设置窗口打不开 → 加全局异常捕获 + 日志 — ✅ SaveSettings/CloseSettings 均有 try/catch

---

## 四、P1 中优先级 Bug

### 4.1 🟠 右键菜单 / Shell 扩展问题（部分已修复）

| # | 描述 | 状态 |
|---|---|---|
| #692 | 右键菜单与语言转换无作用 | ✅ v2.2.7 修复 |
| #685 | Convert 菜单项突然不显示 | ✅ v2.2.7 修复（null-guard + sanitize） |
| #675 | Context Menu Lag Caused by File Conversion | ✅ 已修复（Icon bitmap 缓存，消除 GDI 对象重复创建） |
| #645 | Directory Opus Explorer 中右键菜单不显示 | ✅ v2.2.7 修复（PathHelpers fallback） |
| #633 | Context menu 中不出现 | ✅ v2.2.7 修复 |
| #604 | Convert 选项从右键菜单消失 | ✅ v2.2.7 修复 |
| #566 | 重装后右键菜单只显示一次 + 文件管理器崩溃 | ✅ v2.2.7 修复（多层防御） |
| #516 | Shell extension 被禁用导致不显示 | ✅ v2.2.7 修复 |
| #642 | 无法获取可执行路径 | ✅ v2.2.7 修复（PathHelpers DLL 目录 fallback） |

**修复任务（T-CTX）**：
- [x] **T-CTX1** 审查 Shell Extension 的 COM 注册路径 — ✅ CleanupShellExtensionRegistry + ForceDeleteOnUninstall
- [x] **T-CTX2** Shell Extension 注册时写入 Approved + 清除 Blocked — ✅ RegisterShellExtension() 自动处理（防止 Windows 自动禁用后右键菜单消失）
- [x] **T-CTX3** 上下文菜单延迟（#675）→ CanShowMenu 改用 HashSet O(1) 查找替代 O(m×n) 嵌套循环 — ✅

### 4.2 🟠 格式转换失败（分类整理）

**视频类**：
- #748 MKV → MP4 失败 — ✅ 已修复（-fflags +genpts 修复 broken PTS）
- #713 NVENC/CUDA + scaling 滤镜错（PR #702 覆盖）
- #709 AVI → MP4 失败 — ✅ 已修复（-fflags +genpts）
- #678 mp4 → ogv 失败 — ✅ 已修复（Theora 加 format=yuv420p）
- #577 MKV 文件转换错误 — ✅ 已修复（-fflags +genpts）
- #572 NVIDIA CUDA 硬件错（PR #702 覆盖）
- #560 视频转音频 ffmpeg 错 — ✅ 已修复（所有音频 case 加 -vn）
- #452 "Invalid data found when processing input" — ✅ 已修复（扩展容错范围 EndsWith→Contains）

**音频类**：
- #740 m4a → mp3（合并 PR #732）— ✅ 已修复
- #715 Opus packet header 解析错 — ✅ 已修复（-fflags +genpts 帮助容器修复）
- #711 mp4 → mp3 — ✅ 已修复
- #289 错误音轨被保留在输出（#438 用户希望能选音轨）

**文档类**：
- #745 docx 无法转 pdf
- #717 word → pdf 时崩溃
- #714 Docx 转换不工作
- #705 doc → pdf 更新后不工作
- #631 Docx 未被转为 PDF
- #368 "fail to open document with microsoft office"

**图像类**：
- #746 动画 webp → gif 变静态（→ Issue #640 同类：animated JPG 也丢帧）
- #719 HDR AVIF → PNG 丢 HDR
- #676 非透明 PNG — ✅ 已修复（FFmpeg PNG 输出加 -pix_fmt rgba 保留 alpha）
- #643 PDF → PNG 问题 — ✅ 已修复（PDF 页面设白色背景 + alpha remove）
- #561 PDF → PNG 无输出 — ✅ 已修复（同上）
- #568 HEIC → PNG 丢 EXIF — ✅ 已修复（ImageMagick AutoOrient + profile 保留）
- #251 docx → png 背景色错 — ✅ 已修复（PDF 中间产物加白色背景）
- #513 webp → gif 不再生成 gif

**修复任务（T-F）**：
- [x] **T-F1** 所有 ffmpeg/office 失败路径，**把 stderr 输出落到 `Logs/`** — ✅ `ConversionJob_FFMPEG.cs` 第 651-676 行 `WriteConversionLog` + `CleanOldLogs`
- [x] **T-F2** 动图类（WebP、animated JPG、animated PNG）统一加 `-loop 0` 分支 — ✅ GIF 输出加 -loop 0 确保无限循环
- [x] **T-F3** DOCX → PDF：检测本地 Word/LibreOffice，缺失时给明确提示 — ✅ 已有 `ErrorMicrosoftWordIsNotAvailable`
- [x] **T-F4** HDR AVIF → PNG：保留 color primaries / transfer function — ✅ 检测 depth>8 后 TransformColorSpace 到 sRGB + 嵌入 ICC profile
- [x] **T-F5** HEIC 元数据保留（EXIF、GPS）— ✅ ImageMagick AutoOrient + profile 保留（#568 #599）
- [x] **T-F6** MKV 多音轨 → 保留所有音轨（#438）— ✅ 加 -map 0:v:0 -map 0:a? 保留所有音频流

### 4.3 🟠 功能性 Bug（其他）

| # | 描述 | 状态 |
|---|---|---|
| #605 | 缩放 75% 不生效 | ✅ 已修复（scale format 0.## + scaleFactor=1 跳过 + >0 校验） |
| #599 | EXIF 数据丢失 | ✅ 已修复（ImageMagick AutoOrient + profile 保留） |
| #510 | 已选 "Move to Archive" 但仍附加 "(2)" 到文件名 | ✅ 确认为设计行为（Archive 目录已存在同名文件时防覆盖） |
| #674 | "Unable to extend cache - no space on device" | ✅ 已修复（PrepareConversion 加 DriveInfo 磁盘空间检查） |
| #642 | 上次更新后无法获取可执行路径 | ✅ v2.2.7 修复（PathHelpers fallback） |
| #455 | 多文件一起转换时的 bug | ✅ 确认代码正常（ConversionService 批量队列逐个处理） |
| #431 | 无输出 | ✅ 已修复（转换后校验输出文件，不存在则报错含具体路径） |
| #270 | MP4 缩略图不显示 | ✅ 已修复（-movflags +faststart 移动 moov atom 到文件头） |
| #258 | About 窗口中的链接失效 | ✅ 已修复（6 个 URL 从 Tichau → UCHIHAHA103 fork） |

---

## 五、P2 功能请求与优化建议

### 5.1 格式扩展请求（合集）

| # | 请求 | 状态 |
|---|---|---|
| #744 | PDF → DOC | |
| #743 | PNG/JPG → DDS | |
| #694 | EPUB ↔ ODT/FODT、PDF ↔ ODP/FODP | |
| #687 | OGG 预设支持 Opus（现仅 vorbis） | |
| #668 | 图像转成指定宽高比 | |
| #636 | 覆盖输出选项 | ✅ 已实现（FFmpeg -n→-y，去重由 GenerateUniquePath 处理） |
| #612 | JXL 支持 | |
| #611 | 自定义输出格式 | |
| #602 | TGA → PNG | |
| #601 | AIFC → WAV | |
| #571 | MP4 → PNG（提帧） | |
| #454 | CDR → PSD | |
| #446 | CBR ↔ CBZ ↔ PDF ↔ EPUB | |
| #443 | SVG 输出 | |
| #438 | 选择 MKV 音轨 | |
| #437 | ALAC 输出（**PR #562 已实现**） | ✅ |
| #427 | JPEG XL | |
| #386 | H.265 预设 | ✅ 已实现（Settings.default.xml 含 H.265/HEVC 自定义命令 preset） |
| #375 | 软字幕 → 硬字幕 | |
| #369 | HEIF/HEIC → JPEG/PNG/PDF | |
| #267 | EPS → SVG | |
| #259 | EPUB → PDF | |
| #632 | Whisper 字幕输出控制 | |

### 5.2 流程增强

| # | 请求 | 状态 |
|---|---|---|
| #704 | 多文件夹处理 | ✅ 已实现（DropFiles 展开目录递归获取文件） |
| #701 | 给已有队列追加新任务 | ✅ 已实现（ConversionService.RegisterConversionJob） |
| #697 | 更新时不要重新添加预设 | ✅ 已实现（PostInstallInit 合并逻辑，只删除旧默认、保留用户修改） |
| #613 | 基于文件夹的自动转换 | |
| #613 / #519 | Watch Folder 监视文件夹自动转换 | |
| #518 | 拖拽区域 | ✅ 已实现（MainWindow 添加拖拽提示水印） |
| #515 | 全局无损选项 | |
| #514 | 转换视频片段 | |
| #429 | 从队列中移除单项 | ✅ 已实现（ConversionService.RemoveConversionJob） |
| #522 | 右键命令行选项 | |
| #384 | 进度条 | ✅ 已实现（ParseFFMPEGOutput + ProgressBar UI） |
| #361 | 转换时关闭窗口未提示/中止 | ✅ 已实现（关窗确认对话框 + 取消所有活跃任务） |
| #708 | Expression 功能 |

### 5.3 平台 / 生态

| # | 请求 |
|---|---|
| #742 | Linux 版本（已有社区移植） |
| #671 | 合入 Microsoft PowerToys |
| #669 | Pandoc 集成 |
| #695 / #641 | 便携版 / scoop 支持 |
| #689 | 更新 Chocolatey |
| #400 | .NET port（移除 .NET Framework 依赖） |
| #364 | MS Office ↔ LibreOffice |

---

## 六、安装器 / 更新器 / 右键菜单问题

### 6.1 安装器

| # | 描述 | 状态 |
|---|---|---|
| #721 | v2.2 打包 bug | ✅ v2.2.7 修复（Return="ignore" + PostInstallShellFallback） |
| #579 | v2.1 安装器清空 Temp 目录（严重！） | ✅ 当前版本不存在此问题（INSTALLFOLDER=ProgramFiles64Folder，不涉及 Temp） |
| #526 | 安装 bug code 0xF | ✅ v2.2.7 修复（HandleEarlyCommandLineArgs + ExitEarlyProcess） |
| #450 | FileConverterExtension.DLL 注册表记录错误 | ✅ v2.2.7 修复（CleanupShellExtensionRegistry） |
| #288 | Windows 11 22H2 无法安装 | ❌ 未验证 |
| #396 | 卸载后仍残留文件 | ✅ v2.2.7 修复（三层清理 + ForceDeleteOnUninstall + uninstall.exe） |
| **NEW** | **卸载不干净 + 缺少 uninstall.exe** | ✅ v2.2.7 修复 |

#### 🔴 卸载优化需求（用户反馈 2026-04-28）— ✅ 已修复

**已解决问题**：
1. ✅ uninstall.exe 改为 WinExe（不再显示 CMD 窗口），用 MessageBox 提示
2. ✅ 控制面板卸载报错 2753 — 根因：UninstallShell CA 调度在 RemoveFiles 之后（exe 已被删），现改为 Before RemoveFiles
3. ✅ 三层注册表清理保障（RegAsm + CleanupShellExtensionRegistry + ForceDeleteOnUninstall）
4. ✅ 卸载时提供"清除用户数据"选项（RemoveSettingsDlg 对话框）

### 6.3 🔴 右键菜单消失 — 反复安装 MSI 后 COM 注册失效（2026-05-12）

> **状态**：✅ 已手动修复 + 规避方案已落地

#### 现象

多次安装/覆盖安装 MSI 后，资源管理器右键菜单中的"File Converter"选项完全消失。

#### 根本原因

MSI 的 Custom Action `RegisterShell`（调用 `FileConverter.exe --register-shell-extension <DLL路径>`）在某次安装时静默失败，导致以下注册表键全部丢失：

| 键 | 作用 |
|---|---|
| `HKCR\CLSID\{AF9B72B5-...}\InprocServer32` | COM 类注册，指向 `FileConverterExtension.dll` |
| `HKCR\*\shellex\ContextMenuHandlers\FileConverterExtension` | Explorer 加载 shell ext 的入口 |
| `HKLM\...\Shell Extensions\Approved\{AF9B72B5-...}` | 防止 Windows 自动禁用该 shell ext |

**静默失败的两个陷阱**：

1. **单实例 Mutex 拦截**：如果 FC 进程正在运行，`--register-shell-extension` 会被单实例机制（`SingleInstanceManager.TryAcquirePrimary`）截获，直接 `ForwardArgumentsToPrimaryAndExit()` 退出，**注册逻辑根本不执行**，进程退出码 0，完全静默。

2. **缺少 DLL 路径参数**：`HandleEarlyCommandLineArgs` 里的 `register-shell-extension` case 要求 `args[index+1]` 作为 DLL 路径，若仅传 `--register-shell-extension`（无第二参数），条件 `index < args.Length - 1` 不满足，跳过注册直接 `ExitEarlyProcess()`，同样静默失败。

#### 诊断方法

```powershell
# 检查 CLSID 是否存在（存在 = 注册正常）
reg query "HKCR\CLSID\{AF9B72B5-F4E4-44B0-A3D9-B55B748EFE90}"

# 检查 shellex 入口
reg query "HKCR\*\shellex\ContextMenuHandlers\FileConverterExtension"

# 检查 Approved 列表（搜 FileConverter）
reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved" | findstr /i "FileConverter"
```

任意一条返回 `ERROR: The system was unable to find the specified registry key or value.` → shell extension 未注册。

#### 修复命令（需管理员权限）

```powershell
# Step 1：关掉所有 FC 进程（避免单实例 Mutex 拦截注册）
Stop-Process -Name FileConverter -Force -ErrorAction SilentlyContinue
Start-Sleep 2

# Step 2：以管理员权限注册 shell extension（必须带 DLL 路径参数）
Start-Process -FilePath "C:\Program Files\File Converter\FileConverter.exe" `
    -ArgumentList "--register-shell-extension `"C:\Program Files\File Converter\FileConverterExtension.dll`"" `
    -Verb RunAs -Wait

# Step 3：验证（应出现 {AF9B72B5-...} 相关条目）
reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved" | findstr /i "FileConverter"

# Step 4：重启 Explorer 使注册生效
Stop-Process -Name explorer -Force; Start-Sleep 2; Start-Process explorer
```

验证成功标志（FC 日志 `%LOCALAPPDATA%\FileConverter\Logs\FileConverter.log`）：
```
Install and register shell extension: C:\Program Files\File Converter\FileConverterExtension.dll.
C:\Program Files\File Converter\FileConverterExtension.dll installed and registered.
Added to Shell Extensions\Approved.
Removed from Shell Extensions\Blocked.
```

#### 规避方案（已落地）

1. **每次 MSI 安装后自动补注册**：`my-github` skill 的项目档案 `references/projects/FileConverter.md` 已加入 `post_install_cmd` 字段，AI 执行 `install-latest` 动作时必须在 MSI 安装完成后执行注册命令。

2. **安装脚本中加入注册步骤**：今后所有"下载+安装"操作，在 `msiexec /i ... -Wait` 完成后追加：
   ```powershell
   Stop-Process -Name FileConverter -Force -ErrorAction SilentlyContinue
   Start-Sleep 1
   Start-Process "C:\Program Files\File Converter\FileConverter.exe" `
       -ArgumentList "--register-shell-extension `"C:\Program Files\File Converter\FileConverterExtension.dll`"" `
       -Verb RunAs -Wait
   ```

3. **潜在代码修复（TODO）**：`HandleEarlyCommandLineArgs` 中 `register-shell-extension` 缺少参数时应输出错误日志而非静默跳过；单实例检查应在识别到 `--register-shell-extension` 后直接 bypass Mutex，不走转发流程。

### 6.2 更新器

| # | 描述 | 状态 |
|---|---|---|
| #747 | 永远更新不到 2.2（PR #699 覆盖部分） | ✅ 修复（CheckForUpgrade null-guard） |
| #689 | Chocolatey 版本过旧 | ✅ 已创建 chocolatey/.nuspec + 安装/卸载脚本 |

✅ **fork 已处理**：`version.xml` URL 已指向 `UCHIHAHA103/FileConverter`；`UpgradeService.BaseURI` 已改为 fork 仓库地址。

---

## 七、建议的修复迭代顺序

### 🚀 Sprint 1：发布 **v2.2.8 hotfix**（进行中）

> 目标：合并他人已写好的 PR，快速解决 v2.2 回归与更新崩溃

1. ✅ ~~**合并 PR #732**（FFmpeg 死锁）→ 一次性修复 7 个卡死 issue~~（已在 v2.2.7 中完成）
2. ✅ ~~**Cherry-pick PR #699**（UpgradeService null 修复）~~（已修复：CheckForUpgrade await null task 防护）
3. ✅ ~~**T-U**：更新 `version.xml` 中的 URL 指向 fork releases~~（已完成）
4. ✅ ~~**T-U2**：UpgradeService.BaseURI 指向 fork 仓库~~（原指向 Tichau/FileConverter）
5. ✅ ~~**T-P2 ~ T-P4**：补充 ffmpeg 优雅终止 + 进程清理~~（已在 v2.2.7 中完成）
6. - [ ] 发布 **v2.2.8**

### 🚀 Sprint 2：~~发布 **2.3.0 "Settings Fix"**~~（✅ 已完成）

> 目标：根治语言 / 设置持久化问题（**最多用户抱怨的痛点**）

1. ✅ ~~**T-L1 ~ T-L5**：语言设置不生效根治~~（已在 fork 中修复）
2. ✅ ~~**T-S1 ~ T-S4**：设置窗口打不开 / 不保存 / CRF 不生效~~（已在 fork 中修复）
3. ✅ ~~**合并 PR #698 / #712 / #707**：翻译补全~~（已合入 + 修复 #712 XML 标签 bug）
4. - [ ] 发布版本

### 🚀 Sprint 3：发布 **2.3.1 "Format Fixes"**（大部分已完成）

> 目标：格式转换 bug 批量修复

1. ✅ ~~**T-F1**：失败路径写日志到 `Logs/`~~（已实现 WriteConversionLog + CleanOldLogs）
2. ✅ ~~**T-F2**：动图 WebP / 动画 JPG 保留帧~~（已实现：animated webp 跳过 PNG 中间步骤直接用 FFmpeg）
3. ✅ ~~**T-F3**：DOCX 转换友好提示~~（已有 `ErrorMicrosoftWordIsNotAvailable` 等提示）
4. ✅ ~~**合并 PR #702**：GPU 自动回退软编~~（已实现 TrySoftwareFallback）
5. ✅ ~~**合并 PR #562**：ALAC 支持~~（已手动实现，OutputType.Alac + .m4a 输出）
6. - [ ] 发布版本

### 🚀 Sprint 4：发布 **2.4.0 "Enhancement"**（进行中）

> 目标：右键菜单、批处理、队列增强

1. ✅ ~~**T-CTX1 ~ T-CTX3**：右键菜单修复~~（全部 9 个 issue 已修复，含 #675 延迟优化）
2. - [ ] Watch Folder（#519 / #613）
3. ✅ ~~Queue 管理（#701 / #429）~~（ConversionService.RemoveConversionJob 已实现）
4. ✅ ~~进度条（#384）~~（ParseFFMPEGOutput 解析 time= 计算进度，UI ProgressBar 已绑定）

### 🚀 Sprint 5+：按需实现高票功能请求

- 高票格式：~~H.265 预设（#386）~~✅、HEIC→JPEG（#369）、Opus（#687）
- PowerToys 集成（#671）、Pandoc 集成（#669）

---

## 八、开发约定

- 每个任务编号（如 `T-L1`、`T-P2`）对应一个独立 commit，message 格式：`fix(T-L1): ...`
- 合并社区 PR 时保留原作者 commit author，但用 `Co-authored-by` 方式保留贡献信息
- 每次发布前手动回归 3 项：**语言切换 + CRF 设置 + mp4 转码取消**
- 版本号规则：
  - `2.2.x` = hotfix（仅修 bug，无 schema 变更）
  - `2.3.x` = settings 持久化重构
  - `2.4.x` = 功能增强
- `Settings.xml` 格式变更必须向后兼容原作者 2.x 格式
- 所有错误路径必须写日志到 `%AppData%/FileConverter/Logs/`

---

## 九、数据抓取摘要

| 指标 | 数量 |
|---|---:|
| 抓取的 issues 页数 | 10 |
| 独立 open issues | ~176 |
| open pull requests | 7 |
| 已 review 的关键 closed PR | 2（#699 #712 #707） |
| 可直接合并的 PR 总数 | **6** |
| 一并修复的 issue（合完 6 个 PR 后） | **~15 个** |

---

## 十、第 11-13 页 Issues 补充（2017-2021 早期积压）

> 抓取时间：2026-04-27  
> 覆盖范围：第 11 页（25 条） + 第 12 页（12 条） + 第 13 页（9 条） = **46 条**

### 10.1 第 11 页（2021 年）

| # | 标题 | 标签 | 日期 |
|---|---|---|---|
| #205 | Not Showing in Context Menu in Xyplorer | new feature | 2021-11-08 |
| #204 | WEBA to other audio files | new feature | 2021-11-06 |
| #201 | Future request, convert to jpg, set size | new feature | 2021-10-25 |
| #200 | Conversion from Word to PDF no longer with hyperlinks | bug | 2021-10-16 |
| #194 | Mp4 conversion isn't supported on some Social media sites like Instagram | new feature | 2021-09-22 |
| #189 | Feature Request: Support for AutoCAD Drawing file formats convert to pdf | new feature | 2021-09-05 |
| #184 | Error extracting cda to mp3 | bug | 2021-07-31 |
| #181 | word to pdf = error 21-07-2021 | need more info | 2021-07-21 |
| #180 | Feature Request: Support for Corel .psp and .pspimage file formats | need more info | 2021-07-14 |
| #177 | Allow to specify mp4 quality and scale in exact numbers | new feature | 2021-07-11 |
| #175 | MP4 files not playable in Whatsapp | new feature | 2021-07-05 |
| #174 | Extract documents and images to plain text | new feature | 2021-06-18 |
| #171 | Feature request: Aspect Ratio | new feature | 2021-05-27 |
| #170 | ico has bad quality | bug | 2021-05-21 |
| #169 | Add .txt to document options | new feature | 2021-05-20 |
| #168 | Can't find the output file(s) when down sampling to wav 8bit | bug | 2021-05-20 |
| #165 | Conversion options do not display | bug | 2021-05-08 |
| #164 | Export Failed Because this feature is not installed | need more info | 2021-05-06 |
| #161 | I get the "Error opening filters!" | bug | 2021-04-13 |
| #159 | Error while opening encoder for output stream | bug | 2021-03-27 |
| #155 | Fail to open document with Microsoft Office (Office 10) | — | 2021-03-11 |
| #153 | Please Add Webp Lossless preset | new feature | 2021-02-27 |
| #152 | Not working on my windows 10 | bug | 2021-02-22 |
| #150 | Any non square image comes out smaller than 256x256 icons | new feature | 2021-02-17 |
| #146 | Delete to recycle bin / .ts support / multi user / vid2mp3 album art | new feature | 2021-01-18 |

### 10.2 第 12 页（2020-2021 年）

| # | 标题 | 标签 | 日期 |
|---|---|---|---|
| #144 | After convert, prompt to delete | new feature | 2021-01-14 |
| #142 | Animated gif frame times not always the same when resizing | bug | 2021-01-08 |
| #133 | Crash without converting anything (PDF → JPG) on Windows 10 | need more info | 2020-11-28 |
| #129 | SVG to PNG doesn't respect alpha channel | bug | 2020-10-22 |
| #122 | ARM64 support? | new feature, help wanted | 2020-08-17 |
| #121 | Portable version? | new feature | 2020-08-12 |
| #119 | Please add .mhtml to type of file to convert | new feature | 2020-08-07 |
| #117 | Hidden mode for CLI | new feature | 2020-07-13 |
| #112 | DOC(X) 转换到 PDF 出现错误 | bug | 2020-05-19 |
| #111 | Converting from/to .opus audio files | new feature | 2020-05-16 |
| #109 | Subtitle extensions support | new feature, help wanted | 2020-05-15 |
| #106 | Installer fails | bug, need more info | 2020-03-28 |

### 10.3 第 13 页（2017-2018 年，最早期积压）

| # | 标题 | 标签 | 日期 |
|---|---|---|---|
| #54 | Rotation Not Working From mp4 to mp4 | bug — ✅ 已修复（旋转后清除 metadata rotate 防二次旋转） | 2018-07-01 |
| #45 | Allow support to wma in audio conversions | need more info | 2018-03-10 |
| #44 | Allow user to specify file extension | need more info | 2018-03-01 |
| #42 | CMYK to RGB Color Model | new feature, good first issue | 2018-02-24 |
| #38 | Webp2Gif | bug | 2018-01-02 |
| #28 | Convert Jpg to Bitmap? | new feature, not planned | 2017-08-21 |
| #26 | Combine multiple PDFs into a single PDF | new feature | 2017-07-31 |
| #25 | Turning a series of images into a video | new feature | 2017-07-21 |
| #22 | Issue installing for multiple users | new feature | 2017-05-19 |

### 10.4 分类汇总（第 11-13 页）

| 大类 | 数量 | 典型 issue |
|---|---:|---|
| 💡 格式扩展 / 新功能 | **26** | #204 WEBA、#189 AutoCAD、#174 纯文本提取、#153 Webp 无损、#122 ARM64、#111 Opus、#26 PDF 合并、#25 图片→视频 |
| 🐛 Bug | **14** | #200 Word→PDF 超链接丢失、#170 ICO 质量差、#142 GIF 帧时间错、#129 SVG 透明通道、#54 MP4 旋转不生效、#38 Webp→Gif |
| ❓ 需要更多信息 | **5** | #181 #180 #164 #45 #44 |
| ✅ 已在 fork 中修复 | **4** | #121 便携版（v2.2.2）、#111 Opus（v2.2.2）、#38 Webp→Gif（v2.2.1）、#153 Webp 无损（可通过自定义命令） |

### 10.5 值得关注的功能请求

| 优先级 | # | 描述 | 难度 | 备注 |
|---|---|---|---|---|
| 🔥 高 | #26 | 合并多个 PDF 为一个 | 中 | 用户长期高票请求，可用 Ghostscript/ImageMagick 实现 |
| 🔥 高 | #25 | 图片序列 → 视频 | 低 | ffmpeg `image2` 输入格式即可 |
| 🔥 高 | #201 | JPG 转换时指定目标文件大小 | 中 | 需要循环压缩，AI 素材场景也需要 |
| ⚡ 中 | #177 | MP4 质量/缩放用精确数字而非滑块 | 低 | UI 改进 |
| ⚡ 中 | #171 | 指定输出宽高比 | 中 | ffmpeg `-vf aspect=16:9` |
| ⚡ 中 | #117 | CLI 隐藏模式 | 低 | 已有 `--settings` 等参数 |
| ⚡ 中 | #42 | CMYK → RGB 颜色模型转换 | 低 | ImageMagick 原生支持 |
| ⚡ 中 | #129 | SVG → PNG 保留透明通道 | 低 | ImageMagick 配置调整 |
| 💤 低 | #122 | ARM64 支持 | 高 | 需要交叉编译 + ffmpeg/ImageMagick ARM64 版本 |
| 💤 低 | #189 | AutoCAD DWG → PDF | 高 | 需要 ODA 或 LibreCAD 集成 |

---

_最后更新：2026-05-01（第二次更新）_
