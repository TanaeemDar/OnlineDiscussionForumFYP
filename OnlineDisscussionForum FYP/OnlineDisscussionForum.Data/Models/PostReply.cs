using System;


namespace OnlineDisscussionForum.Data.Models
{
   public class PostReply
    {
        public int Id { get; set; }
        public string Version { get; set; } = Guid.NewGuid().ToString();
        public int PostId { get; set; }
        public string UserId { get; set; }

        public string Content { get; set; }
       
        public DateTime Created { get; set; }
        public virtual ApplicationUser User { get; set; }
        public virtual Post Post { get; set; }


    }
}
