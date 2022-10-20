

using OnlineDisscussionForum.Models.Post;
using System.Collections.Generic;

namespace OnlineDisscussionForum.Models.Forum
{
    public class ForumTopicModel
    {
        public ForumListingModel Forum {get;set;}
        public string SearchQuery { get; set; }
        public IEnumerable<PostListingModel> Posts { get; set; }
    }
}
