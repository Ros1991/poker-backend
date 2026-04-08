using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

public class AuthService
{
    private readonly IAppDbContext _db;
    private readonly IConfiguration _configuration;

    public AuthService(IAppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email && u.IsActive, ct);

        if (user is null)
            throw new DomainException("E-mail ou senha inválidos.");

        if (!VerifyPassword(request.Password, user.PasswordHash))
            throw new DomainException("E-mail ou senha inválidos.");

        user.LastLoginAt = DateTimeOffset.UtcNow;

        var token = GenerateJwtToken(user);
        var refreshToken = GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7);

        await _db.SaveChangesAsync(ct);

        var expiresInMinutes = int.Parse(_configuration["Jwt:ExpiryMinutes"] ?? "60");

        return new AuthResponse
        {
            AccessToken = token,
            RefreshToken = refreshToken,
            ExpiresIn = expiresInMinutes * 60,
            User = new UserResponse
            {
                Id = user.Id,
                Email = user.Email,
                Name = user.FullName,
                Role = user.Role,
                PhotoUrl = user.PhotoUrl
            }
        };
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var existingUser = await _db.Users
            .AnyAsync(u => u.Email == request.Email, ct);

        if (existingUser)
            throw new DomainException("E-mail já está em uso.");

        var user = new User
        {
            FullName = request.FullName,
            Nickname = request.Nickname,
            Email = request.Email,
            Whatsapp = request.Whatsapp,
            PasswordHash = HashPassword(request.Password),
            Role = "Jogador",
            IsActive = true,
            LastLoginAt = DateTimeOffset.UtcNow
        };

        _db.Users.Add(user);

        var token = GenerateJwtToken(user);
        var refreshToken = GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7);

        await _db.SaveChangesAsync(ct);

        var expiresInMinutes = int.Parse(_configuration["Jwt:ExpiryMinutes"] ?? "60");

        return new AuthResponse
        {
            AccessToken = token,
            RefreshToken = refreshToken,
            ExpiresIn = expiresInMinutes * 60,
            User = new UserResponse
            {
                Id = user.Id,
                Email = user.Email,
                Name = user.FullName,
                Role = user.Role,
                PhotoUrl = user.PhotoUrl
            }
        };
    }

    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.RefreshToken == refreshToken
                                   && u.RefreshTokenExpiresAt > DateTimeOffset.UtcNow
                                   && u.IsActive, ct);

        if (user is null)
            throw new DomainException("Token de atualização inválido ou expirado.");

        var token = GenerateJwtToken(user);
        var newRefreshToken = GenerateRefreshToken();

        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7);

        await _db.SaveChangesAsync(ct);

        var expiresInMinutes = int.Parse(_configuration["Jwt:ExpiryMinutes"] ?? "60");

        return new AuthResponse
        {
            AccessToken = token,
            ExpiresIn = expiresInMinutes * 60,
            User = new UserResponse
            {
                Id = user.Id,
                Email = user.Email,
                Name = user.FullName,
                Role = user.Role,
                PhotoUrl = user.PhotoUrl
            }
        };
    }

    public async Task<UserResponse?> GetByIdAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);

        if (user is null) return null;

        return new UserResponse
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.FullName,
            Role = user.Role,
            PhotoUrl = user.PhotoUrl
        };
    }

    public async Task LogoutAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            await _db.SaveChangesAsync(ct);
        }
    }

    private string GenerateJwtToken(Domain.Entities.User user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_configuration["Jwt:Secret"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresInMinutes = int.Parse(_configuration["Jwt:ExpiryMinutes"] ?? "60");

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiresInMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    private static bool VerifyPassword(string password, string hash)
    {
        return HashPassword(password) == hash;
    }
}
