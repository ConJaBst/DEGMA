using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using DEGMA.Models;

namespace DEGMA.Data
{
    public class ApplicationDbContext : IdentityDbContext<IdentityUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<Patient> Patients { get; set; }
        public DbSet<Report> Reports { get; set; }
        public DbSet<SideEffect> SideEffects { get; set; }
    }
}