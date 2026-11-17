using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Wyszyciuszek.Core.Imaging;

namespace Wyszyciuszek.Canvas;

/// <summary>Wczytany obraz: piksele do upraszczania i bitmapa do pokazania.</summary>
internal sealed class LoadedImage(ImageRgb pixels, CanvasBitmap bitmap, byte[] fileBytes) : IDisposable
{
    public ImageRgb Pixels { get; } = pixels;
    public CanvasBitmap Bitmap { get; } = bitmap;
    /// <summary>Oryginalny plik (zapisywany w projekcie).</summary>
    public byte[] FileBytes { get; } = fileBytes;

    public void Dispose() => Bitmap.Dispose();
}

internal static class ImageLoader
{
    /// <summary>Największy obsługiwany bok (większe obrazy są zmniejszane przy wczytaniu).</summary>
    private const int MaxSide = 8192;

    public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".jxr", ".ico"];

    public static bool IsImagePath(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>
    /// Dekoduje plik obrazu przez WIC: z obrotem wg EXIF (zdjęcia z telefonu), zarządzaniem kolorem do sRGB
    /// i przezroczystością nałożoną na biel.
    /// </summary>
    public static async Task<LoadedImage> DecodeAsync(byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        uint w = decoder.OrientedPixelWidth, h = decoder.OrientedPixelHeight;
        if (w == 0 || h == 0) throw new InvalidDataException("Obraz jest pusty.");

        var transform = new BitmapTransform();
        if (w > MaxSide || h > MaxSide)
        {
            double s = MaxSide / (double)Math.Max(w, h);
            // skalowanie przed obrotem EXIF — wymiary w układzie pliku
            transform.ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * s));
            transform.ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * s));
            transform.InterpolationMode = BitmapInterpolationMode.Fant;
            w = (uint)Math.Max(1, Math.Round(w * s));
            h = (uint)Math.Max(1, Math.Round(h * s));
        }
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        var bgra = data.DetachPixelData();
        if (bgra.Length != w * h * 4) throw new InvalidDataException("Nieoczekiwany rozmiar danych obrazu.");

        var pixels = ImageRgb.FromBgraPremultiplied((int)w, (int)h, bgra, Rgb.White);
        // do podglądu też biel pod przezroczystością — tak samo jak widzi to upraszczanie
        var bitmap = CanvasBitmap.CreateFromBytes(CanvasDevice.GetSharedDevice(), pixels.ToBgra(), (int)w, (int)h,
            DirectXPixelFormat.B8G8R8A8UIntNormalized);
        return new LoadedImage(pixels, bitmap, bytes);
    }

    public static async Task<byte[]> ReadAllAsync(IRandomAccessStream stream)
    {
        stream.Seek(0);
        using var s = stream.AsStreamForRead();
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms);
        return ms.ToArray();
    }
}
