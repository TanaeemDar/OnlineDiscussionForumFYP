using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using OnlineDisscussionForum.Data;
using OnlineDisscussionForum.Data.Models;
using OnlineDisscussionForum.Service;
using OnlineDisscussionForum.Services;

namespace OnlineDisscussionForum;

public class Startup
{
    public IConfiguration Configuration { get; }
    private readonly IWebHostEnvironment _environment;
    public Startup(IConfiguration configuration, IWebHostEnvironment environment) { Configuration = configuration; _environment = environment; }

    public void ConfigureServices(IServiceCollection services)
    {
        var dataPath = Path.GetFullPath(Configuration["DataDirectory"] ?? "../../.forum-data");
        var webRoot = Path.GetFullPath("wwwroot");
        if (dataPath == webRoot || dataPath.StartsWith(webRoot + Path.DirectorySeparatorChar))
            throw new InvalidOperationException("DataDirectory must be outside wwwroot.");
        Directory.CreateDirectory(dataPath);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(dataPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.CreateDirectory(Path.Combine(dataPath, "uploads"));
        services.AddSingleton(new LocalDataPaths(dataPath));
        var connection = new SqliteConnectionStringBuilder(Configuration.GetConnectionString("DefaultConnection") ??
            $"Data Source={Path.Combine(dataPath, "forum.db")}") { ForeignKeys = true, DefaultTimeout = 30 };
        if (!Path.IsPathFullyQualified(connection.DataSource))
            connection.DataSource = Path.GetFullPath(connection.DataSource);
        if (connection.DataSource.StartsWith(webRoot + Path.DirectorySeparatorChar))
            throw new InvalidOperationException("Database must be outside wwwroot.");
        Directory.CreateDirectory(Path.GetDirectoryName(connection.DataSource)!);
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection.ToString(),
            sqlite => sqlite.MigrationsAssembly(typeof(Startup).Assembly.FullName)));
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.User.RequireUniqueEmail = true;
        }).AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders().AddSignInManager<ActiveSignInManager>();
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return context.Response.WriteAsync("You do not have permission to perform this action.");
            };
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = _environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Events.OnValidatePrincipal = async context =>
            {
                await SecurityStampValidator.ValidatePrincipalAsync(context);
                if (context.Principal == null) return;
                var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await users.GetUserAsync(context.Principal);
                if (user == null || !user.IsActive)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                }
            };
        });
        services.AddDataProtection().SetApplicationName("OnlineDiscussionForum")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")));
        services.AddScoped<DataSeeder>();
        services.AddScoped<IForum, ForumService>();
        services.AddScoped<IPost, PostService>();
        services.AddScoped<IApplicationUser, ApplicationUserService>();
        services.AddScoped<IUpload, LocalUploadService>();
        services.AddTransient<IEmailSender, EmailSender>();
        services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
        services.AddHealthChecks().AddCheck<SqliteHealthCheck>("sqlite");
        services.Configure<ForwardedHeadersOptions>(options => options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, LocalDataPaths paths)
    {
        app.UseForwardedHeaders();
        if (env.IsDevelopment()) app.UseDeveloperExceptionPage();
        else { app.UseExceptionHandler("/Home/Error"); app.UseHsts(); }
        app.UseStaticFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(paths.Root, "uploads")),
            RequestPath = "/uploads",
            OnPrepareResponse = ctx => ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff"
        });
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapHealthChecks("/health");
            endpoints.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        });
    }
}
