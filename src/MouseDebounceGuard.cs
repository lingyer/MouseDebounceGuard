using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

// 引入 WinForms 仅用于轻量系统托盘 NotifyIcon
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace MouseDebounceGuard
{
    public static class CoreService
    {
        private const int WH_MOUSE_LL    = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP   = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP   = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP   = 0x0208;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP   = 0x020C;

        private static LowLevelMouseProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        public static int GlobalDebounceMs = 100;
        public static bool EnablePerButtonMs = false;

        public static int LeftMs   = 80;
        public static int RightMs  = 100;
        public static int MiddleMs = 80;
        public static int X1Ms     = 80;
        public static int X2Ms     = 80;

        public static bool EnableLeft   = false;
        public static bool EnableRight  = true;
        public static bool EnableMiddle = false;
        public static bool EnableX1     = false;
        public static bool EnableX2     = false;

        public static bool EnableHoldProtection = true;
        public static bool EnableSoundFeedback  = false;
        public static bool AutoStartEnabled     = true;

        public static int BlockedCount = 0;
        public static long MinJitterMs = 999;
        public static long TotalJitterMs = 0;
        public static int ExtremeJitterCount = 0;

        public class ButtonEngine
        {
            private Stopwatch downWatch = new Stopwatch();
            private Stopwatch upWatch = new Stopwatch();
            public bool IsPhysicallyDown = false;
            private System.Threading.Timer delayedUpTimer;

            public ButtonEngine()
            {
                downWatch.Start();
                upWatch.Start();
            }

            public bool HandleDown(int thresholdMs, out long elapsed)
            {
                elapsed = downWatch.ElapsedMilliseconds;

                if (EnableHoldProtection && delayedUpTimer != null)
                {
                    try
                    {
                        delayedUpTimer.Dispose();
                        delayedUpTimer = null;
                        IsPhysicallyDown = true;
                        return true;
                    }
                    catch {}
                }

                if (elapsed < thresholdMs)
                {
                    return true;
                }

                IsPhysicallyDown = true;
                downWatch.Restart();
                return false;
            }

            public bool HandleUp(bool isProtectedButton)
            {
                long held = downWatch.ElapsedMilliseconds;
                upWatch.Restart();

                if (held < 15) return true;

                if (EnableHoldProtection && isProtectedButton && held > 120)
                {
                    if (delayedUpTimer != null) delayedUpTimer.Dispose();
                    delayedUpTimer = new System.Threading.Timer(delegate(object state)
                    {
                        IsPhysicallyDown = false;
                        if (delayedUpTimer != null) { delayedUpTimer.Dispose(); delayedUpTimer = null; }
                    }, null, 35, System.Threading.Timeout.Infinite);

                    return false;
                }

                IsPhysicallyDown = false;
                return false;
            }
        }

        public static ButtonEngine LeftEngine   = new ButtonEngine();
        public static ButtonEngine RightEngine  = new ButtonEngine();
        public static ButtonEngine MiddleEngine = new ButtonEngine();
        public static ButtonEngine X1Engine     = new ButtonEngine();
        public static ButtonEngine X2Engine     = new ButtonEngine();

        public static event Action<string, long, bool> OnClickProcessed;
        public static event Action OnBlockedCountChanged;

        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryName = "MouseDebounceGuard";
        private const string ConfigRegistryKey = @"Software\MouseDebounceGuard";

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public static void Init()
        {
            LoadSettings();
            _hookID = SetHook(_proc);
        }

        public static void Cleanup()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }

        public static void LoadSettings()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ConfigRegistryKey, false))
                {
                    if (key != null)
                    {
                        object gVal = key.GetValue("GlobalDebounceMs");
                        if (gVal != null) GlobalDebounceMs = Convert.ToInt32(gVal);

                        object pVal = key.GetValue("EnablePerButtonMs");
                        if (pVal != null) EnablePerButtonMs = (Convert.ToInt32(pVal) == 1);

                        object lMs = key.GetValue("LeftMs");
                        if (lMs != null) LeftMs = Convert.ToInt32(lMs);

                        object rMs = key.GetValue("RightMs");
                        if (rMs != null) RightMs = Convert.ToInt32(rMs);

                        object mMs = key.GetValue("MiddleMs");
                        if (mMs != null) MiddleMs = Convert.ToInt32(mMs);

                        object x1M = key.GetValue("X1Ms");
                        if (x1M != null) X1Ms = Convert.ToInt32(x1M);

                        object x2M = key.GetValue("X2Ms");
                        if (x2M != null) X2Ms = Convert.ToInt32(x2M);

                        object lEn = key.GetValue("EnableLeft");
                        if (lEn != null) EnableLeft = (Convert.ToInt32(lEn) == 1);

                        object rEn = key.GetValue("EnableRight");
                        if (rEn != null) EnableRight = (Convert.ToInt32(rEn) == 1);

                        object mEn = key.GetValue("EnableMiddle");
                        if (mEn != null) EnableMiddle = (Convert.ToInt32(mEn) == 1);

                        object x1En = key.GetValue("EnableX1");
                        if (x1En != null) EnableX1 = (Convert.ToInt32(x1En) == 1);

                        object x2En = key.GetValue("EnableX2");
                        if (x2En != null) EnableX2 = (Convert.ToInt32(x2En) == 1);

                        object hpVal = key.GetValue("EnableHoldProtection");
                        if (hpVal != null) EnableHoldProtection = (Convert.ToInt32(hpVal) == 1);

                        object sndVal = key.GetValue("EnableSoundFeedback");
                        if (sndVal != null) EnableSoundFeedback = (Convert.ToInt32(sndVal) == 1);

                        object autoVal = key.GetValue("AutoStart");
                        if (autoVal != null)
                        {
                            AutoStartEnabled = (Convert.ToInt32(autoVal) == 1);
                        }
                        else
                        {
                            AutoStartEnabled = CheckRunRegistryExists();
                        }
                    }
                    else
                    {
                        AutoStartEnabled = CheckRunRegistryExists();
                    }
                }
            }
            catch {}
        }

        public static void SaveSettings()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ConfigRegistryKey))
                {
                    if (key != null)
                    {
                        key.SetValue("GlobalDebounceMs", GlobalDebounceMs, RegistryValueKind.DWord);
                        key.SetValue("EnablePerButtonMs", EnablePerButtonMs ? 1 : 0, RegistryValueKind.DWord);

                        key.SetValue("LeftMs", LeftMs, RegistryValueKind.DWord);
                        key.SetValue("RightMs", RightMs, RegistryValueKind.DWord);
                        key.SetValue("MiddleMs", MiddleMs, RegistryValueKind.DWord);
                        key.SetValue("X1Ms", X1Ms, RegistryValueKind.DWord);
                        key.SetValue("X2Ms", X2Ms, RegistryValueKind.DWord);

                        key.SetValue("EnableLeft", EnableLeft ? 1 : 0, RegistryValueKind.DWord);
                        key.SetValue("EnableRight", EnableRight ? 1 : 0, RegistryValueKind.DWord);
                        key.SetValue("EnableMiddle", EnableMiddle ? 1 : 0, RegistryValueKind.DWord);
                        key.SetValue("EnableX1", EnableX1 ? 1 : 0, RegistryValueKind.DWord);
                        key.SetValue("EnableX2", EnableX2 ? 1 : 0, RegistryValueKind.DWord);

                        key.SetValue("EnableHoldProtection", EnableHoldProtection ? 1 : 0, RegistryValueKind.DWord);
                        key.SetValue("EnableSoundFeedback", EnableSoundFeedback ? 1 : 0, RegistryValueKind.DWord);
                        key.SetValue("AutoStart", AutoStartEnabled ? 1 : 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch {}
        }

        private static bool CheckRunRegistryExists()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue(AppRegistryName);
                        return (val != null);
                    }
                }
            }
            catch {}
            return true;
        }

        public static void SetAutoStart(bool enable)
        {
            AutoStartEnabled = enable;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string exePath = Process.GetCurrentProcess().MainModule.FileName;
                            key.SetValue(AppRegistryName, "\"" + exePath + "\"");
                        }
                        else
                        {
                            key.DeleteValue(AppRegistryName, false);
                        }
                    }
                }
            }
            catch {}
            SaveSettings();
        }

        private static IntPtr SetHook(LowLevelMouseProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();

                // ⚡ 极速白名单直通：只要不是按键物理 DOWN/UP，0 纳秒立即直通！
                if (msg != WM_LBUTTONDOWN && msg != WM_LBUTTONUP &&
                    msg != WM_RBUTTONDOWN && msg != WM_RBUTTONUP &&
                    msg != WM_MBUTTONDOWN && msg != WM_MBUTTONUP &&
                    msg != WM_XBUTTONDOWN && msg != WM_XBUTTONUP)
                {
                    return CallNextHookEx(_hookID, nCode, wParam, lParam);
                }

                int leftTh   = EnablePerButtonMs ? LeftMs   : GlobalDebounceMs;
                int rightTh  = EnablePerButtonMs ? RightMs  : GlobalDebounceMs;
                int middleTh = EnablePerButtonMs ? MiddleMs : GlobalDebounceMs;
                int x1Th     = EnablePerButtonMs ? X1Ms     : GlobalDebounceMs;
                int x2Th     = EnablePerButtonMs ? X2Ms     : GlobalDebounceMs;

                // 左键
                if (EnableLeft && (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP))
                {
                    if (DispatchEvent("左键", msg == WM_LBUTTONDOWN, LeftEngine, leftTh, true)) return (IntPtr)1;
                }
                // 右键
                else if (EnableRight && (msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP))
                {
                    if (DispatchEvent("右键", msg == WM_RBUTTONDOWN, RightEngine, rightTh, true)) return (IntPtr)1;
                }
                // 中键
                else if (EnableMiddle && (msg == WM_MBUTTONDOWN || msg == WM_MBUTTONUP))
                {
                    if (DispatchEvent("中键", msg == WM_MBUTTONDOWN, MiddleEngine, middleTh, true)) return (IntPtr)1;
                }
                // 侧键
                else if ((EnableX1 || EnableX2) && (msg == WM_XBUTTONDOWN || msg == WM_XBUTTONUP))
                {
                    MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    int xbtn = (int)((hookStruct.mouseData >> 16) & 0xFFFF);
                    if (EnableX1 && xbtn == 1)
                    {
                        if (DispatchEvent("侧键1", msg == WM_XBUTTONDOWN, X1Engine, x1Th, true)) return (IntPtr)1;
                    }
                    else if (EnableX2 && xbtn == 2)
                    {
                        if (DispatchEvent("侧键2", msg == WM_XBUTTONDOWN, X2Engine, x2Th, true)) return (IntPtr)1;
                    }
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private static bool DispatchEvent(string btnName, bool isDown, ButtonEngine engine, int thresholdMs, bool isProtected)
        {
            if (isDown)
            {
                long elapsed;
                bool blocked = engine.HandleDown(thresholdMs, out elapsed);
                if (blocked)
                {
                    BlockedCount++;
                    TotalJitterMs += elapsed;
                    if (elapsed < MinJitterMs && elapsed > 0) MinJitterMs = elapsed;
                    if (elapsed < 10) ExtremeJitterCount++;

                    if (EnableSoundFeedback)
                    {
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            try { System.Media.SystemSounds.Beep.Play(); } catch {}
                        });
                    }

                    if (OnBlockedCountChanged != null) OnBlockedCountChanged();
                    if (OnClickProcessed != null) OnClickProcessed(btnName, elapsed, true);
                    return true;
                }
                else
                {
                    if (OnClickProcessed != null) OnClickProcessed(btnName, elapsed, false);
                }
            }
            else
            {
                if (engine.HandleUp(isProtected)) return true;
            }
            return false;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }

    public class MainWindow : Window
    {
        private WF.NotifyIcon trayIcon;
        private WF.ContextMenuStrip trayMenu;
        private WF.ToolStripMenuItem statusMenuItem;

        private CheckBox chkPerButtonMode;
        private Slider sliderGlobal;
        private TextBox txtGlobal;

        private CheckBox chkLeft;   private Slider sliderLeft;   private TextBox txtLeft;
        private CheckBox chkRight;  private Slider sliderRight;  private TextBox txtRight;
        private CheckBox chkMiddle; private Slider sliderMiddle; private TextBox txtMiddle;
        private CheckBox chkX1;     private Slider sliderX1;     private TextBox txtX1;
        private CheckBox chkX2;     private Slider sliderX2;     private TextBox txtX2;

        private CheckBox chkHoldProtection;
        private CheckBox chkSoundFeedback;
        private CheckBox chkAutoStart;

        private TextBlock lblHealthTitle;
        private TextBlock lblHealthDetails;
        private TextBlock lblTestBox;
        private TextBlock lblTestStats;

        private bool isInternalUiUpdate = false;

        public MainWindow()
        {
            this.Title = "鼠标防连击卫士 - 控制面板";
            this.Width = 590;
            this.Height = 770;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.ResizeMode = ResizeMode.NoResize;
            this.Background = new SolidColorBrush(Color.FromRgb(245, 247, 250)); // 极简纯净微灰底色
            this.FontFamily = new FontFamily("Microsoft YaHei, Segoe UI");

            BuildWpfUI();
            InitTray();
            SyncControlsWithSettings();

            CoreService.OnBlockedCountChanged += delegate
            {
                this.Dispatcher.BeginInvoke((Action)delegate
                {
                    UpdateStatsDisplay();
                    UpdateHealthDisplay();
                });
            };

            CoreService.OnClickProcessed += delegate(string btn, long elapsed, bool blocked)
            {
                this.Dispatcher.BeginInvoke((Action)delegate
                {
                    if (blocked)
                    {
                        lblTestBox.Text = string.Format("拦截 [{0}] 异常连击/断触 (物理抖动: {1}ms)", btn, elapsed);
                        lblTestBox.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // 严谨警示红
                    }
                    else
                    {
                        lblTestBox.Text = string.Format("[{0}] 有效单次点击 (与上次间隔: {1}ms)", btn, elapsed);
                        lblTestBox.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52)); // 护航沉稳绿
                    }
                });
            };
        }

        private void BuildWpfUI()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(65) }); // 顶部 Banner
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 主体

            // 1. 顶部 Header (天青石蓝现代微渐变，纯文字，无 Emoji，支持全域拖拽)
            Border header = new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromRgb(0, 114, 206),
                    Color.FromRgb(0, 86, 163),
                    new Point(0, 0), new Point(1, 1))
            };
            header.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) this.DragMove();
            };

            StackPanel headerStack = new StackPanel { Margin = new Thickness(20, 12, 20, 12) };
            TextBlock txtTitle = new TextBlock
            {
                Text = "鼠标防连击卫士",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            TextBlock txtSubtitle = new TextBlock
            {
                Text = "已开启静默后台守护 | ms 支持滑动或直接键入 | 点击右上角红叉自动隐藏至托盘",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(215, 235, 255)),
                Margin = new Thickness(0, 4, 0, 0)
            };
            headerStack.Children.Add(txtTitle);
            headerStack.Children.Add(txtSubtitle);
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // 2. 主体区域 (卡片式分层，极简纯净，无 Emoji)
            StackPanel body = new StackPanel { Margin = new Thickness(15, 10, 15, 10) };

            // (1) 全局阈值卡片
            GroupBox gbGlobal = CreateStyledGroupBox("消抖模式与全局阈值");
            StackPanel spGlobal = new StackPanel();

            chkPerButtonMode = new CheckBox
            {
                Content = "开启【每个按键专属 ms 模式】 (勾选后各按键采用独立数值，默认关闭)",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            chkPerButtonMode.Checked += delegate { TogglePerButtonMode(true); };
            chkPerButtonMode.Unchecked += delegate { TogglePerButtonMode(false); };
            spGlobal.Children.Add(chkPerButtonMode);

            Grid gridGlobalRow = new Grid();
            gridGlobalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            gridGlobalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridGlobalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            gridGlobalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            TextBlock lblGTitle = new TextBlock { Text = "全局消抖阈值:", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lblGTitle, 0);
            gridGlobalRow.Children.Add(lblGTitle);

            sliderGlobal = new Slider
            {
                Minimum = 20, Maximum = 300, SmallChange = 1, LargeChange = 10,
                IsSnapToTickEnabled = true, TickFrequency = 10,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 10, 0)
            };
            Grid.SetColumn(sliderGlobal, 1);
            gridGlobalRow.Children.Add(sliderGlobal);

            txtGlobal = CreateStyledInputBox();
            Grid.SetColumn(txtGlobal, 2);
            gridGlobalRow.Children.Add(txtGlobal);

            TextBlock lblGMs = new TextBlock { Text = "ms", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray, Margin = new Thickness(5, 0, 0, 0) };
            Grid.SetColumn(lblGMs, 3);
            gridGlobalRow.Children.Add(lblGMs);

            sliderGlobal.ValueChanged += delegate
            {
                if (isInternalUiUpdate) return;
                isInternalUiUpdate = true;
                int v = (int)sliderGlobal.Value;
                txtGlobal.Text = v.ToString();
                CoreService.GlobalDebounceMs = v;
                CoreService.SaveSettings();
                RebuildTrayMenu();
                isInternalUiUpdate = false;
            };

            BindInputBox(txtGlobal, sliderGlobal, delegate(int v)
            {
                CoreService.GlobalDebounceMs = v;
                CoreService.SaveSettings();
                RebuildTrayMenu();
            });

            spGlobal.Children.Add(gridGlobalRow);
            gbGlobal.Content = spGlobal;
            body.Children.Add(gbGlobal);

            // (2) 按键专属配置卡片 (左键置顶，右键无核心)
            GroupBox gbButtons = CreateStyledGroupBox("按键多选防护与专属消抖调节");
            StackPanel spButtons = new StackPanel();

            CreateButtonRow(spButtons, "鼠标左键", out chkLeft, out sliderLeft, out txtLeft,
                delegate(bool en) { CoreService.EnableLeft = en; },
                delegate(int v) { CoreService.LeftMs = v; });

            CreateButtonRow(spButtons, "鼠标右键", out chkRight, out sliderRight, out txtRight,
                delegate(bool en) { CoreService.EnableRight = en; },
                delegate(int v) { CoreService.RightMs = v; });

            CreateButtonRow(spButtons, "滚轮中键", out chkMiddle, out sliderMiddle, out txtMiddle,
                delegate(bool en) { CoreService.EnableMiddle = en; },
                delegate(int v) { CoreService.MiddleMs = v; });

            CreateButtonRow(spButtons, "侧键 1 (后退)", out chkX1, out sliderX1, out txtX1,
                delegate(bool en) { CoreService.EnableX1 = en; },
                delegate(int v) { CoreService.X1Ms = v; });

            CreateButtonRow(spButtons, "侧键 2 (前进)", out chkX2, out sliderX2, out txtX2,
                delegate(bool en) { CoreService.EnableX2 = en; },
                delegate(int v) { CoreService.X2Ms = v; });

            gbButtons.Content = spButtons;
            body.Children.Add(gbButtons);

            // (3) 进阶防护卡片
            GroupBox gbAdv = CreateStyledGroupBox("进阶增强防护");
            DockPanel dpAdv = new DockPanel();

            chkHoldProtection = new CheckBox
            {
                Content = "长按拖拽防断触保护 (长按瞄准/拖动时吸收虚脱假松开)",
                VerticalAlignment = VerticalAlignment.Center
            };
            chkHoldProtection.Checked += delegate { CoreService.EnableHoldProtection = true; CoreService.SaveSettings(); RebuildTrayMenu(); };
            chkHoldProtection.Unchecked += delegate { CoreService.EnableHoldProtection = false; CoreService.SaveSettings(); RebuildTrayMenu(); };
            dpAdv.Children.Add(chkHoldProtection);

            chkSoundFeedback = new CheckBox
            {
                Content = "拦截提示音效",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            chkSoundFeedback.Checked += delegate { CoreService.EnableSoundFeedback = true; CoreService.SaveSettings(); RebuildTrayMenu(); };
            chkSoundFeedback.Unchecked += delegate { CoreService.EnableSoundFeedback = false; CoreService.SaveSettings(); RebuildTrayMenu(); };
            dpAdv.Children.Add(chkSoundFeedback);

            gbAdv.Content = dpAdv;
            body.Children.Add(gbAdv);

            // (4) 微动健康度卡片
            GroupBox gbHealth = CreateStyledGroupBox("鼠标微动健康度诊断与寿命预警");
            StackPanel spHealth = new StackPanel();
            lblHealthTitle = new TextBlock
            {
                Text = "微动状态良好 (消抖算法平稳护航中)",
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            lblHealthDetails = new TextBlock
            {
                Text = "累计拦截: 0 次 | 最短触点杂波: -- ms | 评估: 触点处于健康消抖区间",
                Foreground = Brushes.DimGray
            };
            spHealth.Children.Add(lblHealthTitle);
            spHealth.Children.Add(lblHealthDetails);
            gbHealth.Content = spHealth;
            body.Children.Add(gbHealth);

            // (5) 测试靶场卡片
            GroupBox gbTest = CreateStyledGroupBox("实时按键消抖测试区 (在此区域内狂按右键测试效果)");
            StackPanel spTest = new StackPanel();
            lblTestBox = new TextBlock
            {
                Text = "请在任意位置或此处点击鼠标测试响应，连击杂波将被实时捕获...",
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 4)
            };
            lblTestStats = new TextBlock
            {
                Text = "已就绪",
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105))
            };
            spTest.Children.Add(lblTestBox);
            spTest.Children.Add(lblTestStats);
            gbTest.Content = spTest;
            body.Children.Add(gbTest);

            // (6) 底部操作栏
            DockPanel dpBottom = new DockPanel { Margin = new Thickness(5, 5, 5, 0) };
            chkAutoStart = new CheckBox
            {
                Content = "开机自动静默启动 (勾选自启，取消彻底关闭)",
                VerticalAlignment = VerticalAlignment.Center
            };
            chkAutoStart.Checked += delegate { CoreService.SetAutoStart(true); RebuildTrayMenu(); };
            chkAutoStart.Unchecked += delegate { CoreService.SetAutoStart(false); RebuildTrayMenu(); };
            dpBottom.Children.Add(chkAutoStart);

            Button btnHide = new Button
            {
                Content = "隐藏至托盘",
                Width = 95, Height = 28,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnHide.Click += delegate { this.Hide(); };
            dpBottom.Children.Add(btnHide);

            body.Children.Add(dpBottom);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            this.Content = root;
        }

        private GroupBox CreateStyledGroupBox(string headerText)
        {
            return new GroupBox
            {
                Header = headerText,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(10, 8, 10, 8),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240))
            };
        }

        private TextBox CreateStyledInputBox()
        {
            return new TextBox
            {
                Width = 55, Height = 24,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold,
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                MaxLength = 4
            };
        }

        private void BindInputBox(TextBox txt, Slider slider, Action<int> onCommit)
        {
            Action commit = delegate
            {
                int val;
                if (!int.TryParse(txt.Text.Trim(), out val))
                {
                    val = (int)slider.Value;
                }
                val = Math.Max(10, Math.Min(500, val));
                txt.Text = val.ToString();

                if (slider.IsEnabled)
                {
                    slider.Value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, val));
                }

                if (!isInternalUiUpdate)
                {
                    isInternalUiUpdate = true;
                    onCommit(val);
                    isInternalUiUpdate = false;
                }
            };

            txt.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter)
                {
                    commit();
                    Keyboard.ClearFocus();
                }
            };

            txt.LostFocus += delegate { commit(); };
        }

        private void CreateButtonRow(StackPanel parent, string title, out CheckBox chk, out Slider slider, out TextBox txt, Action<bool> onToggle, Action<int> onVal)
        {
            Grid row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            chk = new CheckBox { Content = title, VerticalAlignment = VerticalAlignment.Center };
            Action<bool> toggleAction = onToggle;
            CheckBox localChk = chk;
            chk.Checked += delegate { toggleAction(true); CoreService.SaveSettings(); RebuildTrayMenu(); };
            chk.Unchecked += delegate { toggleAction(false); CoreService.SaveSettings(); RebuildTrayMenu(); };
            Grid.SetColumn(chk, 0);
            row.Children.Add(chk);

            slider = new Slider
            {
                Minimum = 20, Maximum = 300, SmallChange = 1, LargeChange = 10,
                IsSnapToTickEnabled = true, TickFrequency = 10,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 10, 0)
            };
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);

            txt = CreateStyledInputBox();
            Grid.SetColumn(txt, 2);
            row.Children.Add(txt);

            TextBlock lblMs = new TextBlock { Text = "ms", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray, Margin = new Thickness(5, 0, 0, 0) };
            Grid.SetColumn(lblMs, 3);
            row.Children.Add(lblMs);

            Slider localSlider = slider;
            TextBox localTxt = txt;
            Action<int> valAction = onVal;

            localSlider.ValueChanged += delegate
            {
                if (isInternalUiUpdate) return;
                isInternalUiUpdate = true;
                int v = (int)localSlider.Value;
                localTxt.Text = v.ToString();
                valAction(v);
                CoreService.SaveSettings();
                RebuildTrayMenu();
                isInternalUiUpdate = false;
            };

            BindInputBox(localTxt, localSlider, delegate(int v)
            {
                valAction(v);
                CoreService.SaveSettings();
                RebuildTrayMenu();
            });

            parent.Children.Add(row);
        }

        private void TogglePerButtonMode(bool enable)
        {
            CoreService.EnablePerButtonMs = enable;
            CoreService.SaveSettings();
            UpdatePerButtonState();
            RebuildTrayMenu();
        }

        private void SetInputState(TextBox txt, Slider slider, bool enabled)
        {
            slider.IsEnabled = enabled;
            txt.IsReadOnly = !enabled;
            if (enabled)
            {
                txt.Background = Brushes.White;
                txt.Foreground = Brushes.Black;
            }
            else
            {
                txt.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                txt.Foreground = Brushes.Gray;
            }
        }

        private void UpdatePerButtonState()
        {
            bool perBtn = CoreService.EnablePerButtonMs;

            SetInputState(txtGlobal, sliderGlobal, !perBtn);

            SetInputState(txtLeft,   sliderLeft,   perBtn);
            SetInputState(txtRight,  sliderRight,  perBtn);
            SetInputState(txtMiddle, sliderMiddle, perBtn);
            SetInputState(txtX1,     sliderX1,     perBtn);
            SetInputState(txtX2,     sliderX2,     perBtn);
        }

        private void SyncControlsWithSettings()
        {
            isInternalUiUpdate = true;

            chkPerButtonMode.IsChecked = CoreService.EnablePerButtonMs;

            int g = Math.Max(10, Math.Min(500, CoreService.GlobalDebounceMs));
            sliderGlobal.Value = Math.Max(sliderGlobal.Minimum, Math.Min(sliderGlobal.Maximum, g));
            txtGlobal.Text = g.ToString();

            chkLeft.IsChecked = CoreService.EnableLeft;
            int l = Math.Max(10, Math.Min(500, CoreService.LeftMs));
            sliderLeft.Value = Math.Max(sliderLeft.Minimum, Math.Min(sliderLeft.Maximum, l));
            txtLeft.Text = l.ToString();

            chkRight.IsChecked = CoreService.EnableRight;
            int r = Math.Max(10, Math.Min(500, CoreService.RightMs));
            sliderRight.Value = Math.Max(sliderRight.Minimum, Math.Min(sliderRight.Maximum, r));
            txtRight.Text = r.ToString();

            chkMiddle.IsChecked = CoreService.EnableMiddle;
            int m = Math.Max(10, Math.Min(500, CoreService.MiddleMs));
            sliderMiddle.Value = Math.Max(sliderMiddle.Minimum, Math.Min(sliderMiddle.Maximum, m));
            txtMiddle.Text = m.ToString();

            chkX1.IsChecked = CoreService.EnableX1;
            int x1 = Math.Max(10, Math.Min(500, CoreService.X1Ms));
            sliderX1.Value = Math.Max(sliderX1.Minimum, Math.Min(sliderX1.Maximum, x1));
            txtX1.Text = x1.ToString();

            chkX2.IsChecked = CoreService.EnableX2;
            int x2 = Math.Max(10, Math.Min(500, CoreService.X2Ms));
            sliderX2.Value = Math.Max(sliderX2.Minimum, Math.Min(sliderX2.Maximum, x2));
            txtX2.Text = x2.ToString();

            chkHoldProtection.IsChecked = CoreService.EnableHoldProtection;
            chkSoundFeedback.IsChecked  = CoreService.EnableSoundFeedback;
            chkAutoStart.IsChecked      = CoreService.AutoStartEnabled;

            UpdatePerButtonState();
            UpdateStatsDisplay();
            UpdateHealthDisplay();

            isInternalUiUpdate = false;
        }

        private void UpdateStatsDisplay()
        {
            int c = CoreService.BlockedCount;
            lblTestStats.Text = string.Format("已累计为您吸收 {0} 次异常硬件杂波", c);
            if (statusMenuItem != null)
            {
                statusMenuItem.Text = string.Format("累计拦截: {0} 次", c);
            }
            UpdateTrayTooltip();
        }

        private void UpdateHealthDisplay()
        {
            int c = CoreService.BlockedCount;
            long minJ = (CoreService.MinJitterMs == 999) ? 0 : CoreService.MinJitterMs;

            if (c > 150 || CoreService.ExtremeJitterCount > 10)
            {
                lblHealthTitle.Text = "微动触点严重老化 (物理寿命将尽，请备好新鼠标/清洁剂)";
                lblHealthTitle.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                lblHealthDetails.Text = string.Format("累计拦截: {0}次 | 最短极端杂波: {1}ms | 告警: 触点硬抖动频发", c, minJ);
            }
            else if (c > 20)
            {
                lblHealthTitle.Text = "微动轻中度疲劳 (软件消抖稳定护航，使用正常)";
                lblHealthTitle.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
                lblHealthDetails.Text = string.Format("累计拦截: {0}次 | 最短杂波: {1}ms | 评估: 处于消抖黄金保护期", c, minJ);
            }
            else
            {
                lblHealthTitle.Text = "微动状态良好 (消抖算法平稳护航中)";
                lblHealthTitle.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
                lblHealthDetails.Text = string.Format("累计拦截: {0}次 | 最短杂波: {1}ms | 评估: 偶发氧化，完全不影响手感", c, minJ);
            }
        }

        private void InitTray()
        {
            trayIcon = new WF.NotifyIcon();
            trayIcon.Icon = CreateAppIcon();
            trayIcon.Visible = true;

            trayIcon.Click += delegate(object sender, EventArgs e)
            {
                WF.MouseEventArgs me = e as WF.MouseEventArgs;
                if (me != null && me.Button == WF.MouseButtons.Left)
                {
                    ShowControlPanel();
                }
            };
            trayIcon.DoubleClick += delegate { ShowControlPanel(); };

            RebuildTrayMenu();
        }

        public void ShowControlPanel()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        // 🌟 托盘菜单：彻底剔除 Emoji，纯净专业
        private void RebuildTrayMenu()
        {
            trayMenu = new WF.ContextMenuStrip();

            WF.ToolStripMenuItem openItem = new WF.ToolStripMenuItem("打开控制面板...");
            openItem.Font = new SD.Font(trayMenu.Font, SD.FontStyle.Bold);
            openItem.Click += delegate { ShowControlPanel(); };

            statusMenuItem = new WF.ToolStripMenuItem(string.Format("累计拦截: {0} 次", CoreService.BlockedCount));
            statusMenuItem.Enabled = false;

            WF.ToolStripSeparator sep1 = new WF.ToolStripSeparator();

            WF.ToolStripMenuItem btnMenu = new WF.ToolStripMenuItem("防护按键 (可多选)");
            AddButtonMenuItem(btnMenu, "鼠标左键", CoreService.EnableLeft, CoreService.LeftMs, delegate(bool en) { CoreService.EnableLeft = en; }, delegate(int ms) { CoreService.LeftMs = ms; });
            AddButtonMenuItem(btnMenu, "鼠标右键", CoreService.EnableRight, CoreService.RightMs, delegate(bool en) { CoreService.EnableRight = en; }, delegate(int ms) { CoreService.RightMs = ms; });
            AddButtonMenuItem(btnMenu, "滚轮中键", CoreService.EnableMiddle, CoreService.MiddleMs, delegate(bool en) { CoreService.EnableMiddle = en; }, delegate(int ms) { CoreService.MiddleMs = ms; });
            AddButtonMenuItem(btnMenu, "侧键 1 (后退)", CoreService.EnableX1, CoreService.X1Ms, delegate(bool en) { CoreService.EnableX1 = en; }, delegate(int ms) { CoreService.X1Ms = ms; });
            AddButtonMenuItem(btnMenu, "侧键 2 (前进)", CoreService.EnableX2, CoreService.X2Ms, delegate(bool en) { CoreService.EnableX2 = en; }, delegate(int ms) { CoreService.X2Ms = ms; });

            // 🎯 全局阈值项：纯粹毫秒数字，开启专属时自然变灰，无任何冗余文字
            string gTitle = string.Format("全局阈值: {0}ms", CoreService.GlobalDebounceMs);
            WF.ToolStripMenuItem globalMenu = new WF.ToolStripMenuItem(gTitle);
            globalMenu.Enabled = !CoreService.EnablePerButtonMs;

            int[] thresholds = new int[] { 40, 60, 80, 100, 120, 150 };
            foreach (int t in thresholds)
            {
                int val = t;
                WF.ToolStripMenuItem msItem = new WF.ToolStripMenuItem(val + "ms", null, delegate
                {
                    CoreService.GlobalDebounceMs = val;
                    CoreService.SaveSettings();
                    SyncControlsWithSettings();
                    RebuildTrayMenu();
                });
                msItem.Checked = (!CoreService.EnablePerButtonMs && CoreService.GlobalDebounceMs == val);
                globalMenu.DropDownItems.Add(msItem);
            }

            WF.ToolStripMenuItem perBtnItem = new WF.ToolStripMenuItem("独立按键 ms 模式");
            perBtnItem.Checked = CoreService.EnablePerButtonMs;
            perBtnItem.Click += delegate
            {
                CoreService.EnablePerButtonMs = !CoreService.EnablePerButtonMs;
                CoreService.SaveSettings();
                SyncControlsWithSettings();
                RebuildTrayMenu();
            };

            WF.ToolStripMenuItem holdItem = new WF.ToolStripMenuItem("长按防断触保护");
            holdItem.Checked = CoreService.EnableHoldProtection;
            holdItem.Click += delegate
            {
                CoreService.EnableHoldProtection = !CoreService.EnableHoldProtection;
                CoreService.SaveSettings();
                SyncControlsWithSettings();
                RebuildTrayMenu();
            };

            WF.ToolStripMenuItem sndItem = new WF.ToolStripMenuItem("拦截提示音效");
            sndItem.Checked = CoreService.EnableSoundFeedback;
            sndItem.Click += delegate
            {
                CoreService.EnableSoundFeedback = !CoreService.EnableSoundFeedback;
                CoreService.SaveSettings();
                SyncControlsWithSettings();
                RebuildTrayMenu();
            };

            WF.ToolStripMenuItem autoItem = new WF.ToolStripMenuItem("开机自动启动");
            autoItem.Checked = CoreService.AutoStartEnabled;
            autoItem.Click += delegate
            {
                bool newState = !autoItem.Checked;
                CoreService.SetAutoStart(newState);
                SyncControlsWithSettings();
                RebuildTrayMenu();
            };

            WF.ToolStripSeparator sep2 = new WF.ToolStripSeparator();

            WF.ToolStripMenuItem exitItem = new WF.ToolStripMenuItem("退出程序", null, delegate
            {
                trayIcon.Visible = false;
                Environment.Exit(0);
            });

            trayMenu.Items.Add(openItem);
            trayMenu.Items.Add(statusMenuItem);
            trayMenu.Items.Add(sep1);
            trayMenu.Items.Add(btnMenu);
            trayMenu.Items.Add(globalMenu);
            trayMenu.Items.Add(perBtnItem);
            trayMenu.Items.Add(holdItem);
            trayMenu.Items.Add(sndItem);
            trayMenu.Items.Add(autoItem);
            trayMenu.Items.Add(sep2);
            trayMenu.Items.Add(exitItem);

            trayIcon.ContextMenuStrip = trayMenu;
            UpdateTrayTooltip();
        }

        private void AddButtonMenuItem(WF.ToolStripMenuItem parent, string name, bool isEnabled, int specificMs, Action<bool> onToggle, Action<int> onSetMs)
        {
            WF.ToolStripMenuItem item = new WF.ToolStripMenuItem(name);
            item.Checked = isEnabled;
            Action<bool> toggle = onToggle;
            item.Click += delegate
            {
                toggle(!item.Checked);
                CoreService.SaveSettings();
                SyncControlsWithSettings();
                RebuildTrayMenu();
            };

            if (CoreService.EnablePerButtonMs)
            {
                int[] msOptions = new int[] { 40, 60, 80, 100, 120, 150 };
                foreach (int opt in msOptions)
                {
                    int val = opt;
                    Action<int> setVal = onSetMs;
                    WF.ToolStripMenuItem msSub = new WF.ToolStripMenuItem(val + "ms", null, delegate
                    {
                        setVal(val);
                        CoreService.SaveSettings();
                        SyncControlsWithSettings();
                        RebuildTrayMenu();
                    });
                    msSub.Checked = (specificMs == val);
                    item.DropDownItems.Add(msSub);
                }
            }

            parent.DropDownItems.Add(item);
        }

        private void UpdateTrayTooltip()
        {
            if (trayIcon != null)
            {
                string info;
                if (CoreService.EnablePerButtonMs)
                {
                    info = string.Format("鼠标防连击卫士 (已防护: {0}次)\n[专属模式: 右键{1}ms/左键{2}ms]",
                        CoreService.BlockedCount, CoreService.RightMs, CoreService.LeftMs);
                }
                else
                {
                    info = string.Format("鼠标防连击卫士 (已防护: {0}次)\n[全局阈值: {1}ms]",
                        CoreService.BlockedCount, CoreService.GlobalDebounceMs);
                }
                if (info.Length >= 64) info = info.Substring(0, 63);
                trayIcon.Text = info;
            }
        }

        // 🌟 图标方案 2：全新电竞鼠标 + 微动脉冲矢量绘制 (干净专业极客风)
        private SD.Icon CreateAppIcon()
        {
            using (SD.Bitmap bmp = new SD.Bitmap(32, 32))
            using (SD.Graphics g = SD.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(SD.Color.Transparent);

                // 科技深邃蓝圆形底盘
                using (SD.Brush bgBrush = new SD.SolidBrush(SD.Color.FromArgb(15, 23, 42)))
                {
                    g.FillEllipse(bgBrush, 1, 1, 30, 30);
                }

                // 外部微光保护环
                using (SD.Pen ringPen = new SD.Pen(SD.Color.FromArgb(0, 150, 255), 1.5f))
                {
                    g.DrawEllipse(ringPen, 2, 2, 28, 28);
                }

                // 鼠标主体外轮廓
                using (SD.Pen mousePen = new SD.Pen(SD.Color.White, 1.8f))
                {
                    // 鼠标机身圆角胶囊
                    g.DrawRectangle(mousePen, 10, 7, 12, 18);
                    // 左右按键中缝
                    g.DrawLine(mousePen, 16, 7, 16, 14);
                }

                // 滚轮标识 (科技青色)
                using (SD.Brush wheelBrush = new SD.SolidBrush(SD.Color.FromArgb(56, 189, 248)))
                {
                    g.FillRectangle(wheelBrush, 15, 9, 2, 4);
                }

                // 贯穿微动脉冲波 (象征消除杂波，平稳输出)
                using (SD.Pen pulsePen = new SD.Pen(SD.Color.FromArgb(52, 211, 153), 1.8f))
                {
                    SD.Point[] wave = new SD.Point[] {
                        new SD.Point(4, 17),
                        new SD.Point(7, 17),
                        new SD.Point(9, 13),
                        new SD.Point(11, 21),
                        new SD.Point(13, 17),
                        new SD.Point(28, 17)
                    };
                    g.DrawLines(pulsePen, wave);
                }

                return SD.Icon.FromHandle(bmp.GetHicon());
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }
    }

    public class App : Application
    {
        [STAThread]
        public static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "MouseDebounceGuard_SingleInstance_Mutex_WPF_V2", out createdNew))
            {
                if (!createdNew)
                {
                    WF.MessageBox.Show("鼠标防连击卫士已在运行中！\n请查看屏幕右下角托盘图标。", "提示", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Information);
                    return;
                }

                CoreService.Init();

                App app = new App();
                MainWindow wnd = new MainWindow();
                // 默认启动隐藏在托盘，不弹大窗口打扰
                wnd.Hide();

                app.Run();

                CoreService.Cleanup();
            }
        }
    }
}
