using Microsoft.EntityFrameworkCore;
using OnlineDisscussionForum.Data;
using OnlineDisscussionForum.Data.Models;

namespace OnlineDisscussionForum.Service;

public class ApplicationUserService(ApplicationDbContext context) : IApplicationUser
{
    public IQueryable<ApplicationUser> GetAll() => context.Users.AsNoTracking();
    public ApplicationUser GetById(string id) => GetAll().SingleOrDefault(user => user.Id == id);

    public async Task SetProfileImage(string id, Uri uri)
    {
        // Only update the image column; a concurrent content write may have changed the rating.
        var count = await context.Users.Where(user => user.Id == id && user.IsActive)
            .ExecuteUpdateAsync(set => set.SetProperty(user => user.ProfileImageUrl, uri.ToString()));
        if (count == 0) throw new InvalidOperationException("The account is no longer active.");
    }
}
