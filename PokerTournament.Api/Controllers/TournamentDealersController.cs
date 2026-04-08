using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Api.Controllers;

[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/dealers")]
[Authorize]
public class TournamentDealersController : ControllerBase
{
    private readonly IAppDbContext _db;
    public TournamentDealersController(IAppDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult> GetAll(Guid tournamentId, CancellationToken ct)
    {
        var dealers = await _db.TournamentDealers
            .Include(td => td.Person)
            .Where(td => td.TournamentId == tournamentId)
            .Select(td => new
            {
                td.Id,
                td.PersonId,
                Name = td.Person.FullName,
                Nickname = td.Person.Nickname,
                PhotoUrl = td.Person.PhotoUrl,
                Whatsapp = td.Person.Whatsapp,
                td.AssignedAt
            })
            .ToListAsync(ct);
        return Ok(dealers);
    }

    [HttpPost]
    public async Task<ActionResult> Add(Guid tournamentId, [FromBody] AddTournamentDealerRequest request, CancellationToken ct)
    {
        var exists = await _db.TournamentDealers.AnyAsync(td => td.TournamentId == tournamentId && td.PersonId == request.PersonId, ct);
        if (exists) return Conflict(new { message = "Dealer já está no torneio." });

        var td = new TournamentDealer
        {
            TournamentId = tournamentId,
            PersonId = request.PersonId,
            AssignedAt = DateTimeOffset.UtcNow
        };
        _db.TournamentDealers.Add(td);
        await _db.SaveChangesAsync(ct);
        return Ok(new { td.Id, td.PersonId });
    }

    [HttpDelete("{dealerId:guid}")]
    public async Task<ActionResult> Remove(Guid tournamentId, Guid dealerId, CancellationToken ct)
    {
        var td = await _db.TournamentDealers.FirstOrDefaultAsync(x => x.Id == dealerId && x.TournamentId == tournamentId, ct);
        if (td == null) return NotFound();
        _db.TournamentDealers.Remove(td);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public class AddTournamentDealerRequest
{
    public Guid PersonId { get; set; }
}
