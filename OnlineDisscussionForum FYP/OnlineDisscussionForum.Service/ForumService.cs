using Microsoft.EntityFrameworkCore;
using OnlineDisscussionForum.Data;
using OnlineDisscussionForum.Data.Models;
namespace OnlineDisscussionForum.Service;
public class ForumService(ApplicationDbContext context) : IForum
{
    public IQueryable<Forum> GetAll() => context.Forums.AsNoTracking();
    public Forum GetById(int id) => GetAll().SingleOrDefault(f => f.Id == id);
    public async Task Create(Forum forum) { context.Forums.Add(forum); await context.SaveChangesAsync(); }
    public IEnumerable<ApplicationUser> GetActiveUsers(int id) => context.Users.AsNoTracking().Where(u =>
        context.Posts.Any(p => p.ForumId == id && p.UserId == u.Id) ||
        context.PostReplies.Any(r => r.Post.ForumId == id && r.UserId == u.Id)).ToList();
    public bool HasRecentPost(int id)
    {
        var cutoff = DateTime.UtcNow.AddHours(-12);
        return context.Posts.Any(p => p.ForumId == id && p.Created > cutoff);
    }
    public async Task<bool> Update(int id, string title, string description, string imageUrl, string version)
    {
        var forum = await context.Forums.SingleOrDefaultAsync(f => f.Id == id);
        if (forum == null) return false;
        context.Entry(forum).Property(f => f.Version).OriginalValue = version;
        forum.Title = title.Trim(); forum.Description = description.Trim(); forum.ImageUrl = imageUrl;
        forum.Version = Guid.NewGuid().ToString();
        return await Save();
    }
    public async Task<bool> UpdateForumTitle(int id, string title)
    {
        var f = GetById(id); return f != null && await Update(id, title, f.Description, f.ImageUrl, f.Version);
    }
    public async Task<bool> UpdateForumDescription(int id, string description)
    {
        var f = GetById(id); return f != null && await Update(id, f.Title, description, f.ImageUrl, f.Version);
    }
    public async Task<bool> Delete(int id, string version = null)
    {
        if (await context.Posts.AnyAsync(p => p.ForumId == id)) return false;
        var forum = await context.Forums.SingleOrDefaultAsync(f => f.Id == id);
        if (forum == null) return false;
        if (version != null) context.Entry(forum).Property(f => f.Version).OriginalValue = version;
        context.Forums.Remove(forum);
        try { return await Save(); }
        catch (DbUpdateException) { context.ChangeTracker.Clear(); return false; }
    }
    private async Task<bool> Save()
    {
        try { await context.SaveChangesAsync(); return true; }
        catch (DbUpdateConcurrencyException) { context.ChangeTracker.Clear(); return false; }
    }
}
