using DEGMA.Models;
using Microsoft.EntityFrameworkCore;

namespace DEGMA.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Patient> Patients { get; set; }
        public DbSet<Report> Reports { get; set; }
        public DbSet<SideEffect> SideEffects { get; set; }
    }
}