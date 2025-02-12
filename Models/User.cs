using System.ComponentModel.DataAnnotations;

namespace DigitalWalletBackend.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string UserName { get; set; }

        [Required, MaxLength(100)]
        public string Email { get; set; }

        [Required]
        public string PasswordHash { get; set; }
    }
}
