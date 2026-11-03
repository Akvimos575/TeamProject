using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TeamProject.Core;

namespace TeamProject.Vision
{
    /// <summary>Пиксели области экрана в формате BGRA.</summary>
    internal sealed class Frame
    {
        public Frame(byte[] bgra, int width, int height, int stride)
        {
            Bgra = bgra;
            Width = width;
            Height = height;
            Stride = stride;
        }

        public byte[] Bgra { get; }
        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
    }

    /// <summary>
    /// Снимок области экрана через GDI (CopyFromScreen). Работает в оконном режиме и в
    /// «полноэкранном окне без рамки»; в эксклюзивном полноэкранном режиме может давать чёрный кадр.
    /// </summary>
    internal static class ScreenCapture
    {
        public static Bitmap CaptureBitmap(ScreenRect r)
        {
            if (r.IsEmpty) throw new InvalidOperationException("Область экрана не откалибрована. Запустите команду calibrate.");
            var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(r.X, r.Y, 0, 0, new Size(r.Width, r.Height), CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }

        public static Frame Capture(ScreenRect r)
        {
            using var bmp = CaptureBitmap(r);
            return ToFrame(bmp);
        }

        public static Frame ToFrame(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                var bytes = new byte[stride * bmp.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                return new Frame(bytes, bmp.Width, bmp.Height, stride);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        /// <summary>Сохранить область в PNG (для отладки калибровки).</summary>
        public static void SavePng(ScreenRect r, string path)
        {
            using var bmp = CaptureBitmap(r);
            bmp.Save(path, ImageFormat.Png);
        }
    }
}
