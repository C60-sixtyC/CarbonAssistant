# 讲台助手 DeskKeeper

教室多媒体大屏桌面维护工具：上课一键整理桌面（三分区排列），下课一键清理残留（归档优先、可撤销），
专为 Win10/Win11 触控大屏设计。设计依据见仓库根目录的《产品设计文档》与《技术方案》。

## 当前实现（对照迭代路线）

- v0.1 核心：规则引擎 + 三分区布局引擎（比例 2:2.5:5.5，槽位不足自动降级两列）、
  快照增量还原（pinned 固定图标不动）、布局前沙盘预览、自动排列图标检测与拦截。
- v0.2 核心：桌面扫描清单（临时文件/陈旧文件自动勾选、教学文档默认保护）、
  双层排除（配置规则 + 本次勾选）、Dry-run 空跑、归档优先（同卷直移、跨卷复制+哈希校验）、
  回收站四项预检（NukeOnDelete、容量、可移动磁盘/UNC、占用文件探测）、逐文件失败隔离、
  撤销上一次清理、JSON 审计日志（仅元数据，50MB/90 天轮转）。
- 安全兜底：OneDrive 桌面整体拦截、公共桌面只读不动、系统虚拟图标识别跳过、
  配置双写 .bak 损坏回滚、执行前写回滚快照（pending 标记）、`--selftest` 内建自检。
- 暂未实现（按路线图后置）：登录/注销 GPO 自动触发、考试模式密码锁、恢复回收站内单项、
  设置页规则拖拽编辑（当前规则在 config.json 中可改）。

## 工程结构

```
src/DeskKeeper.Domain          纯逻辑：规则引擎、Glob、布局引擎（无 UI、无磁盘副作用）
src/DeskKeeper.Infrastructure  Win32/Shell：桌面枚举、图标落位、回收站预检、配置/快照/审计
src/DeskKeeper.Application     用例编排：Arrange / Clean / Restore
src/DeskKeeper.App             WPF 三页 UI + 托盘 + 单实例 + 命令行
tests/DeskKeeper.Tests         xUnit：52 个单元/集成测试（全部跑在临时目录）
```

## 构建 / 测试 / 发布

要求：Windows 10/11 + .NET 8 SDK（`dotnet --info` 可查）。

```powershell
dotnet build DeskKeeper.sln -c Release
dotnet test DeskKeeper.sln -c Release        # 52 个测试，不触碰真实桌面

# 自包含单文件发布（离线机器直接运行）
dotnet publish src/DeskKeeper.App/DeskKeeper.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o artifacts/win-x64

# 内建自检（无 UI，临时目录跑清理→归档→撤销 / 排列→落位→快照）
.\artifacts\win-x64\DeskKeeper.exe --selftest
```

## 运行与命令行

双击运行进入触控面板：首页（上课整理 / 下课清理）/ 清理清单 / 设置。关闭窗口缩到托盘。

供 GPO 计划任务调用的静默模式：

```powershell
DeskKeeper.exe --apply   --profile classroom --dry-run            # 整理（空跑）
DeskKeeper.exe --apply   --profile classroom                      # 整理（执行）
DeskKeeper.exe --cleanup --profile classroom --dry-run            # 清理（空跑）
DeskKeeper.exe --restore --profile classroom                      # 撤销上次清理
DeskKeeper.exe --selftest --log .\selftest.json                   # 自检并写结果文件
```

## 配置与数据位置

- `%LocalAppData%\DeskKeeper\config.json`：配置（写入前自动备份 .bak，损坏自动回滚）。
- `snapshot.json` / `rollback.json`：布局快照与回滚点。
- `last-clean.json`：撤销映射（原路径 ↔ 归档路径）。
- `logs\audit.jsonl`：审计日志（只存元数据，不存文件内容）。

默认 `classroom` 配置示例见 `ConfigStore.CreateDefaultConfig()`，与《技术方案》4.2 节规则一致。

## 安全红线（实现对照）

- 只处理桌面目录（枚举器作用域锁死，无全盘递归入口）。
- 首次默认空跑，删除优先归档或回收站；NukeOnDelete / 容量不足 / UNC 路径均有警告或降级。
- 布局执行前写回滚快照，异常退出后下次启动可恢复。
- 不包含：全盘扫描、注册表清理、内存加速、浏览器主页锁定等「不做清单」功能。

## 已知边界

- 图标落位通过桌面 SysListView32 消息实现，若系统版本差异导致窗口枚举失败会逐项报错并写入审计日志，绝不静默。
- 「最后访问时间」判据默认关闭，需 `LastAccessProbe` 探测通过后才可启用。

