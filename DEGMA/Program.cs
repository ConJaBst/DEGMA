using DEGMA.Data;
using DinkToPdf;
using DinkToPdf.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=degma.db"));
builder.Services.AddSingleton(typeof(IConverter), 
    new SynchronizedConverter(new PdfTools()));

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Auth/Login";
    options.AccessDeniedPath = "/Auth/AccessDenied";
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options => {
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.ValueCountLimit = 10000; // Increase limit to handle large CSVs
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = int.MaxValue;
}); 

// Add services to the container.
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Seed roles and default users
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

    // Create roles
    foreach (var role in new[] { "Doctor", "Receptionist" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    // Default Doctor account
    if (await userManager.FindByEmailAsync("doctor@degma.com") == null)
    {
        var doctor = new IdentityUser { UserName = "doctor@degma.com", Email = "doctor@degma.com" };
        await userManager.CreateAsync(doctor, "Doctor123");
        await userManager.AddToRoleAsync(doctor, "Doctor");
    }

    // Default Receptionist account
    if (await userManager.FindByEmailAsync("receptionist@degma.com") == null)
    {
        var receptionist = new IdentityUser { UserName = "receptionist@degma.com", Email = "receptionist@degma.com" };
        await userManager.CreateAsync(receptionist, "Receptionist123");
        await userManager.AddToRoleAsync(receptionist, "Receptionist");
    }
}

// Make sure this line is present
app.UseAuthentication();
app.UseAuthorization();

// --- AUTO-MIGRATE DATABASE START ---
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        // This line physically creates the database and tables if they are missing
        context.Database.Migrate();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred creating the DB.");
    }
}
// --- AUTO-MIGRATE DATABASE END ---

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.UseStaticFiles();

app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate(); // creates tables if they don't exist
}

app.Run();
