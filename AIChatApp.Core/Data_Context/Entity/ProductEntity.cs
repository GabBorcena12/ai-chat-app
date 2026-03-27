using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIChatApp.Core.Data_Context.Entity
{
    [Table("Products")]
    public class ProductEntity
    {
        [Key]
        public int Id { get; set; }

        public string ProductName { get; set; }

        public string ProductAlias { get; set; }

        public int Volume { get; set; }

        public int UnitOfMeasure { get; set; }

        public int RestockThreshold { get; set; }

        public string CreatedBy { get; set; }

        public DateTime DateCreated { get; set; }

        public bool IsDeleted { get; set; }

        public bool IsDisabled { get; set; }

        public string MasterSku { get; set; }

        public bool IsStatutoryDiscountable { get; set; }

        public int MaxQtyForStatutoryDiscountable { get; set; }
    }
}
