# Repository Guidelines

## Project Structure & Module Organization

本仓库是 .NET 10 Windows/WPF 方案，入口为 `MyWireGuard.slnx`。

- `src/MyWireGuard.App/`：WPF 桌面端、XAML、ViewModel、对话框/消息服务、图标与 manifest。
- `src/MyWireGuard.Core/`：领域模型与抽象接口，例如 tunnel profile、peer、日志、权限、运行时能力。
- `src/MyWireGuard.Infrastructure/`：文件配置、`wg-quick` 解析、runtime 定位、`tunnel.dll` 互操作和服务管理。
- `src/MyWireGuard.ServiceHost/`：随主程序构建/发布复制的辅助 host。
- `tests/MyWireGuard.Tests/`：xUnit 测试，覆盖 parser、邻居扫描基础设施和 tunnel 服务管理。
- `runtime/`：放置官方 `tunnel.dll`、`wireguard.dll`，细节见 `runtime/README.md`。
- `scripts/`：下载或构建 WireGuard runtime DLL 的 PowerShell 脚本。

## Build, Test, and Development Commands

在仓库根目录执行：

- `dotnet build MyWireGuard.slnx`：构建全部项目。
- `dotnet build src/MyWireGuard.App/MyWireGuard.App.csproj`：构建桌面端，并复制 runtime / service-host 产物。
- `dotnet run --project src/MyWireGuard.App/MyWireGuard.App.csproj`：本地启动 WPF 应用。
- `dotnet test tests/MyWireGuard.Tests/MyWireGuard.Tests.csproj`：运行 xUnit 测试。
- `dotnet publish src/MyWireGuard.App/MyWireGuard.App.csproj -c Release`：发布主程序和辅助 host。
- `powershell -ExecutionPolicy Bypass -File .\scripts\Get-WireGuardRuntime.ps1`：获取完整 runtime DLL 集。

在本地 Codex 环境中，shell 命令加 `rtk` 前缀，例如 `rtk dotnet test tests/MyWireGuard.Tests/MyWireGuard.Tests.csproj`。

## Coding Style & Naming Conventions

C# 项目启用 nullable reference types 与 implicit usings。保持现有四空格缩进和 file-scoped namespace。public 类型、方法、属性使用 PascalCase；private 字段和局部变量使用 camelCase。UI 逻辑留在 `MyWireGuard.App`，契约/模型留在 `Core`，OS、native、文件系统集成放在 `Infrastructure`。不要重复实现 WireGuard 配置解析或 runtime 查找逻辑，优先扩展现有服务。

新增或修改文件必须使用 UTF-8，禁止依赖系统代码页默认编码。

## Testing Guidelines

测试框架为 xUnit。测试命名沿用 `MethodOrScenario_ShouldExpectedBehavior`，例如 `Parse_ShouldReadInterfaceAndPeerSections`。修改配置解析、tunnel 服务行为、runtime 发现或邻居扫描时，应补充聚焦测试。涉及 Windows 服务或 native DLL 的改动，至少覆盖纯逻辑部分，并记录无法自动化的手工验证。

## Commit & Pull Request Guidelines

近期提交使用简洁中文摘要，例如“主界面UI重构与样式优化，提升一致性与体验”。提交应保持单一主题，并说明用户可见变化或架构变化。PR 需要包含变更摘要、测试结果（或未运行原因）、关联 issue；涉及 WPF UI 时附截图。

## Security & Runtime Notes

不要提交真实 WireGuard 私钥或生产 tunnel 配置。`wireguard.exe` 不能替代 `tunnel.dll`；运行时需要 `tunnel.dll` 和 `wireguard.dll`。应用还支持从 `%LOCALAPPDATA%/MyWireGuard/Runtime` 或 `MYWIREGUARD_RUNTIME_DIR` 查找 runtime 资产。
