using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OnlineDisscussionForum.Models.Forum
{
    public class AddForumModel
    { 
        [Required, StringLength(150)]
        public string Title { get; set; }
        [Required, StringLength(2000)]
        public string Description { get; set; }
        public IFormFile ImageUpload { get; set; }
    }
}
