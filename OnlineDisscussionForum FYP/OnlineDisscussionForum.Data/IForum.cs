using OnlineDisscussionForum.Data.Models;
namespace OnlineDisscussionForum.Data;
public interface IForum
{
    Forum GetById(int id);
    IQueryable<Forum> GetAll();
    Task Create(Forum forum);
    Task<bool> Delete(int forumId, string version = null);
    Task<bool> Update(int id, string title, string description, string imageUrl, string version);
    Task<bool> UpdateForumTitle(int forumId, string title);
    Task<bool> UpdateForumDescription(int forumId, string description);
    IEnumerable<ApplicationUser> GetActiveUsers(int id);
    bool HasRecentPost(int id);
}
