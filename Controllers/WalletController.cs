using DigitalWalletBackend.Data;
using DigitalWalletBackend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Transactions;

namespace DigitalWalletBackend.Controllers
{
    [Route("api/wallet")]
    [ApiController]
    [Authorize]
    public class WalletController : ControllerBase
    {
        private readonly WalletDbContext _context;

        public WalletController(WalletDbContext context)
        {
            _context = context;
        }

        [HttpGet("balance")]
        public async Task<IActionResult> GetBalance()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
                return NotFound(new { message = "Wallet not found" });

            return Ok(new { Balance = wallet.Balance });
        }

        [HttpPost("transfer")]
        public async Task<IActionResult> Transfer([FromBody] TransferRequest transfer)
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            if (transfer.ReceiverId == userId)
                return BadRequest(new { message = "You cannot transfer money to yourself." });

            var senderWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
            var receiverWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == transfer.ReceiverId);

            if (senderWallet == null || receiverWallet == null)
                return NotFound(new { message = "One or both wallets not found." });

            if (senderWallet.Balance < transfer.Amount)
                return BadRequest(new { message = "Insufficient balance." });

            // Use a transaction to ensure atomic updates
            using (var dbTransaction = await _context.Database.BeginTransactionAsync())
            {
                try
                {
                    senderWallet.Balance -= transfer.Amount;
                    receiverWallet.Balance += transfer.Amount;

                    var transaction = new Models.Transaction
                    {
                        SenderId = userId,
                        ReceiverId = transfer.ReceiverId,
                        Amount = transfer.Amount,
                        Timestamp = DateTime.UtcNow
                    };

                    _context.Transactions.Add(transaction);
                    await _context.SaveChangesAsync();
                    await dbTransaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await dbTransaction.RollbackAsync();
                    return StatusCode(500, new { message = "Transaction failed", error = ex.Message });
                }
            }

            return Ok(new { message = "Transfer successful", senderBalance = senderWallet.Balance });
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetTransactionHistory()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var transactions = await _context.Transactions
                .Where(t => t.SenderId == userId || t.ReceiverId == userId)
                .OrderByDescending(t => t.Timestamp)
                .ToListAsync();

            return Ok(transactions.Select(t => new
            {
                TransactionId = t.Id,
                Amount = t.Amount,
                Timestamp = t.Timestamp,
                Type = t.SenderId == userId ? "Sent" : "Received",
                OtherParty = t.SenderId == userId ? t.ReceiverId : t.SenderId
            }));
        }

        [HttpPost("deposit")]
        public async Task<IActionResult> Deposit([FromBody] DepositWithdrawRequest request)
        {
            if (request.Amount <= 0)
                return BadRequest("Amount must be greater than zero.");

            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
                return NotFound("Wallet not found.");

            wallet.Balance += request.Amount;

            var transaction = new Models.Transaction
            {
                SenderId = 0, // No sender for deposits, using 0 as a placeholder
                ReceiverId = userId,
                Amount = request.Amount,
                Timestamp = DateTime.UtcNow
            };

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Deposit successful", NewBalance = wallet.Balance });
        }

        [HttpPost("withdraw")]
        public async Task<IActionResult> Withdraw([FromBody] DepositWithdrawRequest request)
        {
            if (request.Amount <= 0)
                return BadRequest("Amount must be greater than zero.");

            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null)
                return NotFound("Wallet not found.");

            if (wallet.Balance < request.Amount)
                return BadRequest("Insufficient balance.");

            wallet.Balance -= request.Amount;

            var transaction = new Models.Transaction
            {
                SenderId = userId,
                ReceiverId = 0, // No receiver for withdrawals, using 0 as a placeholder
                Amount = -request.Amount, // Negative for withdrawal
                Timestamp = DateTime.UtcNow
            };

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Withdrawal successful", NewBalance = wallet.Balance });
        }

    }

    public class DepositWithdrawRequest
    {
        public decimal Amount { get; set; }
    }
}
