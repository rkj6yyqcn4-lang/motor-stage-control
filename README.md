# 电机与位移台控制

这组 Windows 示例使用 HK USB DAQ V1.2 控制步进位移台，包含 MATLAB 调试脚本、C# 控制程序，以及用于光谱测距联动的 XY 位移台控制类。

| 目录 | 内容 |
| --- | --- |
| [matlab](matlab/) | 一维位移台控制及 X 轴、XY 双轴调试。`qaa.m` 含 ORG/CW/CCW 限位处理，`qaa_before_limits_20260728.m` 是历史版本。 |
| [xy-console](xy-console/) | `xy_pwm_simple_control.cs` 是独立的 C# XY 轴控制台示例。附有 .NET 10 的 x64 项目文件。 |
| [z-stage](z-stage/) | Z 轴 WinForms 控制工具。运行 `build.cmd` 编译，运行 `self-test.cmd` 可用模拟板卡检查控制逻辑。 |
| [stage-integration](stage-integration/) | 光谱测距联动版新增的 XY 位移台控制类。 |

## 环境与使用

- 实机控制需要 64 位 Windows、HK USB DAQ V1.2 板卡驱动及对应 `USBDAQ_DLL_V12X64.dll`。厂家依赖请从设备随附介质或已有本地备份取得。
- `StageController.cs` 用于接入 .NET Framework 4.7.2 的 WinForms 工程。接入位移台后，项目目标平台应与板卡 DLL 的 x64 位数一致。
- MATLAB 脚本通过 `loadlibrary` 加载 DLL，仓库附带 `HKDAQ_MATLAB.h` 接口适配头文件。运行前核对脚本中的 `dllPath`、`hPath` 和当前目录。部分 C# 示例也有需要按本机修改的 `DllPath` 常量。
- 不同示例的接线和限位配置有差异。连接设备前以对应脚本头部或项目使用说明为准。

## 构建 XY 控制台示例

安装 .NET 10 SDK 后，从仓库根目录执行以下命令。

```powershell
dotnet build xy-console/XYStageControl.csproj -c Release
```

光谱测距基础工程见 [DistanceMeassurementApp2 上游仓库](https://github.com/likepillow/DistanceMeassurementApp2)。原版与完整联动版 ZIP、Z 轴便携包及厂家依赖保存在本地恢复包中。
