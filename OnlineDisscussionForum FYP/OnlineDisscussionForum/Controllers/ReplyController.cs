using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OnlineDisscussionForum.Data;
using OnlineDisscussionForum.Data.Models;
using OnlineDisscussionForum.Models;
using OnlineDisscussionForum.Models.Reply;
namespace OnlineDisscussionForum.Controllers;
[Authorize]
public class ReplyController(IPost posts, UserManager<ApplicationUser> users) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Create(int id)
    {
        var post = posts.GetById(id); if (post == null) return NotFound();
        var user = await users.GetUserAsync(User); if (user == null) return Challenge();
        var model = new PostReplyModel { PostId = id }; Fill(model, post, user); return View(model);
    }
    [HttpPost]
    public async Task<IActionResult> AddReply(PostReplyModel model)
    {
        var post = posts.GetById(model.PostId); if (post == null) return NotFound();
        var user = await users.GetUserAsync(User); if (user == null) return Challenge();
        if (!ModelState.IsValid) { Fill(model, post, user); return View("Create", model); }
        await posts.AddReply(new PostReply { PostId = post.Id, UserId = user.Id, Content = model.ReplyContent.Trim(), Created = DateTime.UtcNow });
        return RedirectToAction("Index", "Post", new { id = post.Id });
    }
    [HttpGet]
    public IActionResult Edit(int id)
    {
        var reply = posts.GetReplyById(id); if (reply == null) return NotFound(); if (!CanManage(reply.UserId)) return Forbid();
        return View(new ReplyEditModel { Id = id, Content = reply.Content, Version = reply.Version });
    }
    [HttpPost]
    public async Task<IActionResult> Edit(int id, ReplyEditModel model)
    {
        var reply = posts.GetReplyById(id); if (reply == null) return NotFound(); if (!CanManage(reply.UserId)) return Forbid();
        if (id != model.Id) return BadRequest(); if (!ModelState.IsValid) return View(model);
        if (!await posts.UpdateReply(id, model.Content, model.Version)) return Conflict("This reply changed or was deleted. Reload before editing.");
        return RedirectToAction("Index", "Post", new { id = reply.PostId });
    }
    [HttpGet]
    public IActionResult Delete(int id)
    {
        var reply = posts.GetReplyById(id); if (reply == null) return NotFound(); if (!CanManage(reply.UserId)) return Forbid();
        return View(new DeleteContentModel { Id = id, Title = "Reply to " + reply.Post.Title, Version = reply.Version });
    }
    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id, DeleteContentModel model)
    {
        var reply = posts.GetReplyById(id); if (reply == null) return NotFound(); if (!CanManage(reply.UserId)) return Forbid();
        if (id != model.Id || !ModelState.IsValid) return BadRequest();
        if (!await posts.DeleteReply(id, model.Version)) return Conflict("This reply changed or was deleted. Reload before deleting.");
        return RedirectToAction("Index", "Post", new { id = reply.PostId });
    }
    private bool CanManage(string authorId) => users.GetUserId(User) == authorId || User.IsInRole("Admin");
    private void Fill(PostReplyModel model, Post post, ApplicationUser user)
    {
        model.PostTitle = post.Title; model.PostContent = post.Content; model.ForumId = post.ForumId; model.ForumName = post.Forum.Title;
        model.ForumImageUrl = post.Forum.ImageUrl; model.AuthorName = user.UserName; model.AuthorId = user.Id;
    }
}
