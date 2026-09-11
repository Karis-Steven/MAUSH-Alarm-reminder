# MAUSH Alarm Reminder 1.0

一个完全离线的 Windows 工作—休息循环计时器。支持自定义工作/休息时长、本地 M4A/WAV/MP3 提醒音、暂停/继续、无限循环、托盘后台运行，以及本地配置备份和恢复。

## 使用

1. 启动 `Maush.App.exe`。
2. 选择或输入工作与休息分钟数。
3. 根据需要开启“持续循环”。
4. 分别为“该休息了”和“继续工作”选择本地音频。
5. 点击“开始”。窗口右上角 X 只会隐藏到托盘。
6. 如需彻底退出，在托盘图标右键菜单中选择“退出程序”。

程序数据默认保存在 `%LocalAppData%\Maush`。所选音频会复制到受管目录，因此原文件移动后不会影响应用。备份文件使用 `.maushbackup` 扩展名，包含设置和受管音频。

## 构建与测试

需要 .NET 10 SDK 和 Windows 10/11：

```powershell
dotnet restore Maush.slnx --configfile NuGet.Config
dotnet test Maush.slnx --no-restore
dotnet publish src/Maush.App/Maush.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/publish
```

## 项目结构

```text
src/Maush.Core             状态机、计时运行时和音频协调规则
src/Maush.Infrastructure   本地设置、受管音频、备份和恢复
src/Maush.App              WPF 主窗口、音频播放器、托盘和系统事件
tests/                     核心、存储与界面逻辑测试
```

产品行为和验收标准以 [PRD.md](PRD.md) 为准。
