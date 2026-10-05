using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OnlineDisscussionForum.Data.Models;

namespace OnlineDisscussionForum.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<ApplicationUser> ApplicationUsers { get; set; }
    public DbSet<Forum> Forums { get; set; }
    public DbSet<Post> Posts { get; set; }
    public DbSet<PostReply> PostReplies { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Forum>().Property(f => f.Title).IsRequired().HasMaxLength(150);
        builder.Entity<Forum>().Property(f => f.Description).IsRequired().HasMaxLength(2000);
        builder.Entity<Post>().Property(p => p.Title).IsRequired().HasMaxLength(200);
        builder.Entity<Post>().Property(p => p.Content).IsRequired().HasMaxLength(20000);
        builder.Entity<PostReply>().Property(r => r.Content).IsRequired().HasMaxLength(20000);
        builder.Entity<Post>().HasOne(p => p.Forum).WithMany(f => f.Posts).HasForeignKey(p => p.ForumId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Post>().HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PostReply>().HasOne(r => r.Post).WithMany(p => p.Replies).HasForeignKey(r => r.PostId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<PostReply>().HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Post>().Property(p => p.Version).IsRequired().IsConcurrencyToken();
        builder.Entity<PostReply>().Property(r => r.Version).IsRequired().IsConcurrencyToken();
        builder.Entity<Forum>().Property(f => f.Version).IsRequired().IsConcurrencyToken();
        builder.Entity<Post>().HasIndex(p => new { p.ForumId, p.Created });
    }

    // Identity's user store marks the entire entity modified. Preserve custom columns
    // that the Identity operation did not change, including concurrent rating/image updates.
    private void PreserveUnchangedUserFields()
    {
        foreach (var entry in ChangeTracker.Entries<ApplicationUser>().Where(e => e.State == EntityState.Modified))
            foreach (var name in new[] { nameof(ApplicationUser.Rating), nameof(ApplicationUser.ProfileImageUrl), nameof(ApplicationUser.MemberSince), nameof(ApplicationUser.IsActive) })
            {
                var property = entry.Property(name);
                if (Equals(property.OriginalValue, property.CurrentValue)) property.IsModified = false;
            }
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PreserveUnchangedUserFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PreserveUnchangedUserFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
