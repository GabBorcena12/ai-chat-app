using System.Collections.Generic;
using AIChatApp.Core.Data_Context.Entity;
using Microsoft.EntityFrameworkCore;

namespace AIChatApp.Core.Data_Context
{
    public class AppDbContext : DbContext
    {
        public DbSet<ChatMessageEntity> ChatMessages { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) { }
        public DbSet<ChatMessageEntity> ChatMessagesTbl { get; set; }

        // -- USAGE --
        // dotnet ef migrations add InitialCreate -c AppDbContext
        // dotnet ef database update -c AppDbContext
    }
}
