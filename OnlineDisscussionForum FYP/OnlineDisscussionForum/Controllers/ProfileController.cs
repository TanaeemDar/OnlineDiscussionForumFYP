using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OnlineDisscussionForum.Data;
using OnlineDisscussionForum.Data.Models;
using OnlineDisscussionForum.Models.ApplicationUser;
namespace OnlineDisscussionForum.Controllers;
[Authorize]
public class ProfileController(UserManager<ApplicationUser> users, IApplicationUser profiles, IUpload uploads) : Controller
{
    public async Task<IActionResult> Detail(string id)
    {
        var user = profiles.GetById(id); if (user == null) return NotFound();
        return View(new ProfileModel { UserId = user.Id, UserName = user.UserName, UserRating = user.Rating.ToString(),
            Email = user.Email, ProfileImageUrl = user.ProfileImageUrl, MemberSince = user.MemberSince, IsAdmin = await users.IsInRoleAsync(user, "Admin") });
    }
    [HttpPost]
    public async Task<IActionResult> UploadProfileImage(IFormFile file)
    {
        var user = await users.GetUserAsync(User); if (user == null) return Challenge();
        if (file == null) { TempData["UploadError"] = "Choose an image."; return RedirectToAction(nameof(Detail), new { id = user.Id }); }
        string image = null;
        var oldImage = user.ProfileImageUrl;
        try
        {
            using var stream = file.OpenReadStream(); image = await uploads.SaveImageAsync(stream, user.Id);
            await profiles.SetProfileImage(user.Id, new Uri(image, UriKind.Relative));
        }
        catch (InvalidDataException ex) { TempData["UploadError"] = ex.Message; return RedirectToAction(nameof(Detail), new { id = user.Id }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await uploads.DeleteImageAsync(image);
            TempData["UploadError"] = "The image could not be saved. Try again later.";
            return RedirectToAction(nameof(Detail), new { id = user.Id });
        }
        catch { await uploads.DeleteImageAsync(image); throw; }
        await uploads.DeleteImageAsync(oldImage);
        return RedirectToAction(nameof(Detail), new { id = user.Id });
    }
    [Authorize(Roles = "Admin")]
    public IActionResult Index(int page = 1)
    {
        page = Math.Max(1, page); ViewBag.Page = page;
        var list = profiles.GetAll().OrderByDescending(u => u.Rating).ThenBy(u => u.Id).Skip((page - 1) * 20).Take(21).Select(u => new ProfileModel
        { UserId = u.Id, Email = u.Email, UserName = u.UserName, ProfileImageUrl = u.ProfileImageUrl, UserRating = u.Rating.ToString(), MemberSince = u.MemberSince }).ToList();
        ViewBag.HasNext = list.Count > 20; return View(new ProfileListModel { Profiles = list.Take(20) });
    }
}
