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

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Vitals are optional, and reports recorded before they were tracked have none.
            builder.Entity<Report>().Property(r => r.Pulse).IsRequired(false);
            builder.Entity<Report>().Property(r => r.SystolicBp).IsRequired(false);
            builder.Entity<Report>().Property(r => r.DiastolicBp).IsRequired(false);
        }
    }
}