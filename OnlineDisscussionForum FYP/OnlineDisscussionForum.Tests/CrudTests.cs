using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineDisscussionForum.Data;
using OnlineDisscussionForum.Data.Models;
using OnlineDisscussionForum.Service;
using OnlineDisscussionForum.Services;
using Xunit;

namespace OnlineDisscussionForum.Tests;

public class CrudTests : IClassFixture<ForumFactory>
{
    private sealed record PickupMail(string To, string Subject, string HtmlBody);
    private readonly ForumFactory factory;
    public CrudTests(ForumFactory factory) => this.factory = factory;
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
    private async Task<string> Token(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, html);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private async Task<HttpResponseMessage> Post(HttpClient client, string path, string form, Dictionary<string,string> fields)
    {
        fields["__RequestVerificationToken"] = await Token(client, form);
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }
    private async Task<(HttpClient Client, string Id)> Member()
    {
        var client = Client(); var name = "user" + Guid.NewGuid().ToString("N")[..12];
        var response = await Post(client, "/Account/Register", "/Account/Register", new()
        { ["UserName"] = name, ["Email"] = name + "@example.test", ["Password"] = "Member!23456", ["ConfirmPassword"] = "Member!23456" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        return (client, scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Single(u => u.UserName == name).Id);
    }
    private async Task<HttpClient> Admin()
    {
        var client = Client();
        var response = await Post(client, "/Account/Login", "/Account/Login", new() { ["UserName"] = "TestAdmin", ["Password"] = "TestAdmin!23456" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); return client;
    }
    private int Forum()
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var f = new Forum { Title = "Test forum " + Guid.NewGuid(), Description = "Discussion", Created = DateTime.UtcNow, ImageUrl = "/images/forum/default.png" };
        db.Forums.Add(f); db.SaveChanges(); return f.Id;
    }
    [Fact]
    public async Task FreshDatabaseAndAdministratorAreValidAndSeederIsIdempotent()
    {
        using var client = await Admin();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedSuperSeeder();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(db.Users.Where(u => u.UserName == "TestAdmin"));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        foreach (var path in new[] { "/", "/Forum", "/Search/Results", "/Search/Results?searchQuery=", "/health" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        foreach (var path in new[] { "/forum.db", "/keys", "/Post/Index/2147483647", "/Forum/Topic/2147483647", "/Profile/Detail/missing" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }
    [Fact]
    public async Task AnonymousMutationsAndMissingTokensAreRejected()
    {
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.PostAsync("/Post/AddPost", new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        var (member, _) = await Member(); using var client = member;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Post/AddPost", new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Forum/Create")).StatusCode);
    }
    [Fact]
    public async Task PostsAndRepliesHaveSecureCompleteCrudAndAtomicRatings()
    {
        var forumId = Forum(); var (owner, userId) = await Member(); using var client = owner;
        var payload = "<script>alert('test')</script>";
        var response = await Post(client, "/Post/AddPost", "/Post/Create/" + forumId, new() { ["ForumId"] = forumId.ToString(), ["Title"] = "Discussion", ["Content"] = payload });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        int id; string version;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var post = db.Posts.Single(p => p.UserId == userId);
            id = post.Id; version = post.Version; Assert.Equal(1, db.Users.Single(u => u.Id == userId).Rating);
        }
        var html = await client.GetStringAsync("/Post/Index/" + id);
        Assert.Contains("&lt;script&gt;", html); Assert.DoesNotContain(payload, html);
        var (other, _) = await Member(); using var stranger = other;
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync("/Post/Edit/" + id)).StatusCode);
        var token = await Token(stranger, "/");
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync("/Post/Edit/" + id, new FormUrlEncodedContent(new Dictionary<string,string>
        { ["Id"] = id.ToString(), ["Title"] = "Stolen", ["Content"] = "Stolen", ["Version"] = version, ["__RequestVerificationToken"] = token }))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Post/Edit/" + id, "/Post/Edit/" + id, new()
        { ["Id"] = id.ToString(), ["Title"] = "Updated", ["Content"] = "Updated body", ["Version"] = version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(client, "/Post/Edit/" + id, "/Post/Edit/" + id, new()
        { ["Id"] = id.ToString(), ["Title"] = "Stale", ["Content"] = "Stale", ["Version"] = version })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Reply/AddReply", "/Reply/Create/" + id, new() { ["PostId"] = id.ToString(), ["ReplyContent"] = payload })).StatusCode);
        int replyId; string replyVersion;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var reply = db.PostReplies.Single(r => r.PostId == id);
            replyId = reply.Id; replyVersion = reply.Version; Assert.Equal(4, db.Users.Single(u => u.Id == userId).Rating);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync("/Reply/Delete/" + replyId)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Reply/Edit/" + replyId, "/Reply/Edit/" + replyId, new()
        { ["Id"] = replyId.ToString(), ["Content"] = "Updated reply", ["Version"] = replyVersion })).StatusCode);
        using (var scope = factory.Services.CreateScope()) replyVersion = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PostReplies.Single(r => r.Id == replyId).Version;
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Reply/Delete/" + replyId, "/Reply/Delete/" + replyId, new()
        { ["Id"] = replyId.ToString(), ["Version"] = replyVersion })).StatusCode);
        // A remaining reply must be removed with its parent post.
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Reply/AddReply", "/Reply/Create/" + id, new() { ["PostId"] = id.ToString(), ["ReplyContent"] = "Second reply" })).StatusCode);
        using (var scope = factory.Services.CreateScope()) version = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Posts.Single(p => p.Id == id).Version;
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Post/Delete/" + id, "/Post/Delete/" + id, new() { ["Id"] = id.ToString(), ["Version"] = version })).StatusCode);
        using var finalScope = factory.Services.CreateScope(); var finalDb = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(finalDb.Posts.Any(p => p.Id == id)); Assert.False(finalDb.PostReplies.Any(r => r.PostId == id));
        Assert.Equal(7, finalDb.Users.Single(u => u.Id == userId).Rating); // Deletion retains historical contribution points.
    }
    [Fact]
    public async Task ForumCrudRejectsNonemptyDeletionAndInvalidCreation()
    {
        using var client = await Admin();
        var response = await Post(client, "/Forum/AddForum", "/Forum/Create", new() { ["Title"] = " ", ["Description"] = " " });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var name = "Forum" + Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Forum/AddForum", "/Forum/Create", new() { ["Title"] = name, ["Description"] = "Valid" })).StatusCode);
        int id; string version;
        using (var scope = factory.Services.CreateScope()) { var f = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Forums.Single(f => f.Title == name); id = f.Id; version = f.Version; }
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Forum/Edit/" + id, "/Forum/Edit/" + id, new()
        { ["Id"] = id.ToString(), ["Title"] = name, ["Description"] = "Updated", ["Version"] = version })).StatusCode);
        using (var scope = factory.Services.CreateScope()) version = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Forums.Single(f => f.Id == id).Version;
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Post/AddPost", "/Post/Create/" + id, new()
        { ["ForumId"] = id.ToString(), ["Title"] = "Keep", ["Content"] = "Keep" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/Forum/Delete/" + id, "/Forum/Delete/" + id, new() { ["Id"] = id.ToString(), ["Version"] = version })).StatusCode);
        var empty = Forum();
        using (var scope = factory.Services.CreateScope()) version = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Forums.Single(f => f.Id == empty).Version;
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Forum/Delete/" + empty, "/Forum/Delete/" + empty, new() { ["Id"] = empty.ToString(), ["Version"] = version })).StatusCode);
    }
    [Fact]
    public async Task ImageUploadIsUniqueValidatedAndReplacedSafely()
    {
        var (first, firstId) = await Member(); using var owner = first;
        var (second, secondId) = await Member(); using var other = second;
        byte[] png;
        using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(2, 2))
        using (var stream = new MemoryStream()) { await image.SaveAsync(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder()); png = stream.ToArray(); }
        async Task Upload(HttpClient client, string userId, byte[] bytes)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(await Token(client, "/Profile/Detail/" + userId)), "__RequestVerificationToken");
            form.Add(new ByteArrayContent(bytes), "file", "same.png");
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Profile/UploadProfileImage", form)).StatusCode);
        }
        string ImageUrl(string userId)
        { using var scope = factory.Services.CreateScope(); return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Single(u => u.Id == userId).ProfileImageUrl; }
        await Upload(owner, firstId, png); var firstUrl = ImageUrl(firstId);
        await Upload(other, secondId, png); var otherUrl = ImageUrl(secondId);
        Assert.NotEqual(firstUrl, otherUrl);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync(firstUrl)).StatusCode);
        await Upload(owner, firstId, System.Text.Encoding.UTF8.GetBytes("<script>invalid image</script>"));
        Assert.Equal(firstUrl, ImageUrl(firstId));
        await Upload(owner, firstId, Array.Empty<byte>()); Assert.Equal(firstUrl, ImageUrl(firstId));
        await Upload(owner, firstId, new byte[5 * 1024 * 1024 + 1]); Assert.Equal(firstUrl, ImageUrl(firstId));
        await Upload(owner, firstId, png); Assert.NotEqual(firstUrl, ImageUrl(firstId));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(firstUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync(otherUrl)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var uploads = scope.ServiceProvider.GetRequiredService<IUpload>();
        await uploads.DeleteImageAsync("/uploads/../forum.db"); Assert.True(File.Exists(Path.Combine(factory.DataPath, "forum.db")));
        // Oversized decoded dimensions are rejected before full pixel allocation.
        using var largeImage = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(4097, 1);
        using var large = new MemoryStream(); await largeImage.SaveAsync(large, new SixLabors.ImageSharp.Formats.Png.PngEncoder()); large.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => uploads.SaveImageAsync(large, firstId));
    }
    [Fact]
    public async Task AccountUpdateConfirmationResetAndDeletionWork()
    {
        var (member, id) = await Member(); using var client = member;
        string email; string originalName;
        using (var scope = factory.Services.CreateScope()) { var user = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Single(u => u.Id == id); email = user.Email; originalName = user.UserName; }
        var mailbox = Path.Combine(factory.DataPath, "mail");
        var registration = Directory.GetFiles(mailbox).Select(f => System.Text.Json.JsonSerializer.Deserialize<PickupMail>(File.ReadAllText(f))).Single(m => m.To == email && m.Subject.Contains("Confirm"));
        var link = Regex.Match(registration.HtmlBody, "href='([^']+)'").Groups[1].Value;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(WebUtility.HtmlDecode(link))).StatusCode);
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Redirect, (await Post(anonymous, "/Account/ForgotPassword", "/Account/ForgotPassword", new() { ["Email"] = email })).StatusCode);
        var reset = Directory.GetFiles(mailbox).Select(f => System.Text.Json.JsonSerializer.Deserialize<PickupMail>(File.ReadAllText(f))).Single(m => m.To == email && m.Subject == "Reset Password");
        var resetLink = WebUtility.HtmlDecode(Regex.Match(reset.HtmlBody, "href='([^']+)'").Groups[1].Value);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(resetLink).Query);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(anonymous, "/Account/ResetPassword", resetLink, new()
        { ["Email"] = email, ["Code"] = query["code"].ToString(), ["Password"] = "Changed!23456", ["ConfirmPassword"] = "Changed!23456" })).StatusCode);
        // Password reset invalidates prior sign-in; authenticate with the replacement password.
        using var changed = Client();
        Assert.Equal(HttpStatusCode.Redirect, (await Post(changed, "/Account/Login", "/Account/Login", new() { ["UserName"] = originalName, ["Password"] = "Changed!23456" })).StatusCode);
        var renamed = "rename" + Guid.NewGuid().ToString("N")[..10];
        Assert.Equal(HttpStatusCode.Redirect, (await Post(changed, "/Manage/Index", "/Manage/Index", new()
        { ["Username"] = renamed, ["Email"] = email, ["PhoneNumber"] = "+12345678901" })).StatusCode);
        Assert.Contains(renamed, await changed.GetStringAsync("/Manage/Index"));
        var forumId = Forum();
        await Post(changed, "/Post/AddPost", "/Post/Create/" + forumId, new() { ["ForumId"] = forumId.ToString(), ["Title"] = "Retain", ["Content"] = "Retained content" });
        Assert.Equal(HttpStatusCode.OK, (await Post(changed, "/Manage/DeleteAccount", "/Manage/DeleteAccount", new() { ["Password"] = "wrong", ["Confirm"] = "true" })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(changed, "/Manage/DeleteAccount", "/Manage/DeleteAccount", new() { ["Password"] = "Changed!23456", ["Confirm"] = "true" })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await changed.GetAsync("/Manage/Index")).StatusCode);
        using var finalScope = factory.Services.CreateScope(); var db = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var removed = db.Users.Single(u => u.Id == id);
        Assert.False(removed.IsActive); Assert.Null(removed.Email); Assert.Null(removed.PasswordHash); Assert.Null(removed.PhoneNumber);
        Assert.StartsWith("Deleted_", removed.UserName); Assert.True(db.Posts.Any(p => p.UserId == id));
        Assert.False(db.UserRoles.Any(r => r.UserId == id)); Assert.False(db.UserTokens.Any(r => r.UserId == id));
    }
    [Fact]
    public async Task BlankPostsDoNotAwardRatingsAndConcurrentWritesKeepEveryPoint()
    {
        var (member, id) = await Member(); using var client = member; var forumId = Forum();
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/Post/AddPost", "/Post/Create/" + forumId, new()
        { ["ForumId"] = forumId.ToString(), ["Title"] = " ", ["Content"] = " " })).StatusCode);
        using (var scope = factory.Services.CreateScope()) Assert.Equal(0, scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Single(u => u.Id == id).Rating);
        // Each writer gets its own scoped context against the same real SQLite file.
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async n =>
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IPost>().Add(new Post { ForumId = forumId, UserId = id, Title = "Concurrent " + n, Content = "Body", Created = DateTime.UtcNow });
        }));
        using var finalScope = factory.Services.CreateScope(); var db = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(8, db.Posts.Count(p => p.UserId == id)); Assert.Equal(8, db.Users.Single(u => u.Id == id).Rating);
        Assert.Equal("wal", db.Database.SqlQueryRaw<string>("PRAGMA journal_mode").AsEnumerable().Single());
    }
    [Fact]
    public async Task PaginationLimitsResultsAndUnknownTargetsReturnNotFound()
    {
        using var client = await Admin(); var forumId = Forum();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var userId = db.Users.Single(u => u.UserName == "TestAdmin").Id;
            db.Posts.AddRange(Enumerable.Range(0, 25).Select(n => new Post { ForumId = forumId, UserId = userId, Title = "PaginationMarker" + n, Content = "Body", Created = DateTime.UtcNow.AddSeconds(n) }));
            db.SaveChanges();
        }
        var first = await client.GetStringAsync("/Forum/Topic/" + forumId); Assert.Contains("Next", first); Assert.DoesNotContain("PaginationMarker0<", first);
        var second = await client.GetStringAsync("/Forum/Topic/" + forumId + "?page=2"); Assert.Contains("Previous", second);
        foreach (var path in new[] { "/Post/Edit/2147483647", "/Reply/Edit/2147483647", "/Forum/Edit/2147483647", "/Reply/Create/2147483647" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }
    [Fact]
    public async Task LastAdministratorCannotDeleteAccountAndLoginFailuresLockOut()
    {
        using var admin = await Admin();
        Assert.Equal(HttpStatusCode.OK, (await Post(admin, "/Manage/DeleteAccount", "/Manage/DeleteAccount", new()
        { ["Password"] = "TestAdmin!23456", ["Confirm"] = "true" })).StatusCode);
        using (var scope = factory.Services.CreateScope()) Assert.True(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Single(u => u.UserName == "TestAdmin").IsActive);
        var (member, id) = await Member(); using var account = member; string name;
        using (var scope = factory.Services.CreateScope()) name = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Single(u => u.Id == id).UserName;
        using var anonymous = Client();
        for (var attempt = 0; attempt < 5; attempt++)
            await Post(anonymous, "/Account/Login", "/Account/Login", new() { ["UserName"] = name, ["Password"] = "wrong" });
        var response = await Post(anonymous, "/Account/Login", "/Account/Login", new() { ["UserName"] = name, ["Password"] = "Member!23456" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); Assert.Contains("Lockout", response.Headers.Location!.ToString());
    }
    [Fact]
    public async Task StorageFailureDoesNotReplaceExistingProfileImage()
    {
        var (member, id) = await Member(); using var client = member;
        var oldPath = "/images/forum/default.png";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var user = db.Users.Single(u => u.Id == id);
            user.ProfileImageUrl = oldPath; db.SaveChanges();
        }
        var ownerDirectory = Path.Combine(factory.DataPath, "uploads", id);
        await File.WriteAllTextAsync(ownerDirectory, "A file prevents the image directory from being created.");
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(2, 2);
        using var stream = new MemoryStream(); await image.SaveAsync(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(await Token(client, "/Profile/Detail/" + id)), "__RequestVerificationToken");
        form.Add(new ByteArrayContent(stream.ToArray()), "file", "image.png");
        var response = await client.PostAsync("/Profile/UploadProfileImage", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("could not", await client.GetStringAsync("/Profile/Detail/" + id));
        using var finalScope = factory.Services.CreateScope();
        Assert.Equal(oldPath, finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Single(u => u.Id == id).ProfileImageUrl);
        File.Delete(ownerDirectory);
    }
    [Fact]
    public async Task EveryContentMutationChecksTokensAndOtherAuthorsCannotEditOrDeleteReplies()
    {
        var (owner, id) = await Member(); using var author = owner; var forumId = Forum();
        await Post(author, "/Post/AddPost", "/Post/Create/" + forumId, new()
        { ["ForumId"] = forumId.ToString(), ["Title"] = "Permission target", ["Content"] = "Body" });
        int postId; string postVersion;
        using (var scope = factory.Services.CreateScope()) { var post = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Posts.Single(p => p.UserId == id); postId = post.Id; postVersion = post.Version; }
        await Post(author, "/Reply/AddReply", "/Reply/Create/" + postId, new() { ["PostId"] = postId.ToString(), ["ReplyContent"] = "Owner reply" });
        int replyId; string replyVersion;
        using (var scope = factory.Services.CreateScope()) { var reply = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PostReplies.Single(p => p.PostId == postId); replyId = reply.Id; replyVersion = reply.Version; }
        var (other, _) = await Member(); using var stranger = other; var token = await Token(stranger, "/");
        foreach (var action in new[] { "Edit", "Delete" })
        {
            var response = await stranger.PostAsync($"/Reply/{action}/{replyId}", new FormUrlEncodedContent(new Dictionary<string,string>
            { ["Id"] = replyId.ToString(), ["Version"] = replyVersion, ["Content"] = "Unauthorized", ["__RequestVerificationToken"] = token }));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync($"/Post/Delete/{postId}", new FormUrlEncodedContent(new Dictionary<string,string>
        { ["Id"] = postId.ToString(), ["Version"] = postVersion, ["__RequestVerificationToken"] = token }))).StatusCode);
        foreach (var path in new[] { "/Reply/AddReply", $"/Reply/Edit/{replyId}", $"/Reply/Delete/{replyId}", $"/Post/Edit/{postId}", $"/Post/Delete/{postId}", "/Profile/UploadProfileImage", "/Manage/DeleteAccount" })
            Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        using var admin = await Admin();
        foreach (var path in new[] { "/Forum/AddForum", $"/Forum/Edit/{forumId}", $"/Forum/Delete/{forumId}" })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        using var finalScope = factory.Services.CreateScope(); var db = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal("Owner reply", db.PostReplies.Single(r => r.Id == replyId).Content);
        Assert.True(db.Posts.Any(p => p.Id == postId));
    }
    [Fact]
    public async Task InvalidParentRollsBackContentAndRating()
    {
        var (member, id) = await Member(); using var client = member;
        using var scope = factory.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<IPost>();
        await Assert.ThrowsAsync<DbUpdateException>(() => service.Add(new Post
        { UserId = id, ForumId = int.MaxValue, Title = "Invalid parent", Content = "Body", Created = DateTime.UtcNow }));
        using var check = factory.Services.CreateScope(); var db = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(db.Posts.Any(p => p.UserId == id)); Assert.Equal(0, db.Users.Single(u => u.Id == id).Rating);
    }
    [Fact]
    public async Task ProfileImageChangeCannotOverwriteConcurrentRatingChange()
    {
        var (member, id) = await Member(); using var client = member;
        using var profileScope = factory.Services.CreateScope();
        var profiles = profileScope.ServiceProvider.GetRequiredService<IApplicationUser>();
        _ = profiles.GetById(id); // Simulate a profile request reading before another request writes.
        using (var writerScope = factory.Services.CreateScope())
            await writerScope.ServiceProvider.GetRequiredService<IPost>().Add(new Post
            { UserId = id, ForumId = Forum(), Title = "Contribution", Content = "Body", Created = DateTime.UtcNow });
        await profiles.SetProfileImage(id, new Uri("/images/forum/default.png", UriKind.Relative));
        using var check = factory.Services.CreateScope();
        Assert.Equal(1, check.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Single(u => u.Id == id).Rating);
    }
    [Fact]
    public async Task IdentitySettingsChangePreservesConcurrentRatingsAndImages()
    {
        var (member, id) = await Member(); using var client = member;
        using var identityScope = factory.Services.CreateScope();
        var users = identityScope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var staleUser = await users.FindByIdAsync(id);
        using (var writerScope = factory.Services.CreateScope())
        {
            await writerScope.ServiceProvider.GetRequiredService<IPost>().Add(new Post
            { UserId = id, ForumId = Forum(), Title = "Contribution", Content = "Body", Created = DateTime.UtcNow });
            await writerScope.ServiceProvider.GetRequiredService<IApplicationUser>().SetProfileImage(id, new Uri("/images/forum/default.png", UriKind.Relative));
        }
        Assert.True((await users.SetUserNameAsync(staleUser, "identity" + Guid.NewGuid().ToString("N")[..10])).Succeeded);
        using var check = factory.Services.CreateScope();
        var saved = check.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Single(u => u.Id == id);
        Assert.Equal(1, saved.Rating); Assert.Equal("/images/forum/default.png", saved.ProfileImageUrl);
    }
    [Fact]
    public async Task RequiredAuthorsAndVersionsAreEnforcedBySqlite()
    {
        var (member, id) = await Member(); using var client = member; var forumId = Forum();
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IPost>();
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => service.Add(new Post
            { ForumId = forumId, UserId = null, Title = "Missing author", Content = "Body", Created = DateTime.UtcNow }));
            Assert.Contains("NOT NULL constraint failed: Posts.UserId", exception.InnerException!.Message);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<IPost>().Add(new Post
            { ForumId = forumId, UserId = id, Version = null, Title = "Missing version", Content = "Body", Created = DateTime.UtcNow }));
            Assert.Contains("NOT NULL constraint failed: Posts.Version", exception.InnerException!.Message);
        }
        int postId;
        using (var scope = factory.Services.CreateScope())
        {
            var post = new Post { ForumId = forumId, UserId = id, Title = "Valid parent", Content = "Body", Created = DateTime.UtcNow };
            await scope.ServiceProvider.GetRequiredService<IPost>().Add(post); postId = post.Id;
        }
        using (var scope = factory.Services.CreateScope())
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<IPost>().AddReply(new PostReply
            { PostId = postId, UserId = null, Content = "Missing author", Created = DateTime.UtcNow }));
            Assert.Contains("NOT NULL constraint failed: PostReplies.UserId", exception.InnerException!.Message);
        }
        using var check = factory.Services.CreateScope(); var db = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, db.Users.Single(u => u.Id == id).Rating);
        Assert.Single(db.Posts.Where(p => p.UserId == id)); Assert.False(db.PostReplies.Any(r => r.PostId == postId));
    }
}
