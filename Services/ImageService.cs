using Android.Graphics;
using Android.Media;

namespace Telegram_Memo_App.Services;

public class ImageService
{
    public const int MaxSide = 1600;
    public const int JpegQuality = 80;

    public string PhotosDirectory { get; } = System.IO.Path.Combine(FileSystem.AppDataDirectory, "photos");

    public string CapturesDirectory { get; } = System.IO.Path.Combine(FileSystem.CacheDirectory, "captures");

    // Decodes the camera JPEG, applies its EXIF orientation so the pixels are upright,
    // scales the longest side to MaxSide and writes a new JPEG.
    // mirror: front camera photos are flipped horizontally so they match the (mirrored) live preview.
    // This runs once at capture time; the queue only moves the resulting file and never re-processes it.
    public Task<string> SaveCaptureAsync(byte[] jpeg, bool mirror) => Task.Run(() =>
    {
        Directory.CreateDirectory(CapturesDirectory);
        var path = System.IO.Path.Combine(CapturesDirectory, $"capture_{Guid.NewGuid():N}.jpg");

        var orientation = (int)Android.Media.Orientation.Normal;
        try
        {
            using var exifStream = new MemoryStream(jpeg);
            var exif = new ExifInterface(exifStream);
            orientation = exif.GetAttributeInt(ExifInterface.TagOrientation, (int)Android.Media.Orientation.Normal);
        }
        catch
        {
            // No readable EXIF: keep the image as is.
        }

        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(jpeg, 0, jpeg.Length, bounds);

        // Power-of-two downsampling while decoding keeps memory low.
        var sample = 1;
        while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= MaxSide)
            sample *= 2;

        using var decoded = BitmapFactory.DecodeByteArray(jpeg, 0, jpeg.Length, new BitmapFactory.Options { InSampleSize = sample })
            ?? throw new InvalidOperationException("Could not decode the photo.");

        var scale = Math.Min(1f, MaxSide / (float)Math.Max(decoded.Width, decoded.Height));
        var matrix = new Matrix();
        matrix.PostScale(scale, scale);
        ApplyOrientation(matrix, orientation);

        // CameraX returns the front photo unmirrored. Some devices instead tag it with an EXIF flip,
        // which ApplyOrientation already applied; flipping again would undo the mirror.
        if (mirror && !IsFlipOrientation(orientation))
        {
            // Post-concatenated after rotation, so this flips the upright image left-right
            // without changing its rotation or size.
            matrix.PostScale(-1, 1);
        }

        var output = Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true)!;
        try
        {
            using var fs = File.Create(path);
            output.Compress(Bitmap.CompressFormat.Jpeg!, JpegQuality, fs);
        }
        finally
        {
            // CreateBitmap may return the same instance when the matrix is identity.
            if (!ReferenceEquals(output, decoded))
                output.Dispose();
        }
        return path;
    });

    // Moves an already normalized capture into permanent app storage.
    public string MoveToPhotos(string capturePath)
    {
        Directory.CreateDirectory(PhotosDirectory);
        var dest = System.IO.Path.Combine(PhotosDirectory, $"memo_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}.jpg");
        File.Move(capturePath, dest);
        return dest;
    }

    public void ClearCaptures()
    {
        try
        {
            if (Directory.Exists(CapturesDirectory))
                Directory.Delete(CapturesDirectory, true);
        }
        catch
        {
            // Cache cleanup is best effort.
        }
    }

    static bool IsFlipOrientation(int orientation) => (Android.Media.Orientation)orientation is
        Android.Media.Orientation.FlipHorizontal or Android.Media.Orientation.FlipVertical
        or Android.Media.Orientation.Transpose or Android.Media.Orientation.Transverse;

    static void ApplyOrientation(Matrix m, int orientation)
    {
        switch ((Android.Media.Orientation)orientation)
        {
            case Android.Media.Orientation.Rotate90: m.PostRotate(90); break;
            case Android.Media.Orientation.Rotate180: m.PostRotate(180); break;
            case Android.Media.Orientation.Rotate270: m.PostRotate(270); break;
            case Android.Media.Orientation.FlipHorizontal: m.PostScale(-1, 1); break;
            case Android.Media.Orientation.FlipVertical: m.PostScale(1, -1); break;
            case Android.Media.Orientation.Transpose: m.PostRotate(90); m.PostScale(-1, 1); break;
            case Android.Media.Orientation.Transverse: m.PostRotate(270); m.PostScale(-1, 1); break;
        }
    }
}
