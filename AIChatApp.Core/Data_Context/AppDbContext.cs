using System.Collections.Generic;
using AIChatApp.Core.Data_Context.Entity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AIChatApp.Core.Data_Context
{
    public class AppDbContext : IdentityDbContext<ApplicationUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }
        public DbSet<ChatMessageEntity> ChatMessages { get; set; }

        public DbSet<ChatMessageEntity> ChatMessagesTbl { get; set; }

        // -- USAGE --
        // cd AIChatApp.API 
        // dotnet ef migrations add InitialCreate -c AppDbContext
        // dotnet ef database update -c AppDbContext
    }
}
