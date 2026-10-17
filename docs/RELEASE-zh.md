讲台助手 DeskKeeper 1.0.0（Windows 10/11 · x64）

一、这是什么
教室多媒体大屏的桌面维护工具。上课一键把图标整理成三列（应用 / 文件夹 / 文件），
下课把残留文件归档或移入回收站，支持空跑预览与撤销上一次清理。

二、直接运行
1. 双击 DeskKeeper.exe。
2. 首次运行默认不删除任何文件；整理与清理都会先给出预览和确认。
3. 关闭窗口后程序留在任务栏托盘，右键托盘图标可重新打开或退出。

三、安全说明
- 只处理桌面目录，不会全盘扫描。
- 删除优先归档（默认 D:\讲台归档，可在设置页修改）或移入回收站。
- 检测到 OneDrive 桌面托管时会整体拦截清理。
- 受保护的课件类型（.docx/.pptx/.xlsx/.pdf/.psd）默认不勾选。
- 本发布包未做代码签名，Windows SmartScreen 可能提示"未知发布者"。
  正式入校部署前请用学校证书签名，并做信息中心加白。

四、自检
在命令行运行：
    DeskKeeper.exe --selftest
返回码 0 表示桌面整理、归档、撤销链路自检通过（全程在临时目录，不触碰真实桌面）。

五、静默模式（GPO / 计划任务）
    DeskKeeper.exe --apply   --profile classroom --dry-run
    DeskKeeper.exe --cleanup --profile classroom --dry-run
    DeskKeeper.exe --cleanup --profile classroom
    DeskKeeper.exe --restore --profile classroom
    DeskKeeper.exe --restore-layout --profile classroom
建议先用 --dry-run 观察结果，再启用正式执行。

六、数据位置
%LocalAppData%\DeskKeeper\
  config.json      配置（自动 .bak 备份）
  snapshot.json    图标布局快照
  last-clean.json  上一次清理的撤销映射
  logs\audit.jsonl 审计日志（仅元数据，自动轮转）

七、系统要求
- Windows 10 1809+ / Windows 11，x64
- 无需安装 .NET 运行时（自包含单文件）
- 首次启动会释放运行库到临时目录，请勿使用只读临时目录的受限账户

八、常见问题
- 提示"自动排列图标已开启"：右键桌面 → 查看 → 取消勾选"自动排列图标"。
- 图标没有全部归位：可能是公共桌面项（无权移动）或超出布局容量，界面会提示数量。
- 想恢复上一次整理：重新执行整理会按快照增量处理；异常退出后可恢复 rollback 快照。

