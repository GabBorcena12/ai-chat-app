using AIChatApp.Core.Services;
using AIChatApp.Core.Data_Context;
using System.Linq;

namespace AIChatApp.Core.Services
{
    public class AgentTools
    {
        private readonly InventoryDbContext _db;

        public AgentTools(InventoryDbContext db)
        {
            _db = db;
        }

        public string SuggestProduct(string productName)
        {
            var products = _db.Products
                           .Where(p => p.ProductName.Contains(productName))
                           .OrderBy(p => p.Volume)
                           .Take(10)
                           .Select(p => p.ProductName)
                           .ToList();

            return products.Any()
                ? $"Recommended feeds: {string.Join(", ", products)}"
                : $"No product available for {products}";
        }
    }
}