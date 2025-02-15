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

        [HttpGet("me")]
        public async Task<IActionResult> GetCurrentUser()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            return Ok(new
            {
                Username = user.UserName,
                Email = user.Email
            });
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

            //if (transfer.ReceiverId == userId)
            //return BadRequest(new { message = "You cannot transfer money to yourself." });

            var senderWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
            var receiver = await _context.Users.FirstOrDefaultAsync(u => u.Email == transfer.Receiver || u.UserName == transfer.Receiver);

            if (senderWallet == null || receiver == null)
                return NotFound(new { message = "Sender or receiver not found." });

            var receiverWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == receiver.Id);
            if (receiverWallet == null)
                return NotFound(new { message = "Receiver wallet not found." });

            if (senderWallet.Balance < transfer.Amount)
                return BadRequest(new { message = "Insufficient balance." });

            using (var dbTransaction = await _context.Database.BeginTransactionAsync())
            {
                try
                {
                    senderWallet.Balance -= transfer.Amount;
                    receiverWallet.Balance += transfer.Amount;

                    var transaction = new Models.Transaction
                    {
                        SenderId = userId,
                        ReceiverId = receiver.Id,
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

            var userIds = transactions.SelectMany(t => new[] { t.SenderId, t.ReceiverId }).Distinct();
            var userMap = await _context.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => new { u.UserName, u.Email });

            var result = transactions.Select(t => new
            {
                TransactionId = t.Id,
                Amount = t.Amount,
                Timestamp = t.Timestamp,
                Type = t.SenderId == userId ? "Sent" : "Received",
                OtherPartyUsername = t.SenderId == t.ReceiverId
                    ? "Self"
                    : (userMap.ContainsKey(t.SenderId) && userMap.ContainsKey(t.ReceiverId)
                        ? (t.SenderId == userId ? userMap[t.ReceiverId].UserName : userMap[t.SenderId].UserName)
                        : "Self"),
                OtherPartyEmail = t.SenderId == t.ReceiverId
                    ? "Own Wallet"
                    : (userMap.ContainsKey(t.SenderId) && userMap.ContainsKey(t.ReceiverId)
                        ? (t.SenderId == userId ? userMap[t.ReceiverId].Email : userMap[t.SenderId].Email)
                        : "Self")
            });

            return Ok(result);
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
