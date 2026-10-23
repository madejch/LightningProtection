using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LightningProtection.Acad
{
    /// <summary>
    /// Устанавливает шрифт GOST Common (прямой и наклонный) для текущего пользователя
    /// без прав администратора, если его нет в системе. Файлы поставляются в папке Fonts пакета.
    /// </summary>
    internal static class FontInstaller
    {
        public const string FontFace = "GOST Common";
        public const string FontFile = "GOST Common.ttf";
        public const string ItalicFontFile = "GOST Common Italic.ttf";
        private const string FontsKey = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";

        /// <summary>Файл шрифта и имя значения в реестре шрифтов Windows.</summary>
        private static readonly (string File, string RegistryValue)[] Fonts =
        {
            (FontFile, FontFace + " (TrueType)"),
            (ItalicFontFile, FontFace + " Italic (TrueType)"),
        };

        /// <summary>Возвращает текст для командной строки или null, если ничего делать не пришлось.</summary>
        public static string? EnsureInstalled()
        {
            var installed = new List<string>();
            var missing = new List<string>();
            foreach (var (file, registryValue) in Fonts)
            {
                if (IsInstalled(file, registryValue))
                    continue;

                var source = FindBundledFont(file);
                if (source == null)
                {
                    missing.Add(file);
                    continue;
                }

                try
                {
                    Install(source, file, registryValue);
                    installed.Add(file);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                {
                    return $"Не удалось установить шрифт {file}: {ex.Message}";
                }
            }

            if (installed.Count > 0)
                PostMessage(new IntPtr(0xFFFF), 0x001D, IntPtr.Zero, IntPtr.Zero); // WM_FONTCHANGE всем окнам

            var messages = new List<string>();
            if (installed.Count > 0)
                messages.Add($"Установлен шрифт: {string.Join(", ", installed)}. Если текст отображается другим шрифтом, перезапустите AutoCAD.");
            if (missing.Count > 0)
                messages.Add($"Шрифт не найден ни в системе, ни в пакете плагина: {string.Join(", ", missing)} — будет использован шрифт-заменитель.");
            return messages.Count > 0 ? string.Join("\n", messages) : null;
        }

        private static bool IsInstalled(string file, string registryValue)
        {
            var systemFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (File.Exists(Path.Combine(systemFonts, file)) || File.Exists(Path.Combine(UserFontsFolder, file)))
                return true;
            return HasRegistryValue(Registry.LocalMachine, registryValue) || HasRegistryValue(Registry.CurrentUser, registryValue);
        }

        private static void Install(string source, string file, string registryValue)
        {
            Directory.CreateDirectory(UserFontsFolder);
            var target = Path.Combine(UserFontsFolder, file);
            File.Copy(source, target, true);
            using (var key = Registry.CurrentUser.CreateSubKey(FontsKey))
                key?.SetValue(registryValue, target);
            AddFontResource(target);
        }

        private static string UserFontsFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Fonts");

        private static bool HasRegistryValue(RegistryKey root, string registryValue)
        {
            using var key = root.OpenSubKey(FontsKey);
            return key?.GetValue(registryValue) != null;
        }

        /// <summary>Шрифт ищется в Contents\Fonts пакета или рядом со сборкой.</summary>
        private static string? FindBundledFont(string file)
        {
            var folder = Path.GetDirectoryName(typeof(FontInstaller).Assembly.Location) ?? "";
            foreach (var candidate in new[]
                     {
                         Path.Combine(folder, "..", "Fonts", file),
                         Path.Combine(folder, "Fonts", file),
                         Path.Combine(folder, file),
                     })
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            return null;
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern int AddFontResource(string fileName);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
