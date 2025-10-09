using Microsoft.EntityFrameworkCore;
using CvAssistantWeb.Models;

namespace CvAssistantWeb.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<ContactMessage> Messages { get; set; }
    }
}