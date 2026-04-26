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

| PR | 类型 | 关联 Issue | 合并价值 |
|---|---|---|---|
| **#732** FFmpeg stdout pipe 死锁修复 | 🔴 Bug Fix | #749 #740 #739 #716 #703 #700 | ⭐⭐⭐⭐⭐ 直接根治 v2.2 卡死回归 |
| **#702** GPU 失败自动回退软件编码 | 🟠 增强 | #713 #691 #572 | ⭐⭐⭐⭐ NVENC/CUDA 用户强烈需要 |
| **#699** UpgradeService null 异常修复（Closed 未合并） | 🟠 Bug Fix | #747 | ⭐⭐⭐⭐ 修复更新崩溃 |
| **#698** 简体中文翻译更新 | 🟢 i18n | #750 #667 #692 | ⭐⭐⭐ 中文用户翻译补全 |
| **#712** 意大利语翻译更新（Closed 未合并） | 🟢 i18n | - | ⭐⭐ 纯翻译 |
| **#707** 新增加泰罗尼亚语（Closed 未合并） | 🟢 i18n | - | ⭐⭐ 纯翻译 |
| **#562** ALAC 编码支持 | 🟢 功能 | #437 | ⭐⭐⭐ 音频用户请求 |

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

### 3.1 🔴 语言设置不生效（长期顽疾，10+ 个 issue）

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

**核心症状**：选择语言 → 保存 → 设置窗口自动关闭 → 重新打开仍是英文。

**修复任务（T-L）**：
- [ ] **T-L1** 定位 `SettingsViewModel` → `Settings.Save()` → `Settings.xml` 的写入路径
- [ ] **T-L2** 定位启动时 `Settings.Load()` → `ApplicationSettings.ApplicationLanguage` 的加载路径
- [ ] **T-L3** 复现 #750 "保存后窗口自动关闭" → 极可能是未捕获异常吞掉了保存流程
- [ ] **T-L4** 检查 `CurrentUICulture` 切换后 WPF `ResourceDictionary` 是否真的重载
- [ ] **T-L5** 若需重启才生效，则增加 "需重启" 提示而非静默失败
- [ ] **T-L6** 合入 PR #698（简体中文）、#712（意大利语）、#707（加泰罗尼亚语）
- [ ] **T-L7** 补齐波兰语（#638）

### 3.2 🔴 v2.2 转换卡死 / FFmpeg 残留（PR #732 一并解决）

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
- [ ] **T-P1** 合并 PR #732（直接修根因）
- [ ] **T-P2** 在取消按钮中向 ffmpeg stdin 发送 `q` 实现优雅终止；超时 `Process.Kill(entireProcessTree: true)`
- [ ] **T-P3** 任务完成 / 异常 / 取消三路径统一释放文件句柄
- [ ] **T-P4** 增加取消回归测试（启动 mp4 转码 5 秒后取消，断言无 ffmpeg 残留 + 输出可删）

### 3.3 🔴 设置不保存 / 设置窗口打不开

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

**修复任务（T-S）**：
- [ ] **T-S1** 与 T-L1 共同排查 Settings 持久化链路（大概率同根因）
- [ ] **T-S2** 启动时 `settings.xml` 损坏 / schema 不匹配的兜底：备份 + 重置到默认
- [ ] **T-S3** CRF 预设：从 `settings.xml` 运行时读取而非硬编码 31
- [ ] **T-S4** 设置窗口打不开 → 加全局异常捕获 + 日志

---

## 四、P1 中优先级 Bug

### 4.1 🟠 右键菜单 / Shell 扩展问题

| # | 描述 |
|---|---|
| #692 | 右键菜单与语言转换无作用 |
| #685 | Convert 菜单项突然不显示 |
| #675 | Context Menu Lag Caused by File Conversion |
| #645 | Directory Opus Explorer 中右键菜单不显示 |
| #633 | Context menu 中不出现 |
| #604 | Convert 选项从右键菜单消失 |
| #566 | 重装后右键菜单只显示一次 + 文件管理器崩溃 |
| #516 | Shell extension 被禁用导致不显示 |

**修复任务（T-CTX）**：
- [ ] **T-CTX1** 审查 Shell Extension 的 COM 注册路径（#450 也是注册表问题）
- [ ] **T-CTX2** 启动器检测 Shell Extension 状态 → 用户可见的"启用"按钮
- [ ] **T-CTX3** 上下文菜单延迟（#675）→ 延迟加载图标、避免同步 I/O

### 4.2 🟠 格式转换失败（分类整理）

**视频类**：
- #748 MKV → MP4 失败
- #713 NVENC/CUDA + scaling 滤镜错（PR #702 覆盖）
- #709 AVI → MP4 失败
- #678 mp4 → ogv 失败
- #577 MKV 文件转换错误
- #572 NVIDIA CUDA 硬件错（PR #702 覆盖）
- #560 视频转音频 ffmpeg 错
- #452 "Invalid data found when processing input"

**音频类**：
- #740 m4a → mp3（合并 PR #732）
- #715 Opus packet header 解析错
- #711 mp4 → mp3
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
- #676 非透明 PNG
- #643 PDF → PNG 问题
- #561 PDF → PNG 无输出
- #568 HEIC → PNG 丢 EXIF
- #251 docx → png 背景色错
- #513 webp → gif 不再生成 gif

**修复任务（T-F）**：
- [ ] **T-F1** 所有 ffmpeg/office 失败路径，**把 stderr 输出落到 `Logs/`**（当前没日志难排查）
- [ ] **T-F2** 动图类（WebP、animated JPG、animated PNG）统一加 `-loop 0` 分支
- [ ] **T-F3** DOCX → PDF：检测本地 Word/LibreOffice，缺失时给明确提示
- [ ] **T-F4** HDR AVIF → PNG：保留 color primaries / transfer function
- [ ] **T-F5** HEIC 元数据保留（EXIF、GPS）
- [ ] **T-F6** MKV 多音轨 → 增加音轨选择 UI（#438）

### 4.3 🟠 功能性 Bug（其他）

| # | 描述 |
|---|---|
| #605 | 缩放 75% 不生效 |
| #599 | EXIF 数据丢失 |
| #510 | 已选 "Move to Archive" 但仍附加 "(2)" 到文件名 |
| #674 | "Unable to extend cache - no space on device" |
| #642 | 上次更新后无法获取可执行路径 |
| #455 | 多文件一起转换时的 bug |
| #431 | 无输出 |
| #270 | MP4 缩略图不显示 |
| #258 | About 窗口中的链接失效 |

---

## 五、P2 功能请求与优化建议

### 5.1 格式扩展请求（合集）

| # | 请求 |
|---|---|
| #744 | PDF → DOC |
| #743 | PNG/JPG → DDS |
| #694 | EPUB ↔ ODT/FODT、PDF ↔ ODP/FODP |
| #687 | OGG 预设支持 Opus（现仅 vorbis） |
| #668 | 图像转成指定宽高比 |
| #636 | 覆盖输出选项 |
| #612 | JXL 支持 |
| #611 | 自定义输出格式 |
| #602 | TGA → PNG |
| #601 | AIFC → WAV |
| #571 | MP4 → PNG（提帧） |
| #454 | CDR → PSD |
| #446 | CBR ↔ CBZ ↔ PDF ↔ EPUB |
| #443 | SVG 输出 |
| #438 | 选择 MKV 音轨 |
| #437 | ALAC 输出（**PR #562 已实现**） |
| #427 | JPEG XL |
| #386 | H.265 预设 |
| #375 | 软字幕 → 硬字幕 |
| #369 | HEIF/HEIC → JPEG/PNG/PDF |
| #267 | EPS → SVG |
| #259 | EPUB → PDF |
| #632 | Whisper 字幕输出控制 |

### 5.2 流程增强

| # | 请求 |
|---|---|
| #704 | 多文件夹处理 |
| #701 | 给已有队列追加新任务 |
| #697 | 更新时不要重新添加预设 |
| #613 | 基于文件夹的自动转换 |
| #613 / #519 | Watch Folder 监视文件夹自动转换 |
| #518 | 拖拽区域 |
| #515 | 全局无损选项 |
| #514 | 转换视频片段 |
| #429 | 从队列中移除单项 |
| #522 | 右键命令行选项 |
| #384 | 进度条 |
| #361 | 转换时关闭窗口未提示/中止 |
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

| # | 描述 |
|---|---|
| #721 | v2.2 打包 bug |
| #579 | v2.1 安装器清空 Temp 目录（严重！） |
| #526 | 安装 bug code 0xF |
| #450 | FileConverterExtension.DLL 注册表记录错误 |
| #288 | Windows 11 22H2 无法安装 |
| #396 | 卸载后仍残留文件 |

### 6.2 更新器

| # | 描述 |
|---|---|
| #747 | 永远更新不到 2.2（PR #699 覆盖部分） |
| #689 | Chocolatey 版本过旧 |

**fork 需要额外处理**：`version.xml` / `version (x86).xml` 的 URL 需改指向 `UCHIHAHA103/FileConverter` 的 releases，否则本 fork 用户仍会被拉回原作者的包。

---

## 七、建议的修复迭代顺序

### 🚀 Sprint 1：发布 **2.2.1 hotfix**（1 周内）

> 目标：合并他人已写好的 PR，快速解决 v2.2 回归与更新崩溃

1. **合并 PR #732**（FFmpeg 死锁）→ 一次性修复 7 个卡死 issue
2. **Cherry-pick PR #699**（UpgradeService null 修复）
3. **T-U**：更新 `version.xml` 中的 URL 指向 fork releases
4. **T-P2 ~ T-P4**：补充 ffmpeg 优雅终止 + 进程清理
5. 发 **2.2.1**

### 🚀 Sprint 2：发布 **2.3.0 "Settings Fix"**（2-3 周）

> 目标：根治语言 / 设置持久化问题（**最多用户抱怨的痛点**）

1. **T-L1 ~ T-L5**：语言设置不生效根治
2. **T-S1 ~ T-S4**：设置窗口打不开 / 不保存 / CRF 不生效
3. **合并 PR #698 / #712 / #707**：翻译补全
4. 发 **2.3.0**

### 🚀 Sprint 3：发布 **2.3.1 "Format Fixes"**（2 周）

> 目标：格式转换 bug 批量修复

1. **T-F1**：失败路径写日志到 `Logs/`
2. **T-F2**：动图 WebP / 动画 JPG 保留帧
3. **T-F3**：DOCX 转换友好提示
4. **合并 PR #702**：GPU 自动回退软编
5. **合并 PR #562**：ALAC 支持

### 🚀 Sprint 4：发布 **2.4.0 "Enhancement"**（持续）

> 目标：右键菜单、批处理、队列增强

1. **T-CTX1 ~ T-CTX3**：右键菜单修复
2. Watch Folder（#519 / #613）
3. Queue 管理（#701 / #429）
4. 进度条（#384）

### 🚀 Sprint 5+：按需实现高票功能请求

- 高票格式：H.265 预设（#386）、HEIC→JPEG（#369）、Opus（#687）
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
| 独立 open issues | ~130 |
| open pull requests | 7 |
| 已 review 的关键 closed PR | 2（#699 #712 #707） |
| 可直接合并的 PR 总数 | **6** |
| 一并修复的 issue（合完 6 个 PR 后） | **~15 个** |

---

_最后更新：2026-04-26_
