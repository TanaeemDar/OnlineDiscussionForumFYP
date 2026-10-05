using Microsoft.AspNetCore.Mvc;
using OnlineDisscussionForum.Data;
using OnlineDisscussionForum.Models.Search;
using OnlineDisscussionForum.Models.Post;
using OnlineDisscussionForum.Models.Forum;
namespace OnlineDisscussionForum.Controllers;
public class SearchController(IPost posts) : Controller
{
    public IActionResult Results(string searchQuery, int page = 1)
    {
        page = Math.Max(1, page); ViewBag.Page = page; ViewBag.SearchQuery = searchQuery;
        var list = posts.GetFilteredPosts(searchQuery).OrderByDescending(p => p.Created).ThenByDescending(p => p.Id)
            .Skip((page - 1) * 20).Take(21).Select(p => new PostListingModel
            {
                Id = p.Id, Title = p.Title, AuthorId = p.UserId, AuthorName = p.User.UserName, AuthorRating = p.User.Rating,
                DatePosted = p.Created.ToString(), RepliesCount = p.Replies.Count,
                Forum = new ForumListingModel { Id = p.ForumId, Name = p.Forum.Title, ImageUrl = p.Forum.ImageUrl }
            }).ToList();
        ViewBag.HasNext = list.Count > 20;
        return View(new SearchResultModel { SearchQuery = searchQuery, Posts = list.Take(20), EmptySearchResults = !string.IsNullOrWhiteSpace(searchQuery) && list.Count == 0 });
    }
    [HttpPost] public IActionResult Search(string searchQuery) => RedirectToAction(nameof(Results), new { searchQuery });
}
