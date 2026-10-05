using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineDisscussionForum.Data;
using OnlineDisscussionForum.Data.Models;
using OnlineDisscussionForum.Models;
using OnlineDisscussionForum.Models.Forum;
using OnlineDisscussionForum.Models.Post;
namespace OnlineDisscussionForum.Controllers;
public class ForumController(IForum forums, IPost posts, IUpload uploads, ApplicationDbContext db) : Controller
{
    public IActionResult Index(int page = 1)
    {
        page = Math.Max(1, page); ViewBag.Page = page;
        var cutoff = DateTime.UtcNow.AddHours(-12);
        var list = forums.GetAll().OrderBy(f => f.Title).Skip((page - 1) * 20).Take(21).Select(f => new ForumListingModel
        {
            Id = f.Id, Name = f.Title, Description = f.Description, ImageUrl = f.ImageUrl,
            NumberOfPosts = f.Posts.Count,
            NumberOfUsers = db.Users.Count(u => db.Posts.Any(p => p.ForumId == f.Id && p.UserId == u.Id) || db.PostReplies.Any(r => r.Post.ForumId == f.Id && r.UserId == u.Id)),
            HasRecentPost = f.Posts.Any(p => p.Created > cutoff)
        }).ToList();
        ViewBag.HasNext = list.Count > 20;
        return View(new ForumIndexModel { ForumList = list.Take(20) });
    }
    [HttpPost] public IActionResult Search(int id, string searchQuery) => RedirectToAction(nameof(Topic), new { id, searchQuery });
    [Authorize(Roles = "Admin"), HttpGet] public IActionResult Create() => View(new AddForumModel());
    [Authorize(Roles = "Admin"), HttpPost]
    public async Task<IActionResult> AddForum(AddForumModel model)
    {
        if (!ModelState.IsValid) return View("Create", model);
        string image = null;
        try
        {
            image = await Upload(model.ImageUpload);
            await forums.Create(new Forum { Title = model.Title.Trim(), Description = model.Description.Trim(), ImageUrl = image ?? "/images/forum/default.png", Created = DateTime.UtcNow });
        }
        catch (InvalidDataException ex) { ModelState.AddModelError("ImageUpload", ex.Message); return View("Create", model); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { await uploads.DeleteImageAsync(image); ModelState.AddModelError("ImageUpload", "The image could not be saved. Try again later."); return View("Create", model); }
        catch { await uploads.DeleteImageAsync(image); throw; }
        return RedirectToAction(nameof(Index));
    }
    public IActionResult Topic(int id, string searchQuery, int page = 1)
    {
        var forum = forums.GetById(id); if (forum == null) return NotFound();
        page = Math.Max(1, page); ViewBag.Page = page; ViewBag.SearchQuery = searchQuery;
        var list = posts.GetFilteredPosts(forum, searchQuery).OrderByDescending(p => p.Created).ThenByDescending(p => p.Id)
            .Skip((page - 1) * 20).Take(21).Select(p => new PostListingModel
            {
                Id = p.Id, AuthorId = p.UserId, AuthorName = p.User.UserName, AuthorRating = p.User.Rating,
                Title = p.Title, DatePosted = p.Created.ToString(), RepliesCount = p.Replies.Count,
                Forum = new ForumListingModel { Id = p.ForumId, Name = p.Forum.Title, ImageUrl = p.Forum.ImageUrl }
            }).ToList();
        ViewBag.HasNext = list.Count > 20;
        return View(new ForumTopicModel { Forum = Listing(forum), Posts = list.Take(20) });
    }
    [Authorize(Roles = "Admin"), HttpGet]
    public IActionResult Edit(int id)
    {
        var forum = forums.GetById(id); if (forum == null) return NotFound();
        return View(new EditForumModel { Id = id, Title = forum.Title, Description = forum.Description, Version = forum.Version });
    }
    [Authorize(Roles = "Admin"), HttpPost]
    public async Task<IActionResult> Edit(int id, EditForumModel model)
    {
        var forum = forums.GetById(id); if (forum == null) return NotFound();
        if (model.Id != id) return BadRequest(); if (!ModelState.IsValid) return View(model);
        string image = null;
        try
        {
            image = await Upload(model.ImageUpload);
            if (!await forums.Update(id, model.Title, model.Description, image ?? forum.ImageUrl, model.Version))
            { await uploads.DeleteImageAsync(image); return Conflict("This forum changed or was deleted. Reload before editing."); }
        }
        catch (InvalidDataException ex) { ModelState.AddModelError("ImageUpload", ex.Message); return View(model); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { await uploads.DeleteImageAsync(image); ModelState.AddModelError("ImageUpload", "The image could not be saved. Try again later."); return View(model); }
        catch { await uploads.DeleteImageAsync(image); throw; }
        if (image != null) await uploads.DeleteImageAsync(forum.ImageUrl);
        return RedirectToAction(nameof(Topic), new { id });
    }
    [Authorize(Roles = "Admin"), HttpGet]
    public IActionResult Delete(int id)
    {
        var forum = forums.GetById(id); if (forum == null) return NotFound();
        return View(new DeleteContentModel { Id = id, Title = forum.Title, Version = forum.Version });
    }
    [Authorize(Roles = "Admin"), HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id, DeleteContentModel model)
    {
        var forum = forums.GetById(id); if (forum == null) return NotFound();
        if (id != model.Id || !ModelState.IsValid) return BadRequest();
        if (!await forums.Delete(id, model.Version)) { ModelState.AddModelError("", "This forum contains posts or has changed. Only unchanged empty forums can be deleted."); return View("Delete", model); }
        await uploads.DeleteImageAsync(forum.ImageUrl); return RedirectToAction(nameof(Index));
    }
    private async Task<string> Upload(IFormFile file)
    {
        if (file == null) return null;
        using var stream = file.OpenReadStream(); return await uploads.SaveImageAsync(stream, "forums");
    }
    private static ForumListingModel Listing(Forum f) => new() { Id = f.Id, Name = f.Title, Description = f.Description, ImageUrl = f.ImageUrl };
}
