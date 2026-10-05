
using System;
using System.Collections.Generic;
using System.Text;

namespace OnlineDisscussionForum.Data.Models
{
   public class Post
    {
        public int Id { get; set; }
        public string Version { get; set; } = Guid.NewGuid().ToString();
        public int ForumId { get; set; }
        public string UserId { get; set; }

        public string Title { get; set; }
        public string Content { get; set; }
        public DateTime Created { get; set; }
        public virtual ApplicationUser User { get; set; }
        public virtual Forum Forum { get; set; }
        public virtual ICollection<PostReply> Replies { get; set; } = new List<PostReply>();
    }
}
