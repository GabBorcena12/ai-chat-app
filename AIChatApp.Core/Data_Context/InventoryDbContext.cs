using AIChatApp.Core.Data_Context.Entity;
using Microsoft.EntityFrameworkCore;

namespace AIChatApp.Core.Data_Context
{
    public class InventoryDbContext : DbContext
    {
        public DbSet<ChatMessageEntity> ChatMessages { get; set; }

        public InventoryDbContext(DbContextOptions<InventoryDbContext> options)
            : base(options) { }
        public DbSet<ProductEntity> Products { get; set; }
        
    }
}
