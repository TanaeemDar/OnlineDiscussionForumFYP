using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OnlineDisscussionForum.Data;
using OnlineDisscussionForum.Data.Models;
using OnlineDisscussionForum.Models;
using OnlineDisscussionForum.Models.Post;
using OnlineDisscussionForum.Models.Reply;
namespace OnlineDisscussionForum.Controllers;

public class PostController(IPost posts, IForum forums, UserManager<ApplicationUser> users) : Controller
{
    public async Task<IActionResult> Index(int id)
    {
        var post = posts.GetById(id);
        if (post == null) return NotFound();
        var admins = (await users.GetUsersInRoleAsync("Admin")).Select(u => u.Id).ToHashSet();
        return View(new PostIndexModel
        {
            Id = post.Id, Title = post.Title, AuthorId = post.UserId, AuthorName = post.User.UserName,
            AuthorImageUrl = post.User.ProfileImageUrl, AuthorRating = post.User.Rating, Created = post.Created,
            PostContent = post.Content, ForumId = post.ForumId, ForumName = post.Forum.Title,
            IsAuthorAdmin = admins.Contains(post.UserId), Replies = post.Replies.OrderBy(r => r.Created).ThenBy(r => r.Id).Select(r => new PostReplyModel
            {
                Id = r.Id, PostId = id, AuthorId = r.UserId, AuthorName = r.User.UserName, AuthorImageUrl = r.User.ProfileImageUrl,
                AuthorRating = r.User.Rating, Created = r.Created, ReplyContent = r.Content, IsAuthorAdmin = admins.Contains(r.UserId)
            }).ToList()
        });
    }
    [Authorize, HttpGet]
    public IActionResult Create(int id)
    {
        var forum = forums.GetById(id);
        if (forum == null) return NotFound();
        var model = new NewPostModel { ForumId = id }; Fill(model, forum); return View(model);
    }
    [Authorize, HttpPost]
    public async Task<IActionResult> AddPost(NewPostModel model)
    {
        var forum = forums.GetById(model.ForumId);
        if (forum == null) return NotFound();
        if (!ModelState.IsValid) { Fill(model, forum); return View("Create", model); }
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        var post = new Post { Title = model.Title.Trim(), Content = model.Content.Trim(), Created = DateTime.UtcNow, UserId = user.Id, ForumId = forum.Id };
        await posts.Add(post);
        return RedirectToAction(nameof(Index), new { id = post.Id });
    }
    [Authorize, HttpGet]
    public IActionResult Edit(int id)
    {
        var post = posts.GetById(id); if (post == null) return NotFound(); if (!CanManage(post.UserId)) return Forbid();
        return View(new ContentEditModel { Id = id, Title = post.Title, Content = post.Content, Version = post.Version });
    }
    [Authorize, HttpPost]
    public async Task<IActionResult> Edit(int id, ContentEditModel model)
    {
        var post = posts.GetById(id); if (post == null) return NotFound(); if (!CanManage(post.UserId)) return Forbid();
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid) return View(model);
        if (!await posts.Update(id, model.Title, model.Content, model.Version)) return Conflict("This post changed or was deleted. Reload before editing.");
        return RedirectToAction(nameof(Index), new { id });
    }
    [Authorize, HttpGet]
    public IActionResult Delete(int id)
    {
        var post = posts.GetById(id); if (post == null) return NotFound(); if (!CanManage(post.UserId)) return Forbid();
        return View(new DeleteContentModel { Id = id, Title = post.Title, Version = post.Version });
    }
    [Authorize, HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id, DeleteContentModel model)
    {
        var post = posts.GetById(id); if (post == null) return NotFound(); if (!CanManage(post.UserId)) return Forbid();
        if (id != model.Id || !ModelState.IsValid) return BadRequest();
        if (!await posts.Delete(id, model.Version)) return Conflict("This post changed or was deleted. Reload before deleting.");
        return RedirectToAction("Topic", "Forum", new { id = post.ForumId });
    }
    private bool CanManage(string authorId) => users.GetUserId(User) == authorId || User.IsInRole("Admin");
    private void Fill(NewPostModel model, Forum forum)
    { model.ForumName = forum.Title; model.ForumImageUrl = forum.ImageUrl; model.AuthorName = User.Identity.Name; }
}
