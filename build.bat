@echo off
chcp 65001 >nul
echo [构建中] 正在使用 Windows 原生 .NET 编译器编译 鼠标防连击卫士...

set CSC_PATH=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC_PATH%" (
    echo [错误] 未在系统中检测到 .NET Framework 4.0/4.8 编译器。
    pause
    exit /b 1
)

if not exist "release" mkdir release

"%CSC_PATH%" /nologo /target:winexe /optimize+ /out:"release\鼠标防连击卫士.exe" /lib:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF /reference:PresentationFramework.dll /reference:PresentationCore.dll /reference:WindowsBase.dll /reference:System.Xaml.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "src\MouseDebounceGuard.cs"

if %ERRORLEVEL% equ 0 (
    echo [成功] 编译完成！生成文件位于: release\鼠标防连击卫士.exe
) else (
    echo [失败] 编译过程中出现错误，请检查代码。
)

pause
