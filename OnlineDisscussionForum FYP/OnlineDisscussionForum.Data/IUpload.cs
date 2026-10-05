namespace OnlineDisscussionForum.Data;
public interface IUpload
{
    Task<string> SaveImageAsync(Stream stream, string owner, CancellationToken cancellationToken = default);
    Task DeleteImageAsync(string url);
}
