using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using OnlineDisscussionForum.Data.Models;

namespace OnlineDisscussionForum.Data;

public class DataSeeder(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, IConfiguration configuration)
{
    public async Task SeedSuperSeeder()
    {
        if (!await roles.RoleExistsAsync("Admin")) Check(await roles.CreateAsync(new IdentityRole("Admin")));
        var name = configuration["Admin:UserName"];
        var password = configuration["Admin:Password"];
        var email = configuration["Admin:Email"];
        if (string.IsNullOrWhiteSpace(name)) return;
        var user = await users.FindByNameAsync(name);
        if (user == null)
        {
            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(email))
                throw new InvalidOperationException("Initial administrator requires Admin:Email and Admin:Password.");
            user = new ApplicationUser { UserName = name, Email = email, EmailConfirmed = true, MemberSince = DateTime.UtcNow };
            Check(await users.CreateAsync(user, password));
        }
        if (!await users.IsInRoleAsync(user, "Admin")) Check(await users.AddToRoleAsync(user, "Admin"));
    }
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
