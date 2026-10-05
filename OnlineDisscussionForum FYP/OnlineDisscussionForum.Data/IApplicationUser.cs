using OnlineDisscussionForum.Data.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace OnlineDisscussionForum.Data
{
    public interface IApplicationUser
    {
        ApplicationUser GetById(string id);
        IQueryable<ApplicationUser> GetAll();
        Task SetProfileImage(string id, Uri uri);


    }
}
