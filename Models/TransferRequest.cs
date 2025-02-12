namespace DigitalWalletBackend.Models
{
    public class TransferRequest
    {
        public int ReceiverId { get; set; }
        public decimal Amount { get; set; }
    }
}
