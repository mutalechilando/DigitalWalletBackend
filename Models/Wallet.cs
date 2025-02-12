namespace DigitalWalletBackend.Models
{

using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    public class Wallet
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Balance { get; set; } = 0.00m;

        [ForeignKey("UserId")]
        public User User { get; set; }
    }
}
