using DigitalWalletBackend.Data;
using DigitalWalletBackend.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using BCrypt.Net;
using DigitalWalletBackend.DTOs;
using Microsoft.AspNetCore.Authorization;
using DigitalWalletBackend.Services;

namespace DigitalWalletBackend.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly WalletDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly TokenValidationService _tokenValidationService;

        public AuthController(WalletDbContext context, IConfiguration configuration, TokenValidationService tokenValidationService)
        {
            _context = context;
            _configuration = configuration;
            _tokenValidationService = tokenValidationService;
        }

        [AllowAnonymous]
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] User user)
        {
            if (await _context.Users.AnyAsync(u => u.Email == user.Email))
                return BadRequest("Email already exists");

            if (await _context.Users.AnyAsync(u => u.UserName == user.UserName))
            {
                return BadRequest(new { message = "Username is already taken" });
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Create Wallet for the user
            var wallet = new Wallet { UserId = user.Id, Balance = 0.00m };
            _context.Wallets.Add(wallet);
            await _context.SaveChangesAsync();

            return Ok("User registered successfully");
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == loginDto.Email);

            if (user == null || !BCrypt.Net.BCrypt.Verify(loginDto.Password, user.PasswordHash))
            {
                return Unauthorized(new { message = "Invalid email or password" });
            }

            var token = GenerateJwtToken(user);
            return Ok(new
            {
                token = token,
                user = new { user.UserName, user.Email }
            });

        }

        private string GenerateJwtToken(User user)
        {
            var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]);
            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new Claim[]
                {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email)
                }),
                Expires = DateTime.UtcNow.AddHours(2),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var token = Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

            if (string.IsNullOrEmpty(token))
            {
                return BadRequest(new { message = "Token is required" });
            }

            if (!_tokenValidationService.ValidateToken(token, out var claimsPrincipal))
            {
                return Unauthorized(new { message = "Invalid token" });
            }

            var expiryDate = claimsPrincipal.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Exp)?.Value;
            if (expiryDate == null)
            {
                return Unauthorized(new { message = "Invalid token" });
            }

            var expirationTime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(expiryDate)).UtcDateTime;

            if (expirationTime < DateTime.UtcNow)
            {
                return Unauthorized(new { message = "Token already expired" });
            }

            var isBlacklisted = await _context.BlacklistedTokens.AnyAsync(bt => bt.Token == token);
            if (isBlacklisted)
            {
                return Unauthorized(new { message = "Token is already revoked" });
            }

            _context.BlacklistedTokens.Add(new BlacklistedToken
            {
                Token = token,
                ExpiryDate = expirationTime
            });

            await _context.SaveChangesAsync();

            return Ok(new { message = "Logged out successfully" });
        }


    }

}
