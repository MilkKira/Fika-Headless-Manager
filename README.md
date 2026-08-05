# Fika 无头管理器

Fika 无头管理器是一款 Windows 桌面控制程序，可在一个窗口中管理多个 Fika 无头客户端。

## 功能

- 单窗口 WPF 仪表盘，包含固定侧栏、运行指标、实时趋势图和实例管理表格。
- 新增、编辑、移除、启动和停止多个 SPT/Fika 无头实例。
- 通过每个实例旁的“查看”按钮，在程序内部实时查看管理器输出、标准输出、错误输出、`BepInEx/LogOutput.log` 和可选的 `Headless.log`。
- 一键启动或停止全部已配置实例。
- 启动前验证 `EscapeFromTarkov.exe`、`Fika.Headless.dll` 和 Fika 后端服务。
- 无头客户端意外退出后自动重启。
- 管理器采用 Windows GUI 模式运行，不显示自身控制台窗口。
- 使用 `CreateNoWindow`、`--enable-console false` 和 Win32 窗口监控隐藏子进程及 BepInEx 控制台。
- 首次在 SPT 目录中运行时，自动导入旧版 `HeadlessConfig.json`。

实例配置保存在 `%LOCALAPPDATA%\FikaHeadlessManager\instances.json`。

## 构建

需要 Windows 和 .NET 9 SDK。

```powershell
dotnet build FikaHeadlessManager.csproj --configuration Release
```

## 发布 EXE

发布依赖框架的单文件程序：

```powershell
dotnet publish FikaHeadlessManager.csproj --configuration Release --runtime win-x64 --output publish
```

目标计算机需要安装 .NET 9 Windows Desktop Runtime。如需包含运行时，请追加 `--self-contained true`。

## 使用方法

1. 运行 `FikaHeadlessManager.exe`。
2. 点击“添加实例”。
3. 选择 SPT 安装目录，并填写 Fika 配置文件 ID 和后端地址。
4. 保存后使用“启动”“停止”“全部启动”或“全部停止”。
5. 点击实例旁的“查看”，在程序内部查看实时输出。

关闭仪表盘时，程序会停止本次运行期间启动的全部无头进程。

实时输出仅保存在内存中，每个实例最多保留 3000 条。同一时间运行的实例必须使用不同的 SPT 安装目录，以避免 BepInEx 和 Unity 文件日志相互混合。
