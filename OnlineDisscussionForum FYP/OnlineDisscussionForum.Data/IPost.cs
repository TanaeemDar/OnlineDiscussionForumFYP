using OnlineDisscussionForum.Data.Models;

using System.Collections.Generic;

using System.Threading.Tasks;

namespace OnlineDisscussionForum.Data
{
    public interface IPost
    {
        Post GetById(int id);
        IEnumerable<Post> GetAll();
        IEnumerable<Post> GetFilteredPosts(Forum forum,string searchQuery);
        IEnumerable<Post> GetFilteredPosts(string searchQuery);
        IEnumerable<Post> GetPostsByForum(int id);
        IEnumerable<Post> GetLatestPosts(int n);
        Task Add(Post post);
        Task Delete(int id);
        Task EditPostContent(int id, string nwContent);
        Task AddReply(PostReply reply);
        
    }
}
