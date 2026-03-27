using AIChatApp.Core.Config;
using AIChatApp.Core.Data_Context;
using Azure.Core;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace AIChatApp.API.Services.Prompting
{
    public class PromptBuilder : IPromptBuilder
    {
        private readonly AppDbContext _db;
        private readonly InventoryDbContext _inventorydb;
        private readonly ChatPaths _paths;

        public PromptBuilder(AppDbContext db, InventoryDbContext inventorydb)
        {
            _db = db;
            _inventorydb = inventorydb;
            _paths = new ChatPaths();
        }

        // TODO : CREATE A FAQ QUESTION 
        // TODO : REFACTOR QUERY ON Products Table
        public async Task<string> RebuildPromptWithIncompleteResponseAsync(
            string chatId,
            string user,
            string message,
            string incompleteResponse)
        {
            var sb = new StringBuilder();

            // Keep the system instruction only
            sb.AppendLine($"System: {_paths.LoadApiSystemContext()}");
            sb.AppendLine();

            // Include only last 2-3 messages (instead of 20)
            var history = await _db.ChatMessagesTbl
                .Where(x => x.ChatId == chatId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(3)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

            foreach (var msg in history)
            {
                sb.AppendLine($"{msg.Role}: {msg.Content}");
            }

            // Original incomplete message
            sb.AppendLine("You must FIX the response below.");
            sb.AppendLine();
            sb.AppendLine("Rules:");
            sb.AppendLine("- Remove repetition");
            sb.AppendLine("- Maximum 2 sentences");
            sb.AppendLine("- If unclear or no product, say: 'No pigeon pellet products available.'");
            sb.AppendLine("- Do NOT add extra explanations");
            sb.AppendLine("- Do NOT repeat phrases");
            sb.AppendLine();
            sb.AppendLine($"Response: {incompleteResponse}");
            sb.AppendLine("Answer:");
            return sb.ToString();
        }

        public async Task<string> BuildPromptAsync(string chatId, string user, string message)
        {
            var sb = new StringBuilder();
            var history = await _db.ChatMessagesTbl
                .Where(x => x.ChatId == chatId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(20)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

            // Relevant Knowledge (RAG)
            var matches = await _inventorydb.Products
                .FromSqlRaw("SELECT TOP 5 * FROM Products WHERE ProductName LIKE '%' + {0} + '%'", message)
                .ToListAsync();

            // System Context
            sb.AppendLine($"System: {_paths.LoadApiSystemContext()}");
            sb.AppendLine();

            // RAG
            if (matches.Any())
            {
                sb.AppendLine("Relevant Knowledge:");
                foreach (var doc in matches)
                {
                    sb.AppendLine(doc.MasterSku + " - "+ doc.ProductName + " - " + doc.ProductAlias);
                }
                sb.AppendLine();
            }

            // Chat History (Memory)
            foreach (var msg in history)
            {
                sb.AppendLine($"{msg.Role}: {msg.Content}");
            }

            sb.AppendLine($"User: {message}");
            sb.AppendLine("AI Assistant:");

            return sb.ToString();
        }
    }
}
