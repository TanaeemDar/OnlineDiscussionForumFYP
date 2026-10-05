using System.Text.RegularExpressions;
using OnlineDisscussionForum.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace OnlineDisscussionForum.Services;
public class LocalUploadService(LocalDataPaths paths, ILogger<LocalUploadService> logger) : IUpload
{
    public async Task<string> SaveImageAsync(Stream stream, string owner, CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(owner, "^[a-zA-Z0-9_-]+$")) throw new InvalidDataException("Invalid image owner.");
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + count > 5 * 1024 * 1024) throw new InvalidDataException("Image must be no larger than 5 MB.");
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
        }
        if (buffer.Length == 0) throw new InvalidDataException("Choose a nonempty image.");
        try
        {
            buffer.Position = 0;
            var info = await Image.IdentifyAsync(buffer, cancellationToken);
            if (info.Width > 4096 || info.Height > 4096 || (long)info.Width * info.Height > 16000000)
                throw new InvalidDataException("Image dimensions must not exceed 4096 pixels or 16 million pixels.");
            var format = info.Metadata.DecodedImageFormat?.Name;
            if (format is not ("PNG" or "JPEG" or "WEBP")) throw new InvalidDataException("Choose a PNG, JPEG, or WebP image.");
            buffer.Position = 0;
            using var image = await Image.LoadAsync(buffer, cancellationToken);
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IccProfile = null;
            while (image.Frames.Count > 1) image.Frames.RemoveFrame(1);
            var name = Guid.NewGuid().ToString("N") + ".png";
            var directory = Path.Combine(paths.Root, "uploads", owner);
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, name);
            try { await image.SaveAsync(file, new PngEncoder(), cancellationToken); }
            catch { if (File.Exists(file)) File.Delete(file); throw; }
            return $"/uploads/{owner}/{name}";
        }
        catch (UnknownImageFormatException) { throw new InvalidDataException("File is not a supported image."); }
        catch (InvalidImageContentException) { throw new InvalidDataException("Image content is invalid."); }
    }
    public Task DeleteImageAsync(string url)
    {
        if (url != null && Regex.IsMatch(url, "^/uploads/[a-zA-Z0-9_-]+/[a-f0-9]{32}\\.png$"))
        {
            try { File.Delete(Path.Combine(paths.Root, url.TrimStart('/'))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { logger.LogWarning(ex, "Image cleanup failed for {ImageUrl}", url); }
        }
        return Task.CompletedTask;
    }
}
