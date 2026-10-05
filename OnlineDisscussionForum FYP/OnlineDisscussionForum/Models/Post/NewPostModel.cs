using System.ComponentModel.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OnlineDisscussionForum.Models.Post
{
    public class NewPostModel
    {
       public string ForumName { get; set; }
        public int ForumId { get; set; }
        public string AuthorName { get; set; }
        public string ForumImageUrl { get; set; }

        [Required, StringLength(200)]
        public string Title { get; set; }
        [Required, StringLength(20000)]
        public string Content { get; set; }
    }
}
