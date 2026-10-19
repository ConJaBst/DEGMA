using DEGMA.Binders;
using DEGMA.Data;
using DEGMA.Models;
using DEGMA.Services;
using DinkToPdf;
using DinkToPdf.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ================================================================
// DATABASE
// ================================================================
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=degma.db"));

// ================================================================
// IDENTITY & AUTHENTICATION
// ================================================================
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

// ================================================================
// SETTINGS (appsettings.json sections)
// ================================================================
builder.Services.Configure<ClinicSettings>(
    builder.Configuration.GetSection("ClinicSettings"));

builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings"));

builder.Services.Configure<BackupSettings>(
    builder.Configuration.GetSection("BackupSettings"));

// ================================================================
// APPLICATION SERVICES
// ================================================================

// PDF generation
builder.Services.AddSingleton(typeof(IConverter),
    new SynchronizedConverter(new PdfTools()));

// Email
builder.Services.AddTransient<EmailService>();

// Database backups — daily scheduled run plus manual trigger
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService<BackupScheduler>();

// Session (used by CSV import)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ================================================================
// MVC + MODEL BINDERS
// ================================================================
builder.Services.AddControllersWithViews(options =>
{
    // Invariant culture binders so decimal/double fields
    // parse correctly regardless of system locale (e.g. South Africa)
    options.ModelBinderProviders.Insert(0, new InvariantDecimalModelBinderProvider());
    options.ModelBinderProviders.Insert(1, new InvariantDoubleModelBinderProvider());
});

// Allow large CSV uploads through the form pipeline
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.ValueCountLimit = 10000;
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = int.MaxValue;
});

// ================================================================
// BUILD
// ================================================================
var app = builder.Build();

// ================================================================
// DATABASE STARTUP — migrate and seed
// ================================================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    // 1. Apply any pending EF migrations (creates tables if missing)
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred applying database migrations.");
    }

    // 2. Seed roles
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Doctor", "Receptionist" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    // 3. Seed default user accounts
    var userManager = services.GetRequiredService<UserManager<IdentityUser>>();

    if (await userManager.FindByEmailAsync("doctor@degma.com") == null)
    {
        var doctor = new IdentityUser
        {
            UserName = "doctor@degma.com",
            Email = "doctor@degma.com"
        };
        await userManager.CreateAsync(doctor, "Doctor123");
        await userManager.AddToRoleAsync(doctor, "Doctor");
    }

    if (await userManager.FindByEmailAsync("receptionist@degma.com") == null)
    {
        var receptionist = new IdentityUser
        {
            UserName = "receptionist@degma.com",
            Email = "receptionist@degma.com"
        };
        await userManager.CreateAsync(receptionist, "Receptionist123");
        await userManager.AddToRoleAsync(receptionist, "Receptionist");
    }

    // 3b. Seed the standard side effect dictionary (adds missing names only)
    await SideEffectDictionary.SeedAsync(services.GetRequiredService<ApplicationDbContext>());

    // 4. Backfill batch number on any reports that don't have one
    //    Safe to leave permanently — only touches null/empty records
    var db = services.GetRequiredService<ApplicationDbContext>();
    var reportsWithoutBatch = await db.Reports
        .Where(r => r.BatchNumber == null || r.BatchNumber == "")
        .ToListAsync();

    if (reportsWithoutBatch.Any())
    {
        foreach (var report in reportsWithoutBatch)
            report.BatchNumber = "4YIS2501A";

        await db.SaveChangesAsync();
    }
}

// ================================================================
// HTTP PIPELINE
// ================================================================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();