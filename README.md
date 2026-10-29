# MouseDebounceGuard (鼠标防连击消抖卫士)

MouseDebounceGuard 是一款专为 Windows 平台打造的高性能鼠标防连击与微动触点消抖护航工具。
基于 WPF 与 DirectX GPU 硬件加速技术架构，彻底解决电竞与日常办公中因鼠标微动氧化、老化导致的“单击变双击（连击）”以及“长按断触（假脱手）”等硬件痛点。

---

## 核心特性

- **现代 WPF DirectX GPU 硬件加速**：
  采用微软 WPF 纯硬件渲染流水线与单一窗口宿主架构，彻底根除高轮询率（1000Hz/4000Hz/8000Hz）电竞鼠标拖拽窗口时的重绘卡顿与延迟，保证拖动与渲染满帧运行。

- **五键独立防护与自由调节**：
  全面覆盖鼠标左键、右键、滚轮中键、侧键 1（后退）和侧键 2（前进）。支持统一全局阈值或针对各按键独立设置防连击时间（5ms - 300ms）。
  支持滑块快速拖动与文本框精确键盘键入。

- **长按拖拽防断触保护**：
  针对老旧鼠标微动接触不良导致的“长按瞄准突然开镜回缩”、“拖拽选区或图标中途松脱”等问题，内置智能假脱水吸收算法，提供平滑可靠的长按维持体验。

- **微动健康诊断与寿命预警**：
  实时捕获并统计异常硬件杂波脉宽（如 0ms~40ms 的金属弹片抖动），智能评估微动氧化损耗程度，提供量化健康诊断指标。

- **极低系统资源占用**：
  底层直接挂接 Windows 低级鼠标钩子（WH_MOUSE_LL），精准纳秒级时间戳拦截，非拦截事件零开销直通，内存常驻仅数兆，无任何性能损耗。

- **单文件免安装，纯净无依赖**：
  程序大小仅数十 KB，纯单文件绿色运行。内置开机静默启动与托盘驻留，无任何多余弹窗与广告。

---

## 系统要求

- 操作系统：Windows 7 / Windows 8 / Windows 10 / Windows 11 (x86 / x64)
- 运行环境：.NET Framework 4.5 及以上（Windows 10/11 系统内置，开箱即用）

---

## 使用说明

1. 运行 `release/鼠标防连击卫士.exe`。
2. 开启您需要防护的鼠标按键（如右键或左键），根据按键老化情况设置消抖阈值（推荐 60ms ~ 100ms）。
3. 可以在下方的“实时按键消抖测试区”中点击鼠标测试拦截效果。
4. 点击窗口右上角关闭按钮或“隐藏至托盘”按钮，程序将自动最小化至系统托盘，静默在后台为您的鼠标按键护航。
5. 在任务栏托盘图标右键单击，即可调出操作菜单（显示面板、切换保护、开机自启、退出）。

---

## 源码构建

本项目采用纯单文件 C# 源码架构（`src/MouseDebounceGuard.cs`），无需安装 Visual Studio 等大型开发套件，直接使用 Windows 系统自带的 .NET Framework csc 编译器即可秒级构建。

### 命令行构建：

双击运行根目录下的 `build.bat`，或在命令行中执行：

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /platform:anycpu /out:release\鼠标防连击卫士.exe /lib:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF /reference:PresentationFramework.dll /reference:PresentationCore.dll /reference:WindowsBase.dll /reference:System.Xaml.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll src\MouseDebounceGuard.cs
```

构建完成后将在 `release` 目录生成优化后的独立可执行文件。

---

## 开源协议

本项目基于 [MIT 协议](LICENSE) 开源，欢迎提交 Issue 和 Pull Request。
