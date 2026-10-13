using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace DesktopHtmlHost
{
    // User choices kept in the per-user data folder (packaged profile for the Store build).
    // The custom center logo is stored as a normalized square PNG; its absence means the default logo.
    public static class UserSettings
    {
        public const int LogoSize = 512;
        public const long MaxLogoSourceBytes = 25L * 1024 * 1024;
        private const string CustomLogoFileName = "custom-logo.png";

        public static string CustomLogoPath
        {
            get { return Path.Combine(Program.GetAppDataFolder(), CustomLogoFileName); }
        }

        public static bool HasCustomLogo
        {
            get { return File.Exists(CustomLogoPath); }
        }

        // Center-crops any readable image to a square, scales it to LogoSize and saves it as PNG.
        public static void ImportLogo(string sourcePath)
        {
            ImportLogo(sourcePath, CustomLogoPath);
        }

        public static void ImportLogo(string sourcePath, string destinationPath)
        {
            var info = new FileInfo(sourcePath);
            if (!info.Exists) throw new FileNotFoundException("Image not found.", sourcePath);
            if (info.Length > MaxLogoSourceBytes) throw new InvalidDataException("Image larger than 25 MB.");

            using (var stream = new MemoryStream(File.ReadAllBytes(sourcePath)))
            using (var source = Image.FromStream(stream, true, true))
            using (var square = new Bitmap(LogoSize, LogoSize, PixelFormat.Format32bppArgb))
            {
                int side = Math.Min(source.Width, source.Height);
                if (side < 16) throw new InvalidDataException("Image too small.");
                var crop = new Rectangle((source.Width - side) / 2, (source.Height - side) / 2, side, side);
                using (var graphics = Graphics.FromImage(square))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.DrawImage(source, new Rectangle(0, 0, LogoSize, LogoSize), crop, GraphicsUnit.Pixel);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                string temporary = destinationPath + ".tmp";
                square.Save(temporary, ImageFormat.Png);
                if (File.Exists(destinationPath)) File.Delete(destinationPath);
                File.Move(temporary, destinationPath);
            }
        }

        public static void ResetLogo()
        {
            if (File.Exists(CustomLogoPath)) File.Delete(CustomLogoPath);
        }

        // Message for the page: the custom logo as a data URL, or null for the default logo.
        public static string BuildSettingsMessage()
        {
            return BuildSettingsMessage(HasCustomLogo ? CustomLogoPath : null);
        }

        public static string BuildSettingsMessage(string logoPath)
        {
            string logo = "null";
            if (logoPath != null)
            {
                try { logo = "\"data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(logoPath)) + "\""; }
                catch (Exception ex) { Program.LogDebug("Custom logo read failed: " + ex.Message); }
            }
            return "{\"control\":\"SETTINGS\",\"logo\":" + logo + "}";
        }
    }
}
