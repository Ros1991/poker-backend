namespace PokerTournament.Application.DTOs.Requests;

public class CreateUserRequest
{
    public Guid PersonId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
