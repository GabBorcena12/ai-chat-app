using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace AIChatApp.Core.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<ChatMessageEntity> ChatMessages { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) { }
        public DbSet<ChatMessageEntity> ChatMessagesTbl { get; set; }
    }
}
