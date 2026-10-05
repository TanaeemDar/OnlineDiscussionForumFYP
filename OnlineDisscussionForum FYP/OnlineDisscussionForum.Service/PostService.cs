using Microsoft.EntityFrameworkCore;
using OnlineDisscussionForum.Data;
using OnlineDisscussionForum.Data.Models;
namespace OnlineDisscussionForum.Service;

public class PostService(ApplicationDbContext context) : IPost
{
    public IQueryable<Post> GetAll() => context.Posts.AsNoTracking().Include(p => p.User).Include(p => p.Forum).Include(p => p.Replies);
    public Post GetById(int id) => context.Posts.AsNoTracking().Include(p => p.User).Include(p => p.Forum)
        .Include(p => p.Replies).ThenInclude(r => r.User).SingleOrDefault(p => p.Id == id);
    public IQueryable<Post> GetPostsByForum(int id) => GetAll().Where(p => p.ForumId == id);
    public IQueryable<Post> GetFilteredPosts(Forum forum, string query) => Filter(GetPostsByForum(forum.Id), query);
    public IQueryable<Post> GetFilteredPosts(string query) => Filter(GetAll(), query);
    private static IQueryable<Post> Filter(IQueryable<Post> posts, string query)
    {
        query = query?.Trim();
        return string.IsNullOrEmpty(query) ? posts : posts.Where(p => p.Title.Contains(query) || p.Content.Contains(query));
    }
    public IQueryable<Post> GetLatestPosts(int n) => GetAll().OrderByDescending(p => p.Created).ThenByDescending(p => p.Id).Take(n);
    public async Task Add(Post post)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        post.User = null; post.Forum = null;
        context.Posts.Add(post);
        await context.SaveChangesAsync();
        await context.Users.Where(u => u.Id == post.UserId).ExecuteUpdateAsync(set => set.SetProperty(u => u.Rating, u => u.Rating + 1));
        await transaction.CommitAsync();
    }
    public async Task AddReply(PostReply reply)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        reply.User = null; reply.Post = null;
        context.PostReplies.Add(reply);
        await context.SaveChangesAsync();
        await context.Users.Where(u => u.Id == reply.UserId).ExecuteUpdateAsync(set => set.SetProperty(u => u.Rating, u => u.Rating + 3));
        await transaction.CommitAsync();
    }
    public async Task<bool> Update(int id, string title, string content, string version)
    {
        var post = await context.Posts.SingleOrDefaultAsync(p => p.Id == id);
        if (post == null) return false;
        context.Entry(post).Property(p => p.Version).OriginalValue = version;
        post.Title = title.Trim(); post.Content = content.Trim(); post.Version = Guid.NewGuid().ToString();
        return await Save();
    }
    public async Task<bool> EditPostContent(int id, string newContent)
    {
        var post = GetById(id);
        return post != null && await Update(id, post.Title, newContent, post.Version);
    }
    public async Task<bool> Delete(int id, string version = null)
    {
        var post = await context.Posts.SingleOrDefaultAsync(p => p.Id == id);
        if (post == null) return false;
        if (version != null) context.Entry(post).Property(p => p.Version).OriginalValue = version;
        context.Posts.Remove(post);
        return await Save();
    }
    public PostReply GetReplyById(int id) => context.PostReplies.AsNoTracking().Include(r => r.User).Include(r => r.Post).SingleOrDefault(r => r.Id == id);
    public async Task<bool> UpdateReply(int id, string content, string version)
    {
        var reply = await context.PostReplies.SingleOrDefaultAsync(r => r.Id == id);
        if (reply == null) return false;
        context.Entry(reply).Property(r => r.Version).OriginalValue = version;
        reply.Content = content.Trim(); reply.Version = Guid.NewGuid().ToString();
        return await Save();
    }
    public async Task<bool> DeleteReply(int id, string version)
    {
        var reply = await context.PostReplies.SingleOrDefaultAsync(r => r.Id == id);
        if (reply == null) return false;
        context.Entry(reply).Property(r => r.Version).OriginalValue = version;
        context.PostReplies.Remove(reply);
        return await Save();
    }
    private async Task<bool> Save()
    {
        try { await context.SaveChangesAsync(); return true; }
        catch (DbUpdateConcurrencyException) { context.ChangeTracker.Clear(); return false; }
    }
}
