using OnlineDisscussionForum.Data.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OnlineDisscussionForum.Data
{
    public interface IForum
    {
        Forum GetById (int id);
        IEnumerable<Forum> GetAll();
       
        Task Create(Forum forum);
        Task Delete(int forumId);
        Task UpdateForumTitle(int forumId, string newTitle);
        Task UpdateForumDescription(int forumId, string newDescription);
        IEnumerable<ApplicationUser> GetActiveUsers(int id);
        bool HasRecentPost(int id);
    }
}
