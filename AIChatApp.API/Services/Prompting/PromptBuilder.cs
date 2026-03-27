using AIChatApp.Core.Config;
using AIChatApp.Core.Data_Context;
using AIChatApp.Core.Data_Context.Entity;
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
        private IConfiguration _config;
        private readonly string _keyword;

        public PromptBuilder(AppDbContext db, InventoryDbContext inventorydb, IConfiguration configuration)
        {
            _db = db;
            _inventorydb = inventorydb;
            _paths = new ChatPaths();
            _config = configuration;
            _keyword = _config.GetValue<string>("ApiSettings:Prompting.Keyword") ?? string.Empty;
        }

        public async Task<string> RebuildPromptWithIncompleteResponseAsync(
            string chatId,
            string user,
            string message,
            string incompleteResponse)
        {
            var sb = new StringBuilder();

            // System Context
            sb.AppendLine($"System: {_paths.LoadApiSystemContext()}");
            sb.AppendLine();

            // Chat History (Memory) - last 10 messages for context
            var history = await _db.ChatMessagesTbl
                .Where(x => x.ChatId == chatId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(10)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

            foreach (var msg in history)
            {
                sb.AppendLine($"{msg.Role}: {msg.Content}");
            }

            // Instructions to fix the incomplete response
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

            // System Context
            sb.AppendLine($"System: {_paths.LoadApiSystemContext()}");
            sb.AppendLine();

            // RAG or Relevant Knowledge (Keyword Matching)
            var matches = await GetKeywordFromMessage(message);
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

        private async Task<List<ProductEntity>> GetKeywordFromMessage(string message)
        {
            if(string.IsNullOrEmpty(_keyword))
                return new List<ProductEntity>();

            var matchedKeywords = _keyword
                .Where(k => message.Contains(k, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchedKeywords.Any())
            {
                var likeClauses = string.Join(" OR ",
                    matchedKeywords.Select((k, i) => $"ProductName LIKE '%' + {{{i}}} + '%'"));

                var sql = $"SELECT TOP 5 * FROM Products WHERE {likeClauses}";

                var matches = await _inventorydb.Products
                    .FromSqlRaw(sql, matchedKeywords.Cast<object>().ToArray())
                    .ToListAsync();

                return matches;
            }
            return new List<ProductEntity>();
        }
    }
}
