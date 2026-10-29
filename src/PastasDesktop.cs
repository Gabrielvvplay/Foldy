// Pastas de apps na área de trabalho, estilo iPhone/Android.
// Compilar com build.ps1 (usa o csc do .NET Framework que já vem no Windows).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Accessibility;
using Microsoft.Win32;

[assembly: AssemblyTitle("Pastas Desktop")]

namespace PastasDesktop
{
    static class Store
    {
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PastasDesktop");
        public static readonly string FoldersDir = Path.Combine(Root, "Pastas");
        public static readonly string IconsDir = Path.Combine(Root, "Icones");
        public static readonly string Exe = Assembly.GetExecutingAssembly().Location;

        public static string Desktop { get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } }
        public static string FolderPath(string name) { return Path.Combine(FoldersDir, name); }
        public static string DesktopLnk(string name) { return Path.Combine(Desktop, name + ".lnk"); }

        public static string CleanName(string s)
        {
            if (s == null) return "";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c.ToString(), "");
            return s.Trim().TrimEnd('.').Trim();
        }

        public static List<string> Names()
        {
            return Directory.GetDirectories(FoldersDir).Select(Path.GetFileName).OrderBy(n => n).ToList();
        }

        public static bool Exists(string name)
        {
            return CleanName(name) == name && name != "" && Directory.Exists(FolderPath(name));
        }

        public static List<string> Items(string name)
        {
            string dir = FolderPath(name);
            if (!Directory.Exists(dir)) return new List<string>();
            return new DirectoryInfo(dir).GetFiles()
                .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .OrderBy(f => f.CreationTimeUtc).ThenBy(f => f.Name)
                .Select(f => f.FullName).ToList();
        }

        static bool SameDir(string a, string b)
        {
            if (a == null || b == null) return false;
            return string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        public static string Unique(string dir, string fileName)
        {
            string path = Path.Combine(dir, fileName);
            string bare = Path.GetFileNameWithoutExtension(fileName), ext = Path.GetExtension(fileName);
            for (int i = 2; File.Exists(path) || Directory.Exists(path); i++)
                path = Path.Combine(dir, bare + " (" + i + ")" + ext);
            return path;
        }

        // Retorna uma mensagem de erro, ou null se deu certo.
        public static string CreateFolder(string name)
        {
            name = CleanName(name);
            if (name == "") return "";
            if (Directory.Exists(FolderPath(name))) return "Já existe uma pasta chamada \"" + name + "\".";
            if (File.Exists(DesktopLnk(name)) || NameTakenOnDesktop(name))
                return "Já tem um item chamado \"" + name + "\" na área de trabalho. Escolha outro nome.";
            Directory.CreateDirectory(FolderPath(name));
            UpdateDesktop(name);
            return null;
        }

        // Adiciona um arquivo/atalho à pasta. Atalhos que estão na área de trabalho do usuário
        // são movidos (como no celular); o resto vira um atalho novo ou uma cópia do atalho.
        public static void Add(string name, string src)
        {
            if (!File.Exists(src) && !Directory.Exists(src)) return;
            string dir = FolderPath(name);
            Directory.CreateDirectory(dir);
            string srcDir = Path.GetDirectoryName(src);
            if (SameDir(srcDir, dir)) return;

            string ext = Path.GetExtension(src).ToLowerInvariant();
            string dest;
            if (File.Exists(src) && (ext == ".lnk" || ext == ".url"))
            {
                if (ext == ".lnk" && IsFolderLnk(src)) return; // é outra pasta
                dest = Unique(dir, Path.GetFileName(src));
                bool moved = false;
                if (SameDir(srcDir, Desktop) || SameDir(Path.GetDirectoryName(srcDir), FoldersDir)) // da área de trabalho ou de outra pasta de apps
                {
                    try { File.Move(src, dest); moved = true; } catch { }
                    if (moved) Shell.NotifyDeleted(src); // some da área de trabalho na hora
                }
                if (!moved) File.Copy(src, dest);
            }
            else
            {
                string label = Directory.Exists(src) ? new DirectoryInfo(src).Name : Path.GetFileNameWithoutExtension(src);
                dest = Unique(dir, label + ".lnk");
                Shell.CreateLnk(dest, src, "", null, srcDir ?? "", null);
            }
            File.SetCreationTimeUtc(dest, DateTime.UtcNow);
        }

        // A área de trabalho mostra os nomes sem extensão, então um clique em "X" pode ser
        // a pasta de apps ou outro item chamado "X". Nesse caso o clique simples não age.
        public static bool NameTakenOnDesktop(string name)
        {
            string ours = DesktopLnk(name);
            string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            foreach (string dir in new[] { Desktop, publicDesktop })
            {
                if (!Directory.Exists(dir)) continue;
                if (Directory.Exists(Path.Combine(dir, name))) return true;
                foreach (string f in Directory.GetFiles(dir, name + ".*"))
                    if (Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(f, ours, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static bool IsFolderLnk(string lnk)
        {
            return string.Equals(Shell.LnkTarget(lnk), Exe, StringComparison.OrdinalIgnoreCase);
        }

        public static void MoveToDesktop(string item)
        {
            File.Move(item, Unique(Desktop, Path.GetFileName(item)));
        }

        // Retorna uma mensagem de erro, ou null se deu certo.
        public static string Rename(string oldName, string newName)
        {
            newName = CleanName(newName);
            if (newName == "" || newName == oldName) return "";
            if (Directory.Exists(FolderPath(newName)) && !string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase))
                return "Já existe uma pasta chamada \"" + newName + "\".";
            if (NameTakenOnDesktop(newName))
                return "Já tem um item chamado \"" + newName + "\" na área de trabalho. Escolha outro nome.";
            try
            {
                Directory.Move(FolderPath(oldName), FolderPath(newName));
                DeleteDesktopEntry(oldName);
                UpdateDesktop(newName);
                return null;
            }
            catch (Exception ex) { return "Não consegui renomear: " + ex.Message; }
        }

        public static void DeleteFolder(string name)
        {
            foreach (string item in Items(name)) { try { MoveToDesktop(item); } catch { } }
            try { Directory.Delete(FolderPath(name), true); } catch { }
            DeleteDesktopEntry(name);
        }

        static void DeleteDesktopEntry(string name)
        {
            try { File.Delete(DesktopLnk(name)); } catch { }
            DeleteIcons(name, null);
            Shell.NotifyDeleted(DesktopLnk(name));
        }

        static void DeleteIcons(string name, string keep, TimeSpan olderThan = default(TimeSpan))
        {
            if (!Directory.Exists(IconsDir)) return;
            DateTime limit = DateTime.UtcNow - olderThan;
            foreach (string f in Directory.GetFiles(IconsDir, name + "_*.ico"))
            {
                if (string.Equals(f, keep, StringComparison.OrdinalIgnoreCase) || File.GetLastWriteTimeUtc(f) > limit) continue;
                try { File.Delete(f); } catch { }
            }
        }

        // Gera o ícone com miniaturas dos apps e (re)cria o atalho na área de trabalho.
        // O .ico ganha um nome novo a cada mudança pra driblar o cache de ícones do Explorer.
        public static void UpdateDesktop(string name)
        {
            Directory.CreateDirectory(IconsDir);
            List<BitmapSource> icons = Items(name).Take(4).Select(p => IconUtil.Get(p)).Where(b => b != null).ToList();
            string ico = Path.Combine(IconsDir, name + "_" + DateTime.UtcNow.Ticks + ".ico");
            IconUtil.WriteIco(ico, new[] { 256, 64, 48, 32, 16 }.Select(s => IconUtil.RenderFolder(icons, s)).ToArray());
            string lnk = DesktopLnk(name);
            Shell.CreateLnk(lnk, Exe, "--abrir \"" + name + "\"", ico + ",0", Root, "Pasta de apps");
            Shell.Notify(lnk);
            DeleteIcons(name, ico, TimeSpan.FromSeconds(30)); // o Explorer pode ainda estar lendo o anterior
        }

        // Se o usuário renomeou o ícone da pasta direto na área de trabalho (F2), a pasta passa a
        // ter o nome novo. Sem isso o ícone "sumido" era recriado e ficavam dois ícones pra mesma pasta.
        public static void AdoptRenamedShortcuts()
        {
            foreach (string lnk in Directory.GetFiles(Desktop, "*.lnk"))
            {
                if (!IsFolderLnk(lnk)) continue;
                var m = System.Text.RegularExpressions.Regex.Match(Shell.LnkArguments(lnk), "\"([^\"]*)\"");
                if (!m.Success) continue;
                string folder = m.Groups[1].Value;
                string shown = Path.GetFileNameWithoutExtension(lnk);
                if (folder == shown || !Exists(folder)) continue;
                if (CleanName(shown) != shown) continue;
                bool caseOnly = string.Equals(folder, shown, StringComparison.OrdinalIgnoreCase);
                if (!caseOnly && Directory.Exists(FolderPath(shown)))
                {
                    Log.Write("Ícone \"" + shown + "\" aponta pra pasta \"" + folder + "\", mas já existe uma pasta \"" + shown + "\".");
                    continue;
                }

                string duplicate = DesktopLnk(folder);
                if (!caseOnly && File.Exists(duplicate)) { File.Delete(duplicate); Shell.NotifyDeleted(duplicate); }
                if (caseOnly)
                {
                    string temp = FolderPath(folder + " ~" + Guid.NewGuid().ToString("N"));
                    Directory.Move(FolderPath(folder), temp);
                    Directory.Move(temp, FolderPath(shown));
                }
                else Directory.Move(FolderPath(folder), FolderPath(shown));
                DeleteIcons(folder, null);
                UpdateDesktop(shown);
                Log.Write("Pasta renomeada pela área de trabalho: \"" + folder + "\" -> \"" + shown + "\".");
            }
        }

        // Restos da versão em que os ícones eram janelas próprias.
        public static void CleanupLegacy()
        {
            try { File.Delete(Path.Combine(Root, "posicoes.txt")); } catch { }
        }

        const string MenuKey = @"Software\Classes\DesktopBackground\Shell\PastasDesktop";

        public static void InstallMenu()
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(MenuKey))
            {
                k.SetValue("", "Nova pasta de apps");
                k.SetValue("Icon", "imageres.dll,-3");
                using (RegistryKey c = k.CreateSubKey("command"))
                    c.SetValue("", "\"" + Exe + "\" --nova");
            }
        }

        public static void UninstallMenu()
        {
            Registry.CurrentUser.DeleteSubKeyTree(MenuKey, false);
        }
    }

    static class Log
    {
        public static void Write(string text)
        {
            try { File.AppendAllText(Path.Combine(Store.Root, "log.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine); }
            catch { }
        }
    }

    static class Shell
    {
        static object Invoke(object o, string member, BindingFlags kind, params object[] args)
        {
            return o.GetType().InvokeMember(member, kind, null, o, args);
        }

        static object NewShortcut(string lnk)
        {
            object sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            return Invoke(sh, "CreateShortcut", BindingFlags.InvokeMethod, lnk);
        }

        public static void CreateLnk(string lnk, string target, string args, string icon, string workDir, string description)
        {
            object sc = NewShortcut(lnk);
            Invoke(sc, "TargetPath", BindingFlags.SetProperty, target);
            Invoke(sc, "Arguments", BindingFlags.SetProperty, args ?? "");
            Invoke(sc, "WorkingDirectory", BindingFlags.SetProperty, workDir ?? "");
            if (icon != null) Invoke(sc, "IconLocation", BindingFlags.SetProperty, icon);
            if (description != null) Invoke(sc, "Description", BindingFlags.SetProperty, description);
            Invoke(sc, "Save", BindingFlags.InvokeMethod);
        }

        public static string LnkTarget(string lnk)
        {
            try { return (string)Invoke(NewShortcut(lnk), "TargetPath", BindingFlags.GetProperty); }
            catch { return ""; }
        }

        public static string LnkArguments(string lnk)
        {
            try { return (string)Invoke(NewShortcut(lnk), "Arguments", BindingFlags.GetProperty); }
            catch { return ""; }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern void SHChangeNotify(int eventId, int flags, string item1, IntPtr item2);

        // SHCNF_PATHW | SHCNF_FLUSHNOWAIT: sem o "flush" o Explorer junta os avisos e só redesenha bem depois.
        const int PathFlush = 0x0005 | 0x3000;

        public static void Notify(string path)
        {
            SHChangeNotify(0x00002000, PathFlush, path, IntPtr.Zero);      // SHCNE_UPDATEITEM
            SHChangeNotify(0x00001000, PathFlush, Path.GetDirectoryName(path) ?? path, IntPtr.Zero); // SHCNE_UPDATEDIR
        }

        // Sem isso o Explorer às vezes continua mostrando o ícone apagado (principalmente no Desktop do OneDrive).
        public static void NotifyDeleted(string path)
        {
            SHChangeNotify(0x00000004, PathFlush, path, IntPtr.Zero);      // SHCNE_DELETE
            SHChangeNotify(0x00001000, PathFlush, Path.GetDirectoryName(path) ?? path, IntPtr.Zero);
        }
    }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(uint pid);
        [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
        [DllImport("oleacc.dll")]
        public static extern int AccessibleObjectFromPoint(POINT p, [MarshalAs(UnmanagedType.Interface)] out IAccessible acc, out object child);

        public static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        // Uma janela em segundo plano só consegue ir pra frente "pegando carona" na janela ativa.
        public static void ForceForeground(IntPtr hwnd)
        {
            uint pid;
            uint fg = GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            uint me = GetCurrentThreadId();
            if (fg != 0 && fg != me)
            {
                AttachThreadInput(me, fg, true);
                SetForegroundWindow(hwnd);
                AttachThreadInput(me, fg, false);
            }
            else SetForegroundWindow(hwnd);
        }
    }

    static class IconUtil
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IImageList
        {
            [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
            [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
            [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
            [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
            [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
            [PreserveSig] int Draw(IntPtr pimldp);
            [PreserveSig] int Remove(int i);
            [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("shell32.dll", EntryPoint = "#727")]
        static extern int SHGetImageList(int list, ref Guid riid, out IImageList ppv);
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr h);

        const int SHIL_EXTRALARGE = 2, SHIL_JUMBO = 4;

        // Ícones já extraídos, por caminho + data de modificação (extrair é a parte lenta).
        static readonly Dictionary<string, KeyValuePair<DateTime, BitmapSource>> cache =
            new Dictionary<string, KeyValuePair<DateTime, BitmapSource>>(StringComparer.OrdinalIgnoreCase);

        public static BitmapSource Get(string path)
        {
            DateTime stamp;
            try { stamp = File.GetLastWriteTimeUtc(path); } catch { stamp = DateTime.MinValue; }
            KeyValuePair<DateTime, BitmapSource> hit;
            if (cache.TryGetValue(path, out hit) && hit.Key == stamp) return hit.Value;
            BitmapSource b = Extract(path);
            if (cache.Count > 500) cache.Clear();
            cache[path] = new KeyValuePair<DateTime, BitmapSource>(stamp, b);
            return b;
        }

        static BitmapSource Extract(string path)
        {
            var info = new SHFILEINFO();
            if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), 0x4000 /* SHGFI_SYSICONINDEX */) == IntPtr.Zero) return null;
            BitmapSource jumbo = FromList(SHIL_JUMBO, info.iIcon);
            if (jumbo != null && !OnlySmall(jumbo)) return jumbo;
            return FromList(SHIL_EXTRALARGE, info.iIcon) ?? jumbo;
        }

        static BitmapSource FromList(int list, int index)
        {
            IImageList il;
            Guid iid = typeof(IImageList).GUID;
            if (SHGetImageList(list, ref iid, out il) != 0 || il == null) return null;
            IntPtr h = IntPtr.Zero;
            il.GetIcon(index, 1 /* ILD_TRANSPARENT */, ref h);
            if (h == IntPtr.Zero) return null;
            try
            {
                BitmapSource b = Imaging.CreateBitmapSourceFromHIcon(h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                b.Freeze();
                return b;
            }
            finally { DestroyIcon(h); }
        }

        // Apps sem ícone grande vêm como um ícone 48px no canto de um quadro 256px.
        static bool OnlySmall(BitmapSource b)
        {
            var conv = new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            if (w <= 48) return false;
            var px = new int[w * h];
            conv.CopyPixels(px, w * 4, 0);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if ((x >= 48 || y >= 48) && ((px[y * w + x] >> 24) & 0xff) != 0) return false;
            return true;
        }

        public static BitmapSource RenderFolder(List<BitmapSource> icons, int size)
        {
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (DrawingContext dc = dv.RenderOpen())
            {
                double k = size / 256.0;
                dc.PushTransform(new ScaleTransform(k, k));
                var bg = new LinearGradientBrush(Color.FromArgb(225, 70, 70, 82), Color.FromArgb(225, 34, 34, 42), 90);
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), size <= 32 ? 10 : 5);
                dc.DrawRoundedRectangle(bg, pen, new Rect(10, 10, 236, 236), 58, 58);
                const double pad = 40, gap = 16;
                double cell = (256 - 2 * pad - gap) / 2;
                for (int i = 0; i < icons.Count && i < 4; i++)
                    dc.DrawImage(icons[i], new Rect(pad + (i % 2) * (cell + gap), pad + (i / 2) * (cell + gap), cell, cell));
                dc.Pop();
            }
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        public static byte[] Png(BitmapSource b)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(b));
            using (var ms = new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
        }

        public static void WriteIco(string file, BitmapSource[] images)
        {
            List<byte[]> pngs = images.Select(Png).ToList();
            using (var w = new BinaryWriter(File.Create(file)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)images.Length);
                int offset = 6 + 16 * images.Length;
                for (int i = 0; i < images.Length; i++)
                {
                    int s = images[i].PixelWidth;
                    w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (byte[] p in pngs) w.Write(p);
            }
        }
    }

    static class Theme
    {
        public static readonly Brush Card = new SolidColorBrush(Color.FromArgb(238, 30, 30, 36));
        public static readonly Brush Stroke = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));
        public static readonly Brush Hover = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
        public static readonly Brush Text = Brushes.White;
        public static readonly Brush Muted = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
        public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Text, Segoe UI");

        public static Border MakeCard(UIElement child)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(26),
                Background = Card,
                BorderBrush = Stroke,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 14, 16, 16),
                Margin = new Thickness(28),
                Child = child,
                Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 8, Opacity = 0.45, Color = Colors.Black },
            };
        }

        public static Button MakeButton(string text, bool primary)
        {
            var label = new TextBlock { Text = text, Foreground = Text, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
            var bd = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18, 7, 18, 8),
                Background = primary ? new SolidColorBrush(Color.FromRgb(10, 132, 255)) : Hover,
                Child = label,
            };
            var tpl = new ControlTemplate(typeof(Button));
            tpl.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
            var b = new Button { Content = bd, Template = tpl, Cursor = Cursors.Hand, Margin = new Thickness(8, 0, 0, 0) };
            b.MouseEnter += delegate { bd.Opacity = 0.85; };
            b.MouseLeave += delegate { bd.Opacity = 1; };
            return b;
        }

        public static MenuItem MenuItem(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += delegate { action(); };
            return mi;
        }

        public static void Message(Window owner, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (owner != null) MessageBox.Show(owner, text, "Pastas", MessageBoxButton.OK, MessageBoxImage.Information);
            else MessageBox.Show(text, "Pastas", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    class InputDialog : Window
    {
        readonly TextBox box;
        bool ok;

        InputDialog(string heading, string value, string okText)
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight; Topmost = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; FontFamily = Theme.Font; Title = heading;

            var title = new TextBlock { Text = heading, Foreground = Theme.Text, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 12) };
            box = new TextBox
            {
                Text = value, FontSize = 15, Width = 300, Padding = new Thickness(8, 6, 8, 6),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), Foreground = Theme.Text,
                CaretBrush = Theme.Text, BorderBrush = Theme.Stroke, SelectionBrush = new SolidColorBrush(Color.FromRgb(10, 132, 255)),
            };
            var cancel = Theme.MakeButton("Cancelar", false);
            var confirm = Theme.MakeButton(okText, true);
            cancel.Click += delegate { Close(); };
            confirm.Click += delegate { ok = true; Close(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            buttons.Children.Add(cancel);
            buttons.Children.Add(confirm);
            var stack = new StackPanel();
            stack.Children.Add(title);
            stack.Children.Add(box);
            stack.Children.Add(buttons);
            var card = Theme.MakeCard(stack);
            card.Padding = new Thickness(20, 16, 20, 18);
            card.MouseLeftButtonDown += delegate { try { DragMove(); } catch { } };
            Content = card;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { ok = true; Close(); }
                else if (e.Key == Key.Escape) Close();
            };
            Loaded += delegate { Native.ForceForeground(new WindowInteropHelper(this).Handle); Activate(); box.Focus(); box.SelectAll(); };
        }

        public static string Ask(Window owner, string heading, string value, string okText)
        {
            var d = new InputDialog(heading, value, okText);
            if (owner != null && owner.IsVisible) { d.Owner = owner; d.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            d.ShowDialog();
            return d.ok ? d.box.Text : null;
        }
    }

    // O popup com os apps da pasta.
    class FolderWindow : Window
    {
        const double TileW = 96;
        public string FolderName { get { return name; } }
        string name;
        readonly System.Drawing.Point anchor;
        readonly TextBlock title, hint;
        readonly WrapPanel grid;
        readonly Border card;
        readonly ScaleTransform zoom = new ScaleTransform(0.88, 0.88);
        readonly DateTime openedAt = DateTime.UtcNow;
        Border pressedTile;
        Point pressedAt;
        bool suppress, closing;

        public FolderWindow(string name, System.Drawing.Point anchor)
        {
            this.name = name;
            this.anchor = anchor;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual; Left = -20000; Top = -20000;
            FontFamily = Theme.Font; Title = name;

            title = new TextBlock
            {
                Text = name, Foreground = Theme.Text, FontSize = 19, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12),
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360,
            };
            hint = new TextBlock
            {
                Text = "Pasta vazia.\nArraste atalhos pra cima do ícone dela na área de trabalho.",
                Foreground = Theme.Muted, FontSize = 12, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 10),
            };
            grid = new WrapPanel();
            var scroller = new ScrollViewer
            {
                Content = grid, MaxHeight = 480,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            var stack = new StackPanel();
            stack.Children.Add(title);
            stack.Children.Add(hint);
            stack.Children.Add(scroller);

            card = Theme.MakeCard(stack);
            card.Opacity = 0;
            card.RenderTransform = zoom;
            card.RenderTransformOrigin = new Point(0.5, 0.5);
            card.ContextMenu = FolderMenu();
            Content = card;

            AllowDrop = true;
            Drop += OnDrop;
            Deactivated += delegate { if (!suppress) CloseAnimated(); };
            ContextMenuOpening += delegate { suppress = true; };
            ContextMenuClosing += delegate
            {
                suppress = false;
                Dispatcher.BeginInvoke(new Action(() => { if (!IsActive && !suppress) CloseAnimated(); }), DispatcherPriority.Background);
            };
            KeyDown += (s, e) => { if (e.Key == Key.Escape) CloseAnimated(); };
            SizeChanged += delegate { Reposition(); };
            Loaded += delegate
            {
                Reposition();
                Animate(1, 1, 170, null);
                Native.ForceForeground(new WindowInteropHelper(this).Handle);
                Activate();
            };
            Build();
        }

        void Build()
        {
            grid.Children.Clear();
            List<string> items = Store.Items(name);
            grid.Width = (items.Count <= 3 ? 3 : 4) * TileW;
            foreach (string p in items) grid.Children.Add(MakeTile(p));
            grid.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            hint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void Changed()
        {
            if (Store.Items(name).Count == 0)
            {
                // Último app saiu: a pasta some (no residente quem apaga é o Sync, aqui só fecha).
                if (!Program.IsResident) Store.DeleteFolder(name);
                closing = true;
                Close();
                return;
            }
            Build();
            if (Program.IsResident) return; // o residente percebe a mudança e refaz o ícone
            try { Store.UpdateDesktop(name); } catch (Exception ex) { Message("Não consegui atualizar o ícone: " + ex.Message); }
        }

        // Um toque só conta se começou e terminou no mesmo item, e não é o resto do
        // duplo clique que abriu a pasta (o popup aparece bem em cima do ícone).
        // Segurar e arrastar tira o app da pasta.
        Border Tile(Image icon, string label, string tooltip, string path)
        {
            var text = new TextBlock
            {
                Text = label, Foreground = Theme.Text, FontSize = 12, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 34, Margin = new Thickness(0, 6, 0, 0),
            };
            var sp = new StackPanel();
            sp.Children.Add(icon);
            sp.Children.Add(text);
            var b = new Border
            {
                Width = TileW - 8, Height = 106, Margin = new Thickness(4), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(6, 10, 6, 4), Background = Brushes.Transparent, Child = sp,
                Cursor = Cursors.Hand, ToolTip = tooltip,
            };
            b.MouseEnter += delegate { b.Background = Theme.Hover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += (s, e) =>
            {
                bool tooSoon = (DateTime.UtcNow - openedAt).TotalMilliseconds < Native.GetDoubleClickTime();
                pressedTile = tooSoon ? null : b;
                pressedAt = e.GetPosition(this);
            };
            b.MouseMove += (s, e) =>
            {
                if (pressedTile != b || e.LeftButton != MouseButtonState.Pressed) return;
                Vector d = e.GetPosition(this) - pressedAt;
                if (Math.Abs(d.X) < 6 && Math.Abs(d.Y) < 6) return;
                pressedTile = null;
                b.Background = Brushes.Transparent;
                DragOut(path, icon.Source);
            };
            b.MouseLeftButtonUp += delegate
            {
                if (pressedTile != b) return;
                pressedTile = null;
                Launch(path);
            };
            return b;
        }

        // O popup some pra mostrar a área de trabalho e o Explorer move o atalho pra onde ele for solto.
        void DragOut(string path, ImageSource image)
        {
            suppress = true;
            var ghost = new DragGhost(image);
            ghost.Show();
            ghost.FollowCursor();
            Animate(0, 0.94, 140, null); // transparente = os cliques e o "soltar" passam pra área de trabalho
            GiveFeedbackEventHandler follow = delegate { ghost.FollowCursor(); };
            GiveFeedback += follow;
            try { DragDrop.DoDragDrop(this, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Move); }
            catch (Exception ex) { Log.Write("Arrastando " + path + ": " + ex.Message); }
            GiveFeedback -= follow;
            ghost.Close();
            suppress = false;

            // O Explorer às vezes termina de mover um instante depois: confere a cada 50 ms, por até 0,5 s.
            int tries = 0;
            var check = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            check.Tick += delegate
            {
                if (File.Exists(path) && ++tries < 10) return;
                check.Stop();
                AfterDragOut(path);
            };
            if (File.Exists(path)) check.Start();
            else AfterDragOut(path);
        }

        void AfterDragOut(string path)
        {
            if (File.Exists(path))
            {
                Animate(1, 1, 140, null);
                Native.ForceForeground(new WindowInteropHelper(this).Handle);
                Activate();
                return;
            }
            Changed();
            if (closing) return; // a pasta ficou vazia e já fechou
            closing = true;
            Close();
        }

        Border MakeTile(string path)
        {
            var img = new Image { Source = IconUtil.Get(path), Width = 52, Height = 52 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            string label = Path.GetFileNameWithoutExtension(path);
            Border tile = Tile(img, label, label, path);

            var menu = new ContextMenu();
            menu.Items.Add(Theme.MenuItem("Abrir", () => Launch(path)));
            menu.Items.Add(Theme.MenuItem("Tirar da pasta (volta pra área de trabalho)", () =>
            {
                try { Store.MoveToDesktop(path); } catch (Exception ex) { Message(ex.Message); }
                Changed();
            }));
            menu.Items.Add(Theme.MenuItem("Excluir atalho", () =>
            {
                if (!Confirm("Excluir o atalho \"" + label + "\"?\n(O programa em si não é desinstalado.)")) return;
                try { File.Delete(path); } catch (Exception ex) { Message(ex.Message); }
                Changed();
            }));
            menu.Items.Add(new Separator());
            foreach (MenuItem mi in FolderMenuItems()) menu.Items.Add(mi);
            tile.ContextMenu = menu;
            return tile;
        }

        List<MenuItem> FolderMenuItems()
        {
            return new List<MenuItem>
            {
                Theme.MenuItem("Adicionar apps...", AddViaDialog),
                Theme.MenuItem("Renomear pasta...", RenameFolder),
                Theme.MenuItem("Abrir no Explorador", () =>
                {
                    Process.Start("explorer.exe", "\"" + Store.FolderPath(name) + "\"");
                    CloseAnimated();
                }),
                Theme.MenuItem("Excluir pasta", DeleteFolder),
            };
        }

        ContextMenu FolderMenu()
        {
            var menu = new ContextMenu();
            foreach (MenuItem mi in FolderMenuItems()) menu.Items.Add(mi);
            return menu;
        }

        void Launch(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Message("Não consegui abrir: " + ex.Message); return; }
            CloseAnimated();
        }

        void AddViaDialog()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Adicionar à pasta " + name,
                Multiselect = true,
                DereferenceLinks = false,
                Filter = "Apps e atalhos|*.lnk;*.url;*.exe;*.bat;*.cmd|Todos os arquivos|*.*",
                InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            };
            suppress = true;
            bool? ok = dlg.ShowDialog(this);
            suppress = false;
            Activate();
            if (ok != true) return;
            AddAll(dlg.FileNames);
        }

        void AddAll(IEnumerable<string> paths)
        {
            foreach (string p in paths)
            {
                try { Store.Add(name, p); }
                catch (Exception ex) { Message("Não consegui adicionar " + Path.GetFileName(p) + ": " + ex.Message); }
            }
            Changed();
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) AddAll(files);
        }

        void RenameFolder()
        {
            suppress = true;
            string n = Store.CleanName(InputDialog.Ask(this, "Renomear pasta", name, "Salvar"));
            string err = Store.Rename(name, n);
            suppress = false;
            Activate();
            if (err != null) { Message(err); return; }
            name = n;
            title.Text = n;
            Title = n;
        }

        void DeleteFolder()
        {
            if (!Confirm("Excluir a pasta \"" + name + "\"?\nOs atalhos dentro dela voltam pra área de trabalho.")) return;
            Store.DeleteFolder(name);
            closing = true;
            Close();
        }

        bool Confirm(string text)
        {
            suppress = true;
            bool yes = MessageBox.Show(this, text, "Pastas", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            suppress = false;
            Activate();
            return yes;
        }

        void Message(string text)
        {
            suppress = true;
            Theme.Message(this, text);
            suppress = false;
            Activate();
        }

        // Centraliza o popup em cima do ícone da pasta, sem sair da tela.
        void Reposition()
        {
            var src = PresentationSource.FromVisual(this);
            if (src == null || ActualWidth == 0) return;
            Matrix m = src.CompositionTarget.TransformFromDevice;
            System.Drawing.Rectangle wa = System.Windows.Forms.Screen.FromPoint(anchor).WorkingArea;
            Point tl = m.Transform(new Point(wa.Left, wa.Top));
            Point br = m.Transform(new Point(wa.Right, wa.Bottom));
            Point a = m.Transform(new Point(anchor.X, anchor.Y));
            double w = ActualWidth, h = ActualHeight;
            Left = Math.Max(tl.X, Math.Min(a.X - w / 2, br.X - w));
            Top = Math.Max(tl.Y, Math.Min(a.Y - h / 2, br.Y - h));
        }

        void Animate(double opacity, double scale, int ms, EventHandler done)
        {
            var dur = new Duration(TimeSpan.FromMilliseconds(ms));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var fade = new DoubleAnimation(opacity, dur) { EasingFunction = ease };
            if (done != null) fade.Completed += done;
            card.BeginAnimation(OpacityProperty, fade);
            zoom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, dur) { EasingFunction = ease });
            zoom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, dur) { EasingFunction = ease });
        }

        void CloseAnimated()
        {
            if (closing) return;
            closing = true;
            Animate(0, 0.94, 110, delegate { Close(); });
        }
    }

    // Miniatura do app que segue o mouse enquanto ele é arrastado pra fora da pasta.
    // Não recebe cliques, pra o "soltar" cair na área de trabalho embaixo dela.
    class DragGhost : Window
    {
        const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        const double Size = 60;

        public DragGhost(ImageSource image)
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false; IsHitTestVisible = false;
            ResizeMode = ResizeMode.NoResize; Width = Size; Height = Size;
            WindowStartupLocation = WindowStartupLocation.Manual; Left = -20000; Top = -20000;
            var img = new Image
            {
                Source = image, Width = Size, Height = Size, Opacity = 0.9,
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 4, Opacity = 0.5, Color = Colors.Black },
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            Content = img;
            SourceInitialized += delegate
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                long ex = Native.GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
                Native.SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
            };
        }

        public void FollowCursor()
        {
            var src = PresentationSource.FromVisual(this);
            Native.POINT p;
            if (src == null || !Native.GetCursorPos(out p)) return;
            Point pt = src.CompositionTarget.TransformFromDevice.Transform(new Point(p.X, p.Y));
            Left = pt.X - Size / 2;
            Top = pt.Y - Size / 2;
        }
    }

    // Observa os cliques do mouse numa thread própria (pra nunca travar o mouse)
    // e avisa quando foi um clique simples, sem arrastar.
    class ClickWatcher
    {
        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT { public int x, y; public uint mouseData, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        const int WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202;

        readonly Action onClick;
        readonly HookProc proc; // precisa ficar referenciado pra o GC não recolher
        int downX, downY;
        uint downTime;

        public ClickWatcher(Action onClick)
        {
            this.onClick = onClick;
            proc = Callback;
        }

        public void Start()
        {
            var t = new Thread(() =>
            {
                if (SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0) == IntPtr.Zero)
                {
                    Log.Write("Não consegui instalar o gancho do mouse: " + Marshal.GetLastWin32Error());
                    return;
                }
                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { }
            });
            t.IsBackground = true;
            t.Start();
        }

        IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                int m = wParam.ToInt32();
                if (m == WM_LBUTTONDOWN || m == WM_LBUTTONUP)
                {
                    var s = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    if (m == WM_LBUTTONDOWN) { downX = s.x; downY = s.y; downTime = s.time; }
                    else if (Math.Abs(s.x - downX) <= 4 && Math.Abs(s.y - downY) <= 4 && s.time - downTime < 1000)
                        onClick();
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }
    }

    // Fica rodando em segundo plano: abre as pastas com um clique e mantém os ícones atualizados.
    class Resident
    {
        public const string WindowTitle = "PastasDesktopResidente";
        const int WM_COPYDATA = 0x4A;

        readonly Dictionary<string, string> signatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly DispatcherTimer debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        FileSystemWatcher watcher, desktopWatcher;
        HwndSource mailbox;
        ClickWatcher clicks;
        FolderWindow popup;
        DateTime popupOpenedAt;

        public void Start()
        {
            Store.CleanupLegacy();

            // Janela invisível que recebe "abra a pasta X" de outras cópias do programa (duplo clique, Enter).
            mailbox = new HwndSource(new HwndSourceParameters(WindowTitle) { WindowStyle = 0, Width = 0, Height = 0 });
            mailbox.AddHook(MailboxProc);

            debounce.Tick += delegate { debounce.Stop(); Sync(); };
            watcher = new FileSystemWatcher(Store.FoldersDir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            FileSystemEventHandler kick = delegate { Application.Current.Dispatcher.BeginInvoke(new Action(Kick)); };
            watcher.Created += kick;
            watcher.Deleted += kick;
            watcher.Changed += kick;
            watcher.Renamed += (s, e) => kick(s, e);
            watcher.Error += (s, e) => Log.Write("Watcher: " + e.GetException().Message);
            watcher.EnableRaisingEvents = true;

            // Percebe quando o ícone de uma pasta é renomeado ou apagado direto na área de trabalho.
            desktopWatcher = new FileSystemWatcher(Store.Desktop, "*.lnk") { NotifyFilter = NotifyFilters.FileName };
            desktopWatcher.Renamed += (s, e) => kick(s, e);
            desktopWatcher.Deleted += kick;
            desktopWatcher.Error += (s, e) => Log.Write("Watcher da área de trabalho: " + e.GetException().Message);
            desktopWatcher.EnableRaisingEvents = true;

            Dispatcher d = Application.Current.Dispatcher;
            clicks = new ClickWatcher(() => d.BeginInvoke(new Action(OnClick)));
            clicks.Start();

            Sync();
        }

        void Kick()
        {
            debounce.Stop();
            debounce.Start();
        }

        // Recria o ícone de uma pasta quando os apps mudam ou quando o atalho sumiu.
        // Como no celular, a pasta se apaga quando o último app sai dela.
        void Sync()
        {
            try { Store.AdoptRenamedShortcuts(); }
            catch (Exception ex) { Log.Write("Renomeação pela área de trabalho: " + ex); }
            try
            {
                foreach (string n in Store.Names())
                {
                    string sig = string.Join("|", Store.Items(n).Take(4));
                    string old;
                    if (sig == "" && signatures.TryGetValue(n, out old) && old != "")
                    {
                        Store.DeleteFolder(n);
                        signatures.Remove(n);
                        continue;
                    }
                    if (signatures.TryGetValue(n, out old) && old == sig && File.Exists(Store.DesktopLnk(n))) continue;
                    var sw = Stopwatch.StartNew();
                    Store.UpdateDesktop(n);
                    signatures[n] = sig;
                    if (sw.ElapsedMilliseconds > 300) Log.Write("Ícone de " + n + " demorou " + sw.ElapsedMilliseconds + " ms pra ser refeito.");
                }
            }
            catch (Exception ex) { Log.Write("Sync: " + ex); }
        }

        // Clique simples: se foi no ícone de uma das pastas na área de trabalho, abre ela.
        void OnClick()
        {
            Native.POINT pt;
            if (!Native.GetCursorPos(out pt)) return;
            IntPtr h = Native.WindowFromPoint(pt);
            if (Native.ClassOf(h) != "SysListView32" || Native.ClassOf(Native.GetParent(h)) != "SHELLDLL_DefView") return;
            string root = Native.ClassOf(Native.GetAncestor(h, 2 /* GA_ROOT */));
            if (root != "Progman" && root != "WorkerW") return;

            string name;
            try
            {
                IAccessible acc;
                object child;
                if (Native.AccessibleObjectFromPoint(pt, out acc, out child) != 0 || acc == null) return;
                name = acc.get_accName(child);
            }
            catch { return; }
            if (string.IsNullOrEmpty(name)) return;
            if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            if (!Store.Exists(name) || !File.Exists(Store.DesktopLnk(name)) || Store.NameTakenOnDesktop(name)) return;
            Open(name, new System.Drawing.Point(pt.X, pt.Y));
        }

        void Open(string name, System.Drawing.Point at)
        {
            // O duplo clique do Explorer também chega aqui; não abre a mesma pasta duas vezes.
            if (popup != null && string.Equals(popup.FolderName, name, StringComparison.OrdinalIgnoreCase)
                && (popup.IsVisible || (DateTime.UtcNow - popupOpenedAt).TotalMilliseconds < 1500)) return;
            popup = new FolderWindow(name, at);
            popupOpenedAt = DateTime.UtcNow;
            popup.Show();
        }

        IntPtr MailboxProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_COPYDATA)
            {
                // "pasta" (abrir) ou "pasta\narquivo\narquivo..." (arquivos soltos em cima do ícone).
                var cds = (Client.COPYDATASTRUCT)Marshal.PtrToStructure(lParam, typeof(Client.COPYDATASTRUCT));
                string[] parts = Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2).TrimEnd('\0').Split('\n');
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    string name = Store.CleanName(parts[0]);
                    if (!Store.Exists(name)) return;
                    if (parts.Length == 1) { Open(name, System.Windows.Forms.Cursor.Position); return; }
                    foreach (string f in parts.Skip(1))
                    {
                        try { Store.Add(name, f); } catch (Exception ex) { Log.Write("Adicionando " + f + ": " + ex.Message); }
                    }
                    debounce.Stop();
                    Sync(); // refaz o ícone já, sem esperar o aviso de mudança
                }));
                handled = true;
                return new IntPtr(1);
            }
            return IntPtr.Zero;
        }
    }

    // Repassa o pedido pro residente sem carregar o WPF (é o que deixa o duplo clique e o
    // "soltar em cima da pasta" rápidos). Não pode usar nenhum tipo do WPF aqui.
    static class Client
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, ref COPYDATASTRUCT data);

        // Retorna false se o residente não está rodando.
        public static bool TrySend(string[] args)
        {
            List<string> rest = args.SkipWhile(a => a == "--abrir").ToList();
            if (rest.Count == 0) return true;
            IntPtr h = FindWindow(null, "PastasDesktopResidente");
            if (h == IntPtr.Zero) return false;
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            AllowSetForegroundWindow(pid);
            string text = string.Join("\n", rest);
            IntPtr data = Marshal.StringToHGlobalUni(text);
            try
            {
                var cds = new COPYDATASTRUCT { cbData = (text.Length + 1) * 2, lpData = data };
                SendMessage(h, 0x4A /* WM_COPYDATA */, IntPtr.Zero, ref cds);
            }
            finally { Marshal.FreeHGlobal(data); }
            return true;
        }
    }

    static class Program
    {
        public static bool IsResident;

        static void StartResident()
        {
            try { Process.Start(Store.Exe, "--residente"); } catch (Exception ex) { Log.Write("Iniciando residente: " + ex.Message); }
        }

        [STAThread]
        static void Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0] : "--residente";
            if (!cmd.StartsWith("--") || cmd == "--abrir")
            {
                if (Client.TrySend(args)) return;
            }
            Run(args);
        }

        // Separado do Main pra o WPF só ser carregado quando precisa mesmo.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void Run(string[] args)
        {
            Directory.CreateDirectory(Store.FoldersDir);
            Directory.CreateDirectory(Store.IconsDir);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("Erro fatal: " + e.ExceptionObject);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log.Write("Erro: " + e.Exception);
                e.Handled = true; // continua rodando
            };

            string cmd = args.Length > 0 ? args[0] : "--residente";
            if (cmd == "--residente")
            {
                bool first;
                var mutex = new Mutex(true, @"Local\PastasDesktopResidente", out first);
                if (!first) return;
                IsResident = true;
                Log.Write("Residente iniciado.");
                new Resident().Start();
                app.Run();
                GC.KeepAlive(mutex);
                return;
            }
            if (cmd == "--nova")
            {
                string n = InputDialog.Ask(null, "Nova pasta de apps", "Nova pasta", "Criar");
                if (n != null) Theme.Message(null, Store.CreateFolder(n));
                StartResident();
                return;
            }
            if (cmd == "--instalar") { Store.InstallMenu(); return; }
            if (cmd == "--desinstalar") { Store.UninstallMenu(); return; }

            // Duplo clique, Enter, ou arquivos soltos em cima do ícone da pasta.
            List<string> rest = args.SkipWhile(a => a == "--abrir").ToList();
            if (rest.Count == 0) return;
            string name = Store.CleanName(rest[0]);
            if (!Store.Exists(name)) return;
            if (rest.Count > 1)
            {
                foreach (string f in rest.Skip(1)) { try { Store.Add(name, f); } catch (Exception ex) { Log.Write("Adicionando " + f + ": " + ex.Message); } }
                Store.UpdateDesktop(name);
                return;
            }
            app.ShutdownMode = ShutdownMode.OnLastWindowClose;
            app.Run(new FolderWindow(name, System.Windows.Forms.Cursor.Position));
        }
    }
}
