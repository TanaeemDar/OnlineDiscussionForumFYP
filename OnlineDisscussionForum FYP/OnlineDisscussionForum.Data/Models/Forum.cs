using System;
using System.Collections.Generic;

namespace OnlineDisscussionForum.Data.Models
{
    public class Forum
    { 
        public int Id { get; set; }
        public string Version { get; set; } = Guid.NewGuid().ToString();

        public string Title { get; set; }
        public string Description { get; set; }
        public DateTime Created { get; set; }
        public string ImageUrl{ get; set; }

        public virtual ICollection<Post> Posts { get; set; } = new List<Post>();


    }
}
