using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PokerTournament.Application.Common;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Services;

/// <summary>Emite os JWTs: de usuário (por sessão de aparelho) e de dealer (pedido aprovado pelo organizador).</summary>
public class JwtTokenFactory
{
    private readonly IConfiguration _configuration;

    public JwtTokenFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // 12h por padrão: cobre uma noite de torneio sem precisar renovar
    public int AccessTokenMinutes =>
        int.TryParse(_configuration["Jwt:ExpiryMinutes"], out var minutes) && minutes > 0 ? minutes : 720;

    // Sessão do aparelho: 90 dias, renovados a cada uso
    public int RefreshDays =>
        int.TryParse(_configuration["Jwt:RefreshDays"], out var days) && days > 0 ? days : 90;

    public string CreateUserToken(User user, Guid sessionId)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(TokenKinds.SessionClaim, sessionId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        return Write(claims, DateTime.UtcNow.AddMinutes(AccessTokenMinutes));
    }

    // Token do dealer: sub = id do PEDIDO aprovado (nunca um usuário nem o QR em si),
    // claim "name" com o nome informado, papel Dealer (nunca Admin, que passa direto no
    // filtro de tenant) e o torneio em "tournament_id". Vence junto com o pedido.
    public string CreateDealerToken(DealerRequest request)
    {
        var expiresAt = request.ExpiresAt
            ?? throw new InvalidOperationException("Pedido de dealer sem prazo: só pedidos aprovados geram token.");
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, request.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Name, request.Name),
            new Claim(ClaimTypes.Role, "Dealer"),
            new Claim(TokenKinds.ClaimType, TokenKinds.Dealer),
            new Claim(TokenKinds.TournamentClaim, request.TournamentId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        return Write(claims, expiresAt.UtcDateTime);
    }

    private string Write(IEnumerable<Claim> claims, DateTime expiresUtc)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Secret"]!));
        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: expiresUtc,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
