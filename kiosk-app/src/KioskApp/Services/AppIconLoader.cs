using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KioskApp.Services
{
    /// <summary>
    /// אייקון לאריח של תוכנה מאושרת: תמונה שהוגדרה בדף הניהול (PNG/ICO/JPG במחשב העמדה), ואם
    /// אין - האייקון של קובץ ההרצה עצמו (כמו בשולחן העבודה, 48 פיקסלים). null = אין אייקון.
    /// </summary>
    public static class AppIconLoader
    {
        public static ImageSource? Load(string? iconPath, string? exePath)
        {
            var icon = Clean(iconPath);
            if (icon.Length > 0 && File.Exists(icon))
            {
                var ext = Path.GetExtension(icon).ToLowerInvariant();
                var image = ext is ".exe" or ".dll" ? FromExecutable(icon) : FromImageFile(icon);
                if (image != null) return image;
            }
            var exe = Clean(exePath);
            return exe.Length > 0 && File.Exists(exe) ? FromExecutable(exe) : null;
        }

        private static string Clean(string? path) => (path ?? "").Trim().Trim('"').Trim();

        private static ImageSource? FromImageFile(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad; // לא משאירים את הקובץ נעול
                bmp.DecodePixelWidth = 128;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                App.LogError($"אייקון: כשל בטעינת התמונה {path}", ex);
                return null;
            }
        }

        private static ImageSource? FromExecutable(string path)
        {
            // אייקון גדול (48x48) מרשימת האייקונים של Windows; אם לא הצליח - האייקון הרגיל (32x32)
            try
            {
                var info = new ShFileInfo();
                if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiSysIconIndex) != IntPtr.Zero)
                {
                    var iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"); // IImageList
                    if (SHGetImageList(ShilExtraLarge, ref iid, out var imageList) == 0 && imageList != IntPtr.Zero)
                    {
                        var hIcon = ImageList_GetIcon(imageList, info.iIcon, IldTransparent);
                        if (hIcon != IntPtr.Zero)
                        {
                            try { return FromHIcon(hIcon); }
                            finally { DestroyIcon(hIcon); }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogError($"אייקון: כשל בטעינת אייקון גדול מ-{path}", ex);
            }

            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                return icon == null ? null : FromHIcon(icon.Handle);
            }
            catch (Exception ex)
            {
                App.LogError($"אייקון: כשל בטעינת האייקון מ-{path}", ex);
                return null;
            }
        }

        private static ImageSource FromHIcon(IntPtr hIcon)
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }

        private const uint ShgfiSysIconIndex = 0x4000;
        private const int ShilExtraLarge = 2; // 48x48
        private const int IldTransparent = 0x1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShFileInfo
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", EntryPoint = "#727")]
        private static extern int SHGetImageList(int iImageList, ref Guid riid, out IntPtr ppv);

        [DllImport("comctl32.dll")]
        private static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, int flags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}
