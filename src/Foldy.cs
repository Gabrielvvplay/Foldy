// Pastas de apps na área de trabalho, estilo iPhone/Android.
// Compilar com build.ps1 (usa o csc do .NET Framework que já vem no Windows).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
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

[assembly: AssemblyTitle("Foldy")]
[assembly: AssemblyVersion("1.1.0.0")]

namespace Foldy
{
    static class Store
    {
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Foldy");
        public static readonly string FoldersDir = Path.Combine(Root, "Pastas");
        public static readonly string IconsDir = Path.Combine(Root, "Icones");
        public static readonly string Exe = Assembly.GetExecutingAssembly().Location;
        public static readonly string InstalledExe = Path.Combine(Root, "Foldy.exe");

        public static string Desktop { get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } }
        public static string PublicDesktop { get { return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory); } }
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
            IEnumerable<FileInfo> files = new DirectoryInfo(dir).GetFiles()
                .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0);
            files = FolderStyle.Load(name).SortByName
                ? files.OrderBy(f => Path.GetFileNameWithoutExtension(f.Name), StringComparer.CurrentCultureIgnoreCase)
                : files.OrderBy(f => f.CreationTimeUtc).ThenBy(f => f.Name);
            return files.Select(f => f.FullName).ToList();
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
            // Pasta nova já nasce com a transparência geral escolhida no painel.
            int icon = Config.IconTransparency, card = Config.CardTransparency;
            if (icon > 0 || card > 0) new FolderStyle { IconTransparency = icon, CardTransparency = card }.Save(name);
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
                // Da área de trabalho (a sua ou a pública) ou de outra pasta de apps. Mover da pública
                // só funciona se a conta tem permissão de apagar lá; senão cai na cópia.
                if (SameDir(srcDir, Desktop) || SameDir(srcDir, PublicDesktop) || SameDir(Path.GetDirectoryName(srcDir), FoldersDir))
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
            FolderStyle style = FolderStyle.Load(name);
            List<BitmapSource> icons = Items(name).Take(style.Preview * style.Preview).Select(p => IconUtil.Get(p)).Where(b => b != null).ToList();
            string ico = Path.Combine(IconsDir, name + "_" + DateTime.UtcNow.Ticks + ".ico");
            IconUtil.WriteIco(ico, new[] { 256, 64, 48, 32, 16 }.Select(s => IconUtil.RenderFolder(icons, s, style)).ToArray());
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

        const string MenuKey = @"Software\Classes\DesktopBackground\Shell\Foldy";

        public static void InstallMenu()
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(MenuKey))
            {
                k.SetValue("", "Nova pasta de apps");
                k.SetValue("Icon", InstalledExe + ",0");
                using (RegistryKey c = k.CreateSubKey("command"))
                    c.SetValue("", "\"" + InstalledExe + "\" --nova");
            }
        }

        public static void UninstallMenu()
        {
            Registry.CurrentUser.DeleteSubKeyTree(MenuKey, false);
        }

        public static bool MenuInstalled
        {
            get { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(MenuKey)) return k != null; }
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
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
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

        public static BitmapSource RenderFolder(List<BitmapSource> icons, int size, FolderStyle style)
        {
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (DrawingContext dc = dv.RenderOpen())
            {
                double k = size / 256.0;
                dc.PushTransform(new ScaleTransform(k, k));
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), size <= 32 ? 10 : 5);
                bool circle = style.Shape == "circulo";
                if (circle) dc.DrawEllipse(style.IconBackground, pen, new Point(128, 128), 118, 118);
                else
                {
                    double radius = style.Shape == "quadrado" ? 18 : 58;
                    dc.DrawRoundedRectangle(style.IconBackground, pen, new Rect(10, 10, 236, 236), radius, radius);
                }
                int n = style.Preview;
                double pad = (n == 3 ? 34 : 40) + (circle ? 18 : 0), gap = n == 3 ? 10 : 16; // no círculo os cantos da grade ficariam pra fora
                double cell = (256 - 2 * pad - (n - 1) * gap) / n;
                for (int i = 0; i < icons.Count && i < n * n; i++)
                    dc.DrawImage(icons[i], new Rect(pad + (i % n) * (cell + gap), pad + (i / n) * (cell + gap), cell, cell));
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
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(10, 132, 255));

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

        // Interruptor liga/desliga. "changed" recebe o estado pedido e devolve o que ficou valendo.
        public static Border MakeToggle(bool on, Func<bool, bool> changed)
        {
            var knob = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Brushes.White, Margin = new Thickness(3) };
            var track = new Border { Width = 44, Height = 24, CornerRadius = new CornerRadius(12), Child = knob, Cursor = Cursors.Hand };
            Action paint = () =>
            {
                track.Background = on ? Accent : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
                knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            };
            paint();
            track.MouseLeftButtonDown += (s, e) => e.Handled = true;
            track.MouseLeftButtonUp += delegate { on = changed(!on); paint(); };
            return track;
        }

        // Controle deslizante no tema escuro (o Slider padrão do WPF é claro). "refresh" redesenha
        // a partir de get(), pra quando o valor muda por fora (ex.: "Restaurar padrão").
        public static FrameworkElement MakeSlider(Func<double> get, Action<double> set, double max, out Action refresh, Action released = null)
        {
            const double knobSize = 18;
            var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)) };
            var fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Accent, HorizontalAlignment = HorizontalAlignment.Left };
            var knob = new Border
            {
                Width = knobSize, Height = knobSize, CornerRadius = new CornerRadius(knobSize / 2), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left,
                Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.4, Color = Colors.Black },
            };
            var grid = new Grid { Height = 26, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            grid.Children.Add(track);
            grid.Children.Add(fill);
            grid.Children.Add(knob);
            Action layout = () =>
            {
                double w = grid.ActualWidth - knobSize;
                if (w <= 0) return;
                double x = w * get() / max;
                fill.Width = x + knobSize / 2;
                knob.Margin = new Thickness(x, 0, 0, 0);
            };
            MouseEventHandler drag = (s, e) =>
            {
                if (!grid.IsMouseCaptured) return;
                double w = grid.ActualWidth - knobSize;
                double x = e.GetPosition(grid).X - knobSize / 2;
                set(Math.Round(Math.Max(0, Math.Min(max, x / w * max))));
                layout();
            };
            grid.SizeChanged += delegate { layout(); };
            grid.MouseLeftButtonDown += (s, e) => { e.Handled = true; grid.CaptureMouse(); drag(s, e); };
            grid.MouseMove += drag;
            grid.MouseLeftButtonUp += delegate
            {
                if (!grid.IsMouseCaptured) return;
                grid.ReleaseMouseCapture();
                if (released != null) released();
            };
            refresh = layout;
            return grid;
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
            if (owner != null) MessageBox.Show(owner, text, "Foldy", MessageBoxButton.OK, MessageBoxImage.Information);
            else MessageBox.Show(text, "Foldy", MessageBoxButton.OK, MessageBoxImage.Information);
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

    // Aparência de uma pasta. Fica num arquivo oculto dentro dela, então acompanha renomeações.
    class FolderStyle
    {
        public static readonly Color DefaultTint = Color.FromRgb(70, 70, 82);
        public static readonly KeyValuePair<string, Color>[] Palette =
        {
            new KeyValuePair<string, Color>("Grafite", DefaultTint),
            new KeyValuePair<string, Color>("Azul", Color.FromRgb(10, 132, 255)),
            new KeyValuePair<string, Color>("Roxo", Color.FromRgb(140, 82, 255)),
            new KeyValuePair<string, Color>("Rosa", Color.FromRgb(255, 55, 135)),
            new KeyValuePair<string, Color>("Vermelho", Color.FromRgb(255, 69, 58)),
            new KeyValuePair<string, Color>("Laranja", Color.FromRgb(255, 149, 0)),
            new KeyValuePair<string, Color>("Amarelo", Color.FromRgb(255, 204, 0)),
            new KeyValuePair<string, Color>("Verde", Color.FromRgb(52, 199, 89)),
            new KeyValuePair<string, Color>("Ciano", Color.FromRgb(50, 173, 230)),
        };

        public Color Tint = DefaultTint;
        public string Shape = "arredondado"; // arredondado, circulo, quadrado
        public int Preview = 2;              // apps por lado na miniatura do ícone (2x2 ou 3x3)
        public int Columns;                  // colunas no popup; 0 = automático
        public int IconSize = 52;
        public bool SortByName;
        public const int MaxTransparency = 90; // em 100% o fundo some e os cliques passariam direto pra área de trabalho
        public int IconTransparency;           // % de transparência do fundo do ícone na área de trabalho
        public int CardTransparency;           // % de transparência do fundo do popup

        static string FileOf(string name) { return Path.Combine(Store.FolderPath(name), ".estilo"); }

        public static FolderStyle Load(string name)
        {
            var s = new FolderStyle();
            string f = FileOf(name);
            if (!File.Exists(f)) return s;
            try
            {
                foreach (string line in File.ReadAllLines(f))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string key = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    int n;
                    int.TryParse(v, out n);
                    switch (key)
                    {
                        case "cor": try { s.Tint = (Color)ColorConverter.ConvertFromString(v); } catch { } break;
                        case "formato": if (v == "circulo" || v == "quadrado") s.Shape = v; break;
                        case "previa": if (n == 3) s.Preview = 3; break;
                        case "colunas": if (n >= 0 && n <= 6) s.Columns = n; break;
                        case "icones": if (n >= 32 && n <= 96) s.IconSize = n; break;
                        case "ordem": s.SortByName = v == "nome"; break;
                        case "transparencia": s.IconTransparency = s.CardTransparency = Math.Max(0, Math.Min(MaxTransparency, n)); break; // versão com um controle só
                        case "transparencia_icone": s.IconTransparency = Math.Max(0, Math.Min(MaxTransparency, n)); break;
                        case "transparencia_pasta": s.CardTransparency = Math.Max(0, Math.Min(MaxTransparency, n)); break;
                    }
                }
            }
            catch (Exception ex) { Log.Write("Lendo estilo de " + name + ": " + ex.Message); }
            return s;
        }

        public void Save(string name)
        {
            string f = FileOf(name);
            if (File.Exists(f)) File.SetAttributes(f, FileAttributes.Normal); // arquivo oculto não pode ser sobrescrito
            File.WriteAllLines(f, new[]
            {
                string.Format("cor=#{0:X2}{1:X2}{2:X2}", Tint.R, Tint.G, Tint.B),
                "formato=" + Shape,
                "previa=" + Preview,
                "colunas=" + Columns,
                "icones=" + IconSize,
                "ordem=" + (SortByName ? "nome" : "entrada"),
                "transparencia_icone=" + IconTransparency,
                "transparencia_pasta=" + CardTransparency,
            });
            File.SetAttributes(f, FileAttributes.Hidden);
        }

        public FolderStyle Clone() { return (FolderStyle)MemberwiseClone(); }

        static Color Mix(Color a, Color b, double t)
        {
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        static byte Alpha(int opaque, int transparency) { return (byte)(opaque * (100 - transparency) / 100); }

        public Brush IconBackground
        {
            get
            {
                Color dark = Mix(Colors.Black, Tint, 0.5);
                byte a = Alpha(225, IconTransparency);
                return new LinearGradientBrush(Color.FromArgb(a, Tint.R, Tint.G, Tint.B), Color.FromArgb(a, dark.R, dark.G, dark.B), 90);
            }
        }

        // Fundo do popup: a cor da pasta bem escurecida, pra o texto branco continuar legível.
        public Brush CardBackground
        {
            get
            {
                if (Tint == DefaultTint && CardTransparency == 0) return Theme.Card;
                Color m = Tint == DefaultTint ? Color.FromRgb(30, 30, 36) : Mix(Color.FromRgb(30, 30, 36), Tint, 0.28);
                return new SolidColorBrush(Color.FromArgb(Alpha(238, CardTransparency), m.R, m.G, m.B));
            }
        }
    }

    class OwnerHandle : System.Windows.Forms.IWin32Window
    {
        public OwnerHandle(Window w) { Handle = new WindowInteropHelper(w).Handle; }
        public IntPtr Handle { get; private set; }
    }

    // Janela "Personalizar pasta", com prévia ao vivo do ícone.
    class CustomizeDialog : Window
    {
        static readonly Brush Selected = new SolidColorBrush(Color.FromRgb(10, 132, 255));

        public FolderStyle Result { get; private set; }
        public string NewName { get { return nameBox.Text; } }

        readonly TextBox nameBox;
        readonly Image preview;
        readonly List<Action> refreshers = new List<Action>();
        readonly List<string> items;
        Action<FolderStyle> livePreview; // aplica o estilo no popup aberto enquanto mexe, pra ver contra o papel de parede

        public CustomizeDialog(string folder, FolderStyle current)
        {
            Result = current.Clone();
            items = Store.Items(folder);
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight; Topmost = true; ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; FontFamily = Theme.Font; Title = "Personalizar pasta";

            var stack = new StackPanel { Width = 400 };
            stack.Children.Add(new TextBlock { Text = "Personalizar pasta", Foreground = Theme.Text, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 14) });

            preview = new Image { Width = 96, Height = 96, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top };
            RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.HighQuality);
            nameBox = new TextBox
            {
                Text = folder, FontSize = 15, Padding = new Thickness(8, 6, 8, 6),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), Foreground = Theme.Text,
                CaretBrush = Theme.Text, BorderBrush = Theme.Stroke, SelectionBrush = Selected,
            };
            var nameCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            nameCol.Children.Add(Label("Nome"));
            nameCol.Children.Add(nameBox);
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(preview, Dock.Left);
            top.Children.Add(preview);
            top.Children.Add(nameCol);
            stack.Children.Add(top);

            stack.Children.Add(Label("Cor"));
            stack.Children.Add(Swatches());
            stack.Children.Add(Label("Transparência do fundo"));
            stack.Children.Add(TransparencySlider("Ícone", () => Result.IconTransparency, v => Result.IconTransparency = v));
            stack.Children.Add(TransparencySlider("Pasta aberta", () => Result.CardTransparency, v => Result.CardTransparency = v));
            stack.Children.Add(Choice("Formato do ícone", new[] { "Arredondado", "Círculo", "Quadrado" }, new[] { "arredondado", "circulo", "quadrado" },
                () => Result.Shape, v => Result.Shape = v));
            stack.Children.Add(Choice("Apps no ícone", new[] { "4 (2×2)", "9 (3×3)" }, new[] { 2, 3 },
                () => Result.Preview, v => Result.Preview = v));
            stack.Children.Add(Choice("Colunas na pasta", new[] { "Automático", "3", "4", "5" }, new[] { 0, 3, 4, 5 },
                () => Result.Columns, v => Result.Columns = v));
            stack.Children.Add(Choice("Tamanho dos apps", new[] { "Pequeno", "Médio", "Grande" }, new[] { 40, 52, 64 },
                () => Result.IconSize, v => Result.IconSize = v));
            stack.Children.Add(Choice("Ordem dos apps", new[] { "Ordem que entraram", "Nome (A–Z)" }, new[] { false, true },
                () => Result.SortByName, v => Result.SortByName = v));

            var reset = Theme.MakeButton("Restaurar padrão", false);
            reset.Margin = new Thickness(0);
            reset.Click += delegate { Result = new FolderStyle(); Refresh(); };
            var cancel = Theme.MakeButton("Cancelar", false);
            var save = Theme.MakeButton("Salvar", true);
            cancel.Click += delegate { Close(); };
            save.Click += delegate { DialogResult = true; };
            var buttons = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
            DockPanel.SetDock(reset, Dock.Left);
            DockPanel.SetDock(save, Dock.Right);
            DockPanel.SetDock(cancel, Dock.Right);
            buttons.Children.Add(reset);
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            stack.Children.Add(buttons);

            var card = Theme.MakeCard(stack);
            card.Padding = new Thickness(20, 16, 20, 18);
            card.MouseLeftButtonDown += delegate { try { DragMove(); } catch { } };
            Content = card;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) DialogResult = true;
                else if (e.Key == Key.Escape) Close();
            };
            Loaded += delegate { Native.ForceForeground(new WindowInteropHelper(this).Handle); Activate(); };
            Refresh();
        }

        // Abre a janela e aplica o que foi salvo (nome, estilo e ícone da área de trabalho).
        // Retorna o nome final da pasta, ou null se nada mudou. Com livePreview, a janela abre
        // ao lado do popup (e não em cima dele) e o popup acompanha as mudanças na hora.
        public static string Edit(Window owner, string name, Action<string> message, Action<FolderStyle> livePreview = null)
        {
            FolderStyle original = FolderStyle.Load(name);
            var d = new CustomizeDialog(name, original) { Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            if (livePreview != null)
            {
                d.livePreview = livePreview;
                d.WindowStartupLocation = WindowStartupLocation.Manual;
                d.Loaded += delegate { d.PlaceBeside(owner); };
            }
            if (d.ShowDialog() != true)
            {
                if (livePreview != null) livePreview(original);
                return null;
            }
            string n = Store.CleanName(d.NewName);
            if (n != "" && n != name)
            {
                string err = Store.Rename(name, n);
                if (err != null) { message(err); return null; }
                name = n;
            }
            try { d.Result.Save(name); }
            catch (Exception ex) { message("Não consegui salvar a personalização: " + ex.Message); return name; }
            try { Store.UpdateDesktop(name); } catch (Exception ex) { message("Não consegui atualizar o ícone: " + ex.Message); }
            return name;
        }

        void Refresh()
        {
            foreach (Action a in refreshers) a();
            List<string> order = Result.SortByName
                ? items.OrderBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.CurrentCultureIgnoreCase).ToList()
                : items.OrderBy(p => File.GetCreationTimeUtc(p)).ToList();
            List<BitmapSource> icons = order.Take(Result.Preview * Result.Preview).Select(p => IconUtil.Get(p)).Where(b => b != null).ToList();
            preview.Source = IconUtil.RenderFolder(icons, 192, Result);
            if (livePreview != null) livePreview(Result);
        }

        // À direita do popup; se não couber, à esquerda. Sempre dentro da tela.
        void PlaceBeside(Window owner)
        {
            Rect wa = SystemParameters.WorkArea;
            double right = owner.Left + owner.ActualWidth - 20, left = owner.Left + 20 - ActualWidth;
            Left = right + ActualWidth <= wa.Right ? right : Math.Max(wa.Left, left);
            Top = Math.Max(wa.Top, Math.Min(owner.Top, wa.Bottom - ActualHeight));
        }

        UIElement TransparencySlider(string label, Func<int> get, Action<int> set)
        {
            var name = new TextBlock { Text = label, Foreground = Theme.Text, FontSize = 12.5, Width = 92, VerticalAlignment = VerticalAlignment.Center };
            var value = new TextBlock { Foreground = Theme.Muted, FontSize = 12, Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Action redraw;
            FrameworkElement slider = Theme.MakeSlider(() => get(), v => { set((int)v); Refresh(); }, FolderStyle.MaxTransparency, out redraw);
            refreshers.Add(redraw);
            refreshers.Add(() => value.Text = get() + "%");
            var row = new DockPanel { Margin = new Thickness(2, 0, 0, 2) };
            DockPanel.SetDock(name, Dock.Left);
            DockPanel.SetDock(value, Dock.Right);
            row.Children.Add(name);
            row.Children.Add(value);
            row.Children.Add(slider);
            return row;
        }

        static TextBlock Label(string text)
        {
            return new TextBlock { Text = text, Foreground = Theme.Muted, FontSize = 12, Margin = new Thickness(2, 10, 0, 6) };
        }

        // Bolinha de cor; o anel branco marca a escolhida.
        Border Dot(Brush fill, UIElement content, string tooltip, Func<bool> isSelected, Action pick)
        {
            var inner = new Border { CornerRadius = new CornerRadius(13), Background = fill, Child = content };
            var ring = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(2),
                Padding = new Thickness(2), Margin = new Thickness(0, 0, 6, 6), Child = inner, Cursor = Cursors.Hand, ToolTip = tooltip,
            };
            ring.MouseLeftButtonDown += (s, e) => e.Handled = true; // senão o DragMove do cartão engole o clique
            ring.MouseLeftButtonUp += delegate { pick(); Refresh(); };
            refreshers.Add(() => ring.BorderBrush = isSelected() ? Theme.Text : Brushes.Transparent);
            return ring;
        }

        UIElement Swatches()
        {
            var panel = new WrapPanel();
            foreach (var p in FolderStyle.Palette)
            {
                Color c = p.Value;
                panel.Children.Add(Dot(new SolidColorBrush(c), null, p.Key, () => Result.Tint == c, () => Result.Tint = c));
            }
            // Qualquer outra cor, pelo seletor do Windows.
            var plus = new TextBlock { Text = "+", Foreground = Theme.Text, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) };
            var rainbow = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(255, 69, 58), 0), new GradientStop(Color.FromRgb(255, 204, 0), 0.33),
                new GradientStop(Color.FromRgb(52, 199, 89), 0.66), new GradientStop(Color.FromRgb(10, 132, 255), 1),
            }, 45);
            Border custom = Dot(rainbow, plus, "Outra cor...", () => !FolderStyle.Palette.Any(p => p.Value == Result.Tint), PickColor);
            refreshers.Add(() =>
            {
                bool own = !FolderStyle.Palette.Any(p => p.Value == Result.Tint);
                ((Border)custom.Child).Background = own ? new SolidColorBrush(Result.Tint) : (Brush)rainbow;
            });
            panel.Children.Add(custom);
            return panel;
        }

        void PickColor()
        {
            var dlg = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(Result.Tint.R, Result.Tint.G, Result.Tint.B),
            };
            if (dlg.ShowDialog(new OwnerHandle(this)) == System.Windows.Forms.DialogResult.OK)
                Result.Tint = Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
            Activate();
        }

        UIElement Choice<T>(string label, string[] texts, T[] values, Func<T> get, Action<T> set)
        {
            var sp = new StackPanel();
            sp.Children.Add(Label(label));
            var row = new WrapPanel();
            for (int i = 0; i < texts.Length; i++)
            {
                T v = values[i];
                var b = new Border
                {
                    CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 6, 12, 7), Margin = new Thickness(0, 0, 6, 6),
                    Cursor = Cursors.Hand, Child = new TextBlock { Text = texts[i], Foreground = Theme.Text, FontSize = 12.5 },
                };
                b.MouseLeftButtonDown += (s, e) => e.Handled = true; // senão o DragMove do cartão engole o clique
                b.MouseLeftButtonUp += delegate { set(v); Refresh(); };
                refreshers.Add(() => b.Background = EqualityComparer<T>.Default.Equals(get(), v) ? Selected : Theme.Hover);
                row.Children.Add(b);
            }
            sp.Children.Add(row);
            return sp;
        }
    }

    // O popup com os apps da pasta.
    class FolderWindow : Window
    {
        public string FolderName { get { return name; } }
        string name;
        FolderStyle style;
        double TileW { get { return style.IconSize + 44; } }
        readonly System.Drawing.Point anchor;
        readonly TextBlock title, hint;
        readonly TextBox titleBox;
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
            style = FolderStyle.Load(name);
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual; Left = -20000; Top = -20000;
            FontFamily = Theme.Font; Title = name;

            title = new TextBlock
            {
                Text = name, Foreground = Theme.Text, FontSize = 19, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(30, 0, 30, 0),
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360,
            };
            // Engrenagem no canto: abre o "Personalizar pasta".
            var gearText = new TextBlock
            {
                Text = "", FontFamily = Theme.Icons, FontSize = 14,
                Foreground = Theme.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var gear = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Brushes.Transparent, Child = gearText,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand, ToolTip = "Personalizar pasta",
            };
            gear.MouseEnter += delegate { gear.Background = Theme.Hover; gearText.Foreground = Theme.Text; };
            gear.MouseLeave += delegate { gear.Background = Brushes.Transparent; gearText.Foreground = Theme.Muted; };
            gear.MouseLeftButtonUp += delegate { Customize(); };
            // Clicar no nome troca ele por uma caixa de texto pra renomear ali mesmo.
            // Mesma fonte do nome, sem moldura: só um traço embaixo mostra que está editando.
            var underline = new FrameworkElementFactory(typeof(Border));
            underline.SetValue(Border.BorderBrushProperty, Theme.Muted);
            underline.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1.5));
            var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
            underline.AppendChild(host);
            titleBox = new TextBox
            {
                FontSize = 19, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(30, 0, 30, 0), Padding = new Thickness(0),
                Background = Brushes.Transparent, Foreground = Theme.Text, CaretBrush = Theme.Text,
                SelectionBrush = new SolidColorBrush(Color.FromRgb(10, 132, 255)), Visibility = Visibility.Collapsed,
                Template = new ControlTemplate(typeof(TextBox)) { VisualTree = underline },
            };
            title.Cursor = Cursors.IBeam;
            title.ToolTip = "Clique pra renomear";
            title.MouseLeftButtonUp += delegate { StartEditingName(); };
            titleBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; FinishEditingName(true); }
                else if (e.Key == Key.Escape) { e.Handled = true; FinishEditingName(false); }
            };
            titleBox.LostKeyboardFocus += delegate { FinishEditingName(true); };
            var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            header.Children.Add(title);
            header.Children.Add(titleBox);
            header.Children.Add(gear);
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
            stack.Children.Add(header);
            stack.Children.Add(hint);
            stack.Children.Add(scroller);

            card = Theme.MakeCard(stack);
            card.Background = style.CardBackground;
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
            // A margem em volta do cartão (onde fica a sombra) ainda é parte da janela; clicar nela
            // não tira o foco, então conta como clicar fora.
            PreviewMouseDown += (s, e) =>
            {
                // Editando o nome: clicar em qualquer outro lugar confirma (o fundo da pasta não pega o foco sozinho).
                if (titleBox.Visibility == Visibility.Visible && !titleBox.IsMouseOver) FinishEditingName(true);
                Point p = e.GetPosition(card);
                if (p.X >= 0 && p.Y >= 0 && p.X <= card.ActualWidth && p.Y <= card.ActualHeight) return;
                e.Handled = true;
                CloseAnimated();
            };
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
            int columns = style.Columns > 0 ? style.Columns : (items.Count <= 3 ? 3 : 4);
            grid.Width = columns * TileW;
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
                Width = TileW - 8, Height = style.IconSize + 54, Margin = new Thickness(4), CornerRadius = new CornerRadius(14),
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
            var img = new Image { Source = IconUtil.Get(path), Width = style.IconSize, Height = style.IconSize };
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
                Theme.MenuItem("Personalizar pasta...", Customize),
                Theme.MenuItem("Renomear pasta...", RenameFolder),
                Theme.MenuItem("Abrir no Explorador", () =>
                {
                    Process.Start("explorer.exe", "\"" + Store.FolderPath(name) + "\"");
                    CloseAnimated();
                }),
                Theme.MenuItem("Excluir pasta", DeleteFolder),
                Theme.MenuItem("Painel de controle...", () =>
                {
                    try { Process.Start(Store.Exe, "--painel"); } catch (Exception ex) { Message(ex.Message); }
                    CloseAnimated();
                }),
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
            suppress = false;
            Activate();
            ApplyName(n);
        }

        void StartEditingName()
        {
            if (titleBox.Visibility == Visibility.Visible) return;
            titleBox.Text = name;
            titleBox.MinWidth = Math.Max(60, title.ActualWidth + 8);
            title.Visibility = Visibility.Hidden; // Hidden (não Collapsed) pra o popup não mudar de tamanho
            titleBox.Visibility = Visibility.Visible;
            titleBox.Focus();
            titleBox.SelectAll();
        }

        void FinishEditingName(bool save)
        {
            if (titleBox.Visibility != Visibility.Visible) return;
            titleBox.Visibility = Visibility.Collapsed;
            title.Visibility = Visibility.Visible;
            if (!save) return;
            string n = Store.CleanName(titleBox.Text);
            if (n != "" && n != name) ApplyName(n);
        }

        bool ApplyName(string n)
        {
            string err = Store.Rename(name, n);
            if (err != null) { Message(err); return false; }
            name = n;
            title.Text = n;
            Title = n;
            return true;
        }

        void Customize()
        {
            suppress = true;
            string n = CustomizeDialog.Edit(this, name, text => Theme.Message(this, text), s => card.Background = s.CardBackground);
            suppress = false;
            Activate();
            if (n == null) return;
            name = n;
            title.Text = n;
            Title = n;
            style = FolderStyle.Load(name);
            card.Background = style.CardBackground;
            Build();
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
            bool yes = MessageBox.Show(this, text, "Foldy", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
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
        public const string WindowTitle = "FoldyResidente";
        const int WM_COPYDATA = 0x4A;

        readonly Dictionary<string, string> signatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly DispatcherTimer debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        readonly DispatcherTimer updates = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) }; // a primeira procura espera o PC terminar de ligar
        Updater.Release pending;
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

            Updater.CleanupOld();
            updates.Tick += delegate { updates.Interval = TimeSpan.FromHours(6); CheckForUpdate(); };
            updates.Start();

            Sync();
        }

        void CheckForUpdate()
        {
            if (!Config.AutoUpdate || !Installer.IsInstalledCopy) return;
            if (pending != null) { TryApplyUpdate(); return; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Updater.Release r = Updater.Check();
                    if (r == null) return;
                    Updater.Download(r);
                    Log.Write("Versão " + r.Version.ToString(2) + " baixada.");
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => { pending = r; TryApplyUpdate(); }));
                }
                catch (Exception ex) { Log.Write("Procurando atualização: " + ex.Message); }
            });
        }

        // Só troca de versão com nenhuma pasta nem o painel abertos, pra nada sumir da frente da pessoa.
        void TryApplyUpdate()
        {
            if ((popup != null && popup.IsVisible) || Native.FindWindow(null, ControlPanel.WindowTitle) != IntPtr.Zero)
            {
                updates.Interval = TimeSpan.FromMinutes(1);
                return;
            }
            try { Updater.Apply(null); }
            catch (Exception ex) { Log.Write("Aplicando atualização: " + ex); pending = null; }
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
                    string sig = string.Join("|", Store.Items(n).Take(9));
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
            if (!Config.OneClick) return; // desligado no painel: só o duplo clique abre
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

    // Opções gerais do programa (as de cada pasta ficam no FolderStyle).
    static class Config
    {
        public static readonly string FilePath = Path.Combine(Store.Root, "config.txt");

        public static bool OneClick
        {
            get { return Get("umclique") != "0"; }
            set { Set("umclique", value ? "1" : "0"); }
        }

        public static bool AutoUpdate
        {
            get { return Get("atualizar") != "0"; }
            set { Set("atualizar", value ? "1" : "0"); }
        }

        // Última transparência aplicada a todas as pastas pelo painel (e a que as pastas novas recebem).
        public static int IconTransparency
        {
            get { return GetInt("transparencia_icone"); }
            set { Set("transparencia_icone", value.ToString()); }
        }

        public static int CardTransparency
        {
            get { return GetInt("transparencia_pasta"); }
            set { Set("transparencia_pasta", value.ToString()); }
        }

        static int GetInt(string key)
        {
            int n;
            return int.TryParse(Get(key), out n) ? Math.Max(0, Math.Min(FolderStyle.MaxTransparency, n)) : 0;
        }

        static Dictionary<string, string> Load()
        {
            var d = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(FilePath)) return d;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            }
            catch (Exception ex) { Log.Write("Lendo config: " + ex.Message); }
            return d;
        }

        static string Get(string key)
        {
            string v;
            return Load().TryGetValue(key, out v) ? v : null;
        }

        static void Set(string key, string value)
        {
            Dictionary<string, string> d = Load();
            d[key] = value;
            File.WriteAllLines(FilePath, d.Select(p => p.Key + "=" + p.Value));
        }
    }

    // Instalar, iniciar com o Windows e desinstalar, tudo pelo próprio .exe: quem baixa o
    // programa não precisa rodar script nenhum, nem ter permissão de administrador.
    static class Installer
    {
        const string TaskName = "Foldy";
        static readonly string[][] StartMenu = { new[] { "Foldy", "--painel" }, new[] { "Nova pasta de apps", "--nova" } };
        static string Programs { get { return Environment.GetFolderPath(Environment.SpecialFolder.Programs); } }

        public static bool IsInstalledCopy { get { return string.Equals(Store.Exe, Store.InstalledExe, StringComparison.OrdinalIgnoreCase); } }
        public static bool HasInstall { get { return File.Exists(Store.InstalledExe); } }

        // Copia este .exe (ex.: o que foi baixado) pra pasta de instalação, fechando a versão antiga se estiver rodando.
        public static void CopyToInstallDir()
        {
            if (IsInstalledCopy) return;
            Directory.CreateDirectory(Store.Root);
            StopOthers();
            File.Copy(Store.Exe, Store.InstalledExe, true);
        }

        // Roda na cópia instalada: menu do botão direito, Menu Iniciar, início com o Windows e o residente.
        public static void Setup()
        {
            try { MigrateFromPastasDesktop(); } catch (Exception ex) { Log.Write("Migrando do Pastas Desktop: " + ex); }
            Store.InstallMenu();
            foreach (string[] s in StartMenu)
                Shell.CreateLnk(Path.Combine(Programs, s[0] + ".lnk"), Store.InstalledExe, s[1], Store.InstalledExe + ",0", Store.Root, null);
            SetAutostart(true);
            Program.StartResident();
        }

        // Antes de virar Foldy o programa se chamava "Pastas Desktop" e ficava em %LOCALAPPDATA%\PastasDesktop.
        // Traz as pastas e o resto de lá, refaz os ícones (os antigos apontam pro .exe velho) e tira
        // a tarefa, o menu e o atalho antigos.
        static void MigrateFromPastasDesktop()
        {
            string old = Path.Combine(Path.GetDirectoryName(Store.Root), "PastasDesktop");
            if (!Directory.Exists(old)) return;
            foreach (Process p in Process.GetProcessesByName("PastasDesktop")) { try { p.Kill(); p.WaitForExit(3000); } catch { } }

            foreach (string sub in new[] { "Icones", "PastasDesktop.exe", "log.txt" })
            {
                string path = Path.Combine(old, sub);
                try { if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path); } catch { }
            }
            string oldFolders = Path.Combine(old, "Pastas");
            if (Directory.Exists(oldFolders))
            {
                foreach (string dir in Directory.GetDirectories(oldFolders))
                {
                    string dest = Store.FolderPath(Path.GetFileName(dir));
                    if (!Directory.Exists(dest)) Directory.Move(dir, dest);
                }
                try { Directory.Delete(oldFolders); } catch { }
            }
            // O que sobrar (config, backups) vem junto.
            foreach (string entry in Directory.GetFileSystemEntries(old))
            {
                string dest = Path.Combine(Store.Root, Path.GetFileName(entry));
                if (File.Exists(dest) || Directory.Exists(dest)) continue;
                if (Directory.Exists(entry)) Directory.Move(entry, dest); else File.Move(entry, dest);
            }
            try { Directory.Delete(old); } catch { } // só sai se ficou vazia

            foreach (string n in Store.Names()) { try { Store.UpdateDesktop(n); } catch (Exception ex) { Log.Write("Refazendo ícone de " + n + ": " + ex.Message); } }
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\DesktopBackground\Shell\PastasDesktop", false);
            RunPowerShell("Unregister-ScheduledTask -TaskName PastasDesktop -Confirm:$false -ErrorAction SilentlyContinue");
            try { File.Delete(Path.Combine(Programs, "Pastas.lnk")); } catch { }
            Log.Write("Pastas trazidas da instalação antiga (Pastas Desktop).");
        }

        // Tira tudo do PC. Os apps das pastas voltam pra área de trabalho, então nada se perde.
        public static void Uninstall()
        {
            StopOthers();
            foreach (string n in Store.Names()) Store.DeleteFolder(n);
            Store.UninstallMenu();
            SetAutostart(false);
            foreach (string[] s in StartMenu) { try { File.Delete(Path.Combine(Programs, s[0] + ".lnk")); } catch { } }
            foreach (string d in new[] { Store.FoldersDir, Store.IconsDir }) { try { Directory.Delete(d, true); } catch { } }
            foreach (string f in new[] { Config.FilePath, Path.Combine(Store.Root, "log.txt") }) { try { File.Delete(f); } catch { } }
            // O .exe em uso só pode ser apagado depois que o programa fecha. A pasta só sai se ficou
            // vazia, pra não levar junto nada que não seja nosso.
            Process.Start(new ProcessStartInfo("cmd.exe",
                "/c ping -n 3 127.0.0.1 >nul & del /f /q \"" + Store.InstalledExe + "\" & rmdir \"" + Store.Root + "\"")
                { CreateNoWindow = true, UseShellExecute = false });
        }

        public static void StopOthers()
        {
            int me = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcessesByName("Foldy"))
            {
                if (p.Id == me) continue;
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }

        public static bool ResidentRunning
        {
            get
            {
                Mutex m;
                if (!Mutex.TryOpenExisting(@"Local\FoldyResidente", out m)) return false;
                m.Dispose();
                return true;
            }
        }

        public static bool Autostart { get { return Run("schtasks.exe", "/Query /TN " + TaskName) == 0; } }

        // Tarefa agendada: inicia no logon e, a cada 5 minutos, reabre o residente se ele tiver fechado.
        public static void SetAutostart(bool on)
        {
            string exe = Store.InstalledExe.Replace("'", "''");
            string ps = on
                ? "$a = New-ScheduledTaskAction -Execute '" + exe + "' -Argument '--residente';" +
                  "$l = New-ScheduledTaskTrigger -AtLogOn -User \"$env:USERDOMAIN\\$env:USERNAME\";" +
                  "$r = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 5);" +
                  "$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew;" +
                  "Register-ScheduledTask -TaskName " + TaskName + " -Action $a -Trigger $l, $r -Settings $s -Force | Out-Null"
                : "Unregister-ScheduledTask -TaskName " + TaskName + " -Confirm:$false -ErrorAction SilentlyContinue";
            int code = RunPowerShell(ps);
            if (code != 0) Log.Write("Tarefa agendada (" + (on ? "criar" : "remover") + ") terminou com código " + code + ".");
        }

        static int RunPowerShell(string script)
        {
            return Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " +
                Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        }

        static int Run(string file, string args)
        {
            var psi = new ProcessStartInfo(file, args) { CreateNoWindow = true, UseShellExecute = false };
            using (Process p = Process.Start(psi))
            {
                if (!p.WaitForExit(30000)) return -1;
                return p.ExitCode;
            }
        }
    }

    // Procura versão nova nos Releases do GitHub e troca o .exe instalado por ela.
    static class Updater
    {
        const string LatestRelease = "https://api.github.com/repos/Gabrielvvplay/Foldy/releases/latest";
        static string OldExe { get { return Path.Combine(Store.Root, "Foldy.old.exe"); } }
        static string NewExe { get { return Path.Combine(Store.Root, "Foldy.new.exe"); } }

        public class Release { public Version Version; public string Url; }

        public static Version Current { get { return Normalize(Assembly.GetExecutingAssembly().GetName().Version); } }

        static Version Normalize(Version v) { return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision)); }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; // o GitHub não aceita as versões antigas do TLS
            var web = new WebClient { Encoding = Encoding.UTF8 };
            web.Headers[HttpRequestHeader.UserAgent] = "Foldy"; // a API do GitHub recusa pedidos sem isso
            return web;
        }

        // A versão mais nova publicada, ou null se esta já é a mais nova.
        public static Release Check()
        {
            string json;
            using (WebClient web = Client()) json = web.DownloadString(LatestRelease);
            var tag = System.Text.RegularExpressions.Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9]+(\\.[0-9]+){1,3})\"");
            var url = System.Text.RegularExpressions.Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+/Foldy\\.exe)\"");
            if (!tag.Success || !url.Success) return null;
            Version v = Normalize(new Version(tag.Groups[1].Value));
            return v > Current ? new Release { Version = v, Url = url.Groups[1].Value } : null;
        }

        // Baixa e confere se o arquivo é mesmo o Foldy da versão anunciada.
        public static void Download(Release r)
        {
            try { File.Delete(NewExe); } catch { }
            using (WebClient web = Client()) web.DownloadFile(r.Url, NewExe);
            AssemblyName got = AssemblyName.GetAssemblyName(NewExe);
            if (got.Name != "Foldy" || Normalize(got.Version) != r.Version)
            {
                File.Delete(NewExe);
                throw new InvalidDataException("o arquivo baixado não é o Foldy " + r.Version.ToString(2) + ".");
            }
        }

        // Troca o .exe instalado pelo baixado (um .exe em uso pode ser renomeado, só não sobrescrito),
        // reinstala com ele e fecha este processo. "thenArgs" abre a versão nova em seguida (ex.: o painel).
        public static void Apply(string thenArgs)
        {
            CleanupOld();
            Installer.StopOthers();
            File.Move(Store.InstalledExe, OldExe);
            try { File.Move(NewExe, Store.InstalledExe); }
            catch { File.Move(OldExe, Store.InstalledExe); throw; }
            Log.Write("Atualizando da versão " + Current.ToString(2) + ".");
            Process.Start(Store.InstalledExe, "--instalar");
            if (thenArgs != null) Process.Start(Store.InstalledExe, thenArgs);
            Application.Current.Shutdown();
        }

        public static void CleanupOld() { try { File.Delete(OldExe); } catch { } }
    }

    // Painel de controle: as pastas e as opções gerais num lugar só. Aberto de fora da pasta de
    // instalação (ex.: o .exe recém-baixado), mostra a tela de instalar.
    class ControlPanel : Window
    {
        public const string WindowTitle = "Foldy";
        static readonly Brush Surface = new SolidColorBrush(Color.FromRgb(28, 28, 34));
        static readonly Brush RowBackground = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
        static readonly Brush NavSelected = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
        static readonly Brush Danger = new SolidColorBrush(Color.FromRgb(255, 105, 97));

        readonly ContentControl page = new ContentControl();
        readonly StackPanel nav = new StackPanel { Margin = new Thickness(10, 4, 10, 0) };
        string current = "pastas";

        public ControlPanel()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanMinimize; Width = 780; Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; FontFamily = Theme.Font; Title = WindowTitle;
            Icon = AppIcon();

            var scroller = new ScrollViewer
            {
                Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(12, 0, 28, 20),
            };
            var body = new Grid();
            if (Installer.IsInstalledCopy)
            {
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                body.ColumnDefinitions.Add(new ColumnDefinition());
                Grid.SetColumn(scroller, 1);
                body.Children.Add(nav);
                AddNav("pastas", "", "Pastas");
                AddNav("geral", "", "Geral");
                Show("pastas");
                Activated += delegate { Show(current); }; // pega mudanças feitas pela área de trabalho
            }
            else page.Content = InstallPage();
            body.Children.Add(scroller);

            UIElement bar = TopBar();
            var layout = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top);
            layout.Children.Add(bar);
            layout.Children.Add(body);
            Content = new Border
            {
                CornerRadius = new CornerRadius(14), Background = Surface, BorderBrush = Theme.Stroke, BorderThickness = new Thickness(1),
                Margin = new Thickness(14), Child = layout,
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.4, Color = Colors.Black },
            };
            Loaded += delegate { Native.ForceForeground(new WindowInteropHelper(this).Handle); Activate(); };
        }

        // O ícone do programa (src\icone.ico, embutido no .exe pelo build.ps1; o maior tamanho vem primeiro).
        static ImageSource AppIcon()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("icone.ico"))
                return s == null ? null : BitmapFrame.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }

        UIElement TopBar()
        {
            var title = new TextBlock
            {
                Text = WindowTitle, Foreground = Theme.Muted, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0),
            };
            Border min = BarButton("", () => WindowState = WindowState.Minimized);
            Border close = BarButton("", Close);
            var bar = new DockPanel { Height = 46, Background = Brushes.Transparent, LastChildFill = false };
            DockPanel.SetDock(close, Dock.Right);
            DockPanel.SetDock(min, Dock.Right);
            bar.Children.Add(title);
            bar.Children.Add(close);
            bar.Children.Add(min);
            bar.MouseLeftButtonDown += delegate { try { DragMove(); } catch { } };
            return bar;
        }

        static Border BarButton(string glyph, Action click)
        {
            var t = new TextBlock
            {
                Text = glyph, FontFamily = Theme.Icons, FontSize = 10, Foreground = Theme.Muted,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var b = new Border { Width = 40, Height = 32, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 6, 0), Background = Brushes.Transparent, Child = t };
            b.MouseEnter += delegate { b.Background = Theme.Hover; t.Foreground = Theme.Text; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; t.Foreground = Theme.Muted; };
            b.MouseLeftButtonDown += (s, e) => e.Handled = true; // não começa a arrastar a janela
            b.MouseLeftButtonUp += delegate { click(); };
            return b;
        }

        void AddNav(string key, string glyph, string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 15, Foreground = Theme.Text, Width = 30, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = text, Foreground = Theme.Text, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center });
            var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 2), Child = sp, Cursor = Cursors.Hand, Tag = key, Background = Brushes.Transparent };
            b.MouseEnter += delegate { if (current != key) b.Background = Theme.Hover; };
            b.MouseLeave += delegate { if (current != key) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += delegate { Show(key); };
            nav.Children.Add(b);
        }

        void Show(string key)
        {
            current = key;
            foreach (Border b in nav.Children.OfType<Border>()) b.Background = (string)b.Tag == key ? NavSelected : Brushes.Transparent;
            page.Content = key == "geral" ? GeneralPage() : FoldersPage();
        }

        UIElement FoldersPage()
        {
            var sp = new StackPanel();
            Button add = Theme.MakeButton("Nova pasta", true);
            add.Click += delegate { NewFolder(); };
            sp.Children.Add(Header("Pastas", add));
            sp.Children.Add(Note("Um clique numa pasta da área de trabalho abre ela. Pra guardar um app, arraste o atalho pra cima da pasta."));
            List<string> names = Store.Names();
            if (names.Count == 0) sp.Children.Add(Note("Nenhuma pasta ainda. Clique em \"Nova pasta\" pra criar a primeira."));
            foreach (string n in names) sp.Children.Add(FolderRow(n));
            return sp;
        }

        UIElement FolderRow(string name)
        {
            FolderStyle style = FolderStyle.Load(name);
            List<string> items = Store.Items(name);
            List<BitmapSource> icons = items.Take(style.Preview * style.Preview).Select(p => IconUtil.Get(p)).Where(b => b != null).ToList();
            var img = new Image { Source = IconUtil.RenderFolder(icons, 96, style), Width = 46, Height = 46, Margin = new Thickness(0, 0, 14, 0) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = name, Foreground = Theme.Text, FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = items.Count == 1 ? "1 app" : items.Count + " apps", Foreground = Theme.Muted, FontSize = 12 });

            Button edit = Theme.MakeButton("Personalizar", false);
            edit.Click += delegate { CustomizeDialog.Edit(this, name, Message); Show(current); };
            Button del = Theme.MakeButton("Excluir", false);
            del.Click += delegate
            {
                if (!Confirm("Excluir a pasta \"" + name + "\"?\nOs apps de dentro dela voltam pra área de trabalho.")) return;
                Store.DeleteFolder(name);
                Show(current);
            };
            edit.VerticalAlignment = del.VerticalAlignment = VerticalAlignment.Center;

            var row = new DockPanel();
            DockPanel.SetDock(img, Dock.Left);
            DockPanel.SetDock(del, Dock.Right);
            DockPanel.SetDock(edit, Dock.Right);
            row.Children.Add(img);
            row.Children.Add(del);
            row.Children.Add(edit);
            row.Children.Add(text);
            return new Border { CornerRadius = new CornerRadius(12), Background = RowBackground, Padding = new Thickness(12, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8), Child = row };
        }

        void NewFolder()
        {
            string n = InputDialog.Ask(this, "Nova pasta de apps", "Nova pasta", "Criar");
            if (n == null) return;
            Message(Store.CreateFolder(n));
            Show(current);
        }

        UIElement GeneralPage()
        {
            var sp = new StackPanel();
            sp.Children.Add(Header("Geral", null));
            sp.Children.Add(Note("Opções que valem pra todas as pastas."));

            sp.Children.Add(Row("Abrir com um clique",
                "Desligado, as pastas abrem com dois cliques, como uma pasta comum.",
                Theme.MakeToggle(Config.OneClick, on => { Config.OneClick = on; return on; })));
            sp.Children.Add(Row("Iniciar com o Windows",
                "Deixa o programa rodando em segundo plano desde que o PC liga. Ele é leve e é o que faz a pasta abrir com um clique.",
                Theme.MakeToggle(Installer.Autostart, on => { Busy(() => Installer.SetAutostart(on)); return Installer.Autostart; })));
            sp.Children.Add(TransparencyBlock());
            sp.Children.Add(Row("\"Nova pasta de apps\" no botão direito",
                "Mostra a opção no menu do botão direito da área de trabalho (no Windows 11, fica em \"Mostrar mais opções\").",
                Theme.MakeToggle(Store.MenuInstalled, on => { if (on) Store.InstallMenu(); else Store.UninstallMenu(); return Store.MenuInstalled; })));

            bool running = Installer.ResidentRunning;
            Button restart = Theme.MakeButton(running ? "Reiniciar" : "Iniciar", false);
            restart.Click += delegate
            {
                Busy(() => { Installer.StopOthers(); Program.StartResident(); Thread.Sleep(800); });
                Show(current);
            };
            sp.Children.Add(Row("Programa em segundo plano",
                running ? "Rodando." : "Parado. As pastas ainda abrem com dois cliques, mas não com um.", restart));

            sp.Children.Add(Row("Atualizar sozinho",
                "Procura versão nova a cada 6 horas e instala sem você precisar baixar nada. Só troca quando não tem nenhuma pasta aberta.",
                Theme.MakeToggle(Config.AutoUpdate, on => { Config.AutoUpdate = on; return on; })));
            Button check = Theme.MakeButton("Procurar agora", false);
            check.Click += delegate { CheckForUpdateNow(); };
            sp.Children.Add(Row("Versão " + Updater.Current.ToString(2), "Você está usando esta versão do Foldy.", check));

            Button uninstall = Theme.MakeButton("Desinstalar", false);
            ((TextBlock)((Border)uninstall.Content).Child).Foreground = Danger;
            uninstall.Click += delegate
            {
                if (!Confirm("Desinstalar o Foldy?\n\nAs pastas são removidas e os apps de dentro delas voltam pra área de trabalho.")) return;
                if (!Busy(Installer.Uninstall)) return;
                Message("Pronto, o Foldy foi desinstalado.");
                Application.Current.Shutdown();
            };
            var danger = new TextBlock { Text = "Zona de perigo", Foreground = Theme.Muted, FontSize = 12, Margin = new Thickness(2, 16, 0, 8) };
            sp.Children.Add(danger);
            sp.Children.Add(Row("Desinstalar", "Remove o programa do PC. Os apps de dentro das pastas voltam pra área de trabalho, nada se perde.", uninstall));
            return sp;
        }

        void CheckForUpdateNow()
        {
            Updater.Release r = null;
            if (!Busy(() => { r = Updater.Check(); if (r != null) Updater.Download(r); })) return;
            if (r == null) { Message("Você já tem a versão mais nova (" + Updater.Current.ToString(2) + ")."); return; }
            if (!Confirm("O Foldy " + r.Version.ToString(2) + " foi baixado. Instalar agora?\n\nO painel fecha e abre de novo já na versão nova.")) return;
            Busy(() => Updater.Apply("--painel"));
        }

        // Transparência de todas as pastas de uma vez. Aplica quando solta a barra (refazer os
        // ícones de todas as pastas a cada movimento do mouse seria lento).
        UIElement TransparencyBlock()
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "Transparência de todas as pastas", Foreground = Theme.Text, FontSize = 14 });
            sp.Children.Add(new TextBlock
            {
                Text = "Muda todas de uma vez, pra combinar com o papel de parede. Dá pra ajustar uma pasta sozinha depois, no Personalizar dela.",
                Foreground = Theme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10),
            });
            sp.Children.Add(GlobalSlider("Ícone", () => Config.IconTransparency, v => Config.IconTransparency = v, (s, v) => s.IconTransparency = v));
            sp.Children.Add(GlobalSlider("Pasta aberta", () => Config.CardTransparency, v => Config.CardTransparency = v, (s, v) => s.CardTransparency = v));
            return new Border { CornerRadius = new CornerRadius(12), Background = RowBackground, Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8), Child = sp };
        }

        UIElement GlobalSlider(string label, Func<int> saved, Action<int> save, Action<FolderStyle, int> apply)
        {
            int current = saved();
            var name = new TextBlock { Text = label, Foreground = Theme.Text, FontSize = 12.5, Width = 92, VerticalAlignment = VerticalAlignment.Center };
            var value = new TextBlock { Text = current + "%", Foreground = Theme.Muted, FontSize = 12, Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Action redraw;
            FrameworkElement slider = Theme.MakeSlider(() => current, v => { current = (int)v; value.Text = current + "%"; }, FolderStyle.MaxTransparency, out redraw, () =>
            {
                Busy(() =>
                {
                    save(current);
                    foreach (string n in Store.Names())
                    {
                        FolderStyle s = FolderStyle.Load(n);
                        apply(s, current);
                        s.Save(n);
                        Store.UpdateDesktop(n);
                    }
                });
            });
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            DockPanel.SetDock(name, Dock.Left);
            DockPanel.SetDock(value, Dock.Right);
            row.Children.Add(name);
            row.Children.Add(value);
            row.Children.Add(slider);
            return row;
        }

        UIElement InstallPage()
        {
            bool update = Installer.HasInstall;
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 440, Margin = new Thickness(16, 24, 0, 0) };
            var img = new Image { Source = AppIcon(), Width = 120, Height = 120, Margin = new Thickness(0, 0, 0, 18) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            sp.Children.Add(img);
            sp.Children.Add(new TextBlock { Text = "Foldy", Foreground = Theme.Text, FontSize = 26, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(new TextBlock
            {
                Text = "Pastas de apps na área de trabalho, iguais às do celular. Um clique abre a pasta, e é só arrastar os atalhos pra dentro ou pra fora.",
                Foreground = Theme.Muted, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 22),
            });
            Button install = Theme.MakeButton(update ? "Atualizar" : "Instalar", true);
            install.HorizontalAlignment = HorizontalAlignment.Center;
            install.Margin = new Thickness(0);
            install.Click += delegate
            {
                bool ok = Busy(() =>
                {
                    Installer.CopyToInstallDir();
                    using (Process p = Process.Start(Store.InstalledExe, "--instalar")) p.WaitForExit(60000);
                    Process.Start(Store.InstalledExe, "--painel");
                });
                if (ok) Application.Current.Shutdown();
            };
            sp.Children.Add(install);
            sp.Children.Add(new TextBlock
            {
                Text = update ? "Troca a versão instalada por esta. Suas pastas continuam como estão." : "Instala só pra sua conta, sem pedir senha de administrador.",
                Foreground = Theme.Muted, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0),
            });
            return sp;
        }

        static UIElement Header(string text, FrameworkElement right)
        {
            var d = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
            if (right != null)
            {
                DockPanel.SetDock(right, Dock.Right);
                right.VerticalAlignment = VerticalAlignment.Center;
                d.Children.Add(right);
            }
            d.Children.Add(new TextBlock { Text = text, Foreground = Theme.Text, FontSize = 22, FontWeight = FontWeights.SemiBold });
            return d;
        }

        static TextBlock Note(string text)
        {
            return new TextBlock { Text = text, Foreground = Theme.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        }

        static UIElement Row(string title, string description, FrameworkElement control)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            control.Margin = new Thickness(18, 0, 0, 0);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = title, Foreground = Theme.Text, FontSize = 14 });
            text.Children.Add(new TextBlock { Text = description, Foreground = Theme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            var row = new DockPanel();
            DockPanel.SetDock(control, Dock.Right);
            row.Children.Add(control);
            row.Children.Add(text);
            return new Border { CornerRadius = new CornerRadius(12), Background = RowBackground, Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8), Child = row };
        }

        // Roda algo demorado com o cursor de espera; erro vira mensagem em vez de derrubar o painel.
        bool Busy(Action action)
        {
            string err = null;
            Mouse.OverrideCursor = Cursors.Wait;
            try { action(); }
            catch (Exception ex) { err = ex.Message; Log.Write("Painel: " + ex); }
            finally { Mouse.OverrideCursor = null; }
            if (err != null) Message("Não deu certo: " + err);
            return err == null;
        }

        void Message(string text) { Theme.Message(this, text); }

        bool Confirm(string text)
        {
            return MessageBox.Show(this, text, "Foldy", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
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
            IntPtr h = FindWindow(null, "FoldyResidente");
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

        public static void StartResident()
        {
            try { Process.Start(Store.InstalledExe, "--residente"); } catch (Exception ex) { Log.Write("Iniciando residente: " + ex.Message); }
        }

        [STAThread]
        static void Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0] : "--painel";
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

            // Sem argumentos (ex.: o .exe baixado aberto com dois cliques) abre o painel.
            string cmd = args.Length > 0 ? args[0] : "--painel";
            if (cmd == "--painel")
            {
                IntPtr open = Native.FindWindow(null, ControlPanel.WindowTitle);
                if (open != IntPtr.Zero)
                {
                    Native.ShowWindow(open, 9 /* SW_RESTORE */);
                    Native.ForceForeground(open);
                    return;
                }
                if (Installer.IsInstalledCopy && !Installer.ResidentRunning) StartResident();
                app.ShutdownMode = ShutdownMode.OnLastWindowClose;
                app.Run(new ControlPanel());
                return;
            }
            if (cmd == "--residente")
            {
                bool first;
                var mutex = new Mutex(true, @"Local\FoldyResidente", out first);
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
            if (cmd == "--instalar") { Installer.Setup(); return; }
            if (cmd == "--desinstalar") { Installer.Uninstall(); return; }

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
