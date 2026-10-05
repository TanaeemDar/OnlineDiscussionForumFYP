using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OnlineDisscussionForum.Data.Models;
namespace OnlineDisscussionForum.Services;
public class ActiveSignInManager(UserManager<ApplicationUser> users, IHttpContextAccessor accessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claims, IOptions<IdentityOptions> options,
    ILogger<SignInManager<ApplicationUser>> logger, IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation)
    : SignInManager<ApplicationUser>(users, accessor, claims, options, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(ApplicationUser user) => user.IsActive && await base.CanSignInAsync(user);
}
