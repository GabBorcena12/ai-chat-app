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
        public DbSet<ChatConversationEntity> ChatConversations { get; set; }
        public DbSet<ChatResponseReportEntity> ChatResponseReports { get; set; }
        public DbSet<AssistantPromptTemplateEntity> AssistantPromptTemplates { get; set; }
        public DbSet<AssistantKnowledgeEntryEntity> AssistantKnowledgeEntries { get; set; }
        public DbSet<CoreDataFileEntity> CoreDataFiles { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<CoreDataFileEntity>()
                .HasIndex(x => x.RelativePath)
                .IsUnique();

            builder.Entity<CoreDataFileEntity>()
                .HasIndex(x => x.ContentKey);

            builder.Entity<ChatConversationEntity>()
                .Property(x => x.ChatId)
                .HasMaxLength(128);

            builder.Entity<ChatConversationEntity>()
                .Property(x => x.UserId)
                .HasMaxLength(450);

            builder.Entity<ChatConversationEntity>()
                .Property(x => x.Username)
                .HasMaxLength(256);

            builder.Entity<ChatConversationEntity>()
                .HasIndex(x => new { x.UserId, x.ChatId })
                .IsUnique();

            builder.Entity<ChatMessageEntity>()
                .Property(x => x.ChatId)
                .HasMaxLength(128);

            builder.Entity<ChatMessageEntity>()
                .Property(x => x.UserId)
                .HasMaxLength(450);

            builder.Entity<ChatMessageEntity>()
                .Property(x => x.MessageId)
                .HasMaxLength(450);

            builder.Entity<ChatMessageEntity>()
                .Property(x => x.Username)
                .HasMaxLength(256);

            builder.Entity<ChatMessageEntity>()
                .HasIndex(x => new { x.UserId, x.ChatId });

            builder.Entity<ChatMessageEntity>()
                .HasIndex(x => x.MessageId);
        }

        // -- USAGE --
        // cd AIChatApp.API 
        // dotnet ef migrations add InitialCreate -c AppDbContext
        // dotnet ef database update -c AppDbContext
    }
}
