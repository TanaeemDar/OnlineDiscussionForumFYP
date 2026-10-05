using OnlineDisscussionForum.Data.Models;
namespace OnlineDisscussionForum.Data;
public interface IPost
{
    Post GetById(int id);
    IQueryable<Post> GetAll();
    IQueryable<Post> GetFilteredPosts(Forum forum, string searchQuery);
    IQueryable<Post> GetFilteredPosts(string searchQuery);
    IQueryable<Post> GetPostsByForum(int id);
    IQueryable<Post> GetLatestPosts(int n);
    Task Add(Post post);
    Task<bool> Delete(int id, string version = null);
    Task<bool> EditPostContent(int id, string newContent);
    Task<bool> Update(int id, string title, string content, string version);
    Task AddReply(PostReply reply);
    PostReply GetReplyById(int id);
    Task<bool> UpdateReply(int id, string content, string version);
    Task<bool> DeleteReply(int id, string version);
}
