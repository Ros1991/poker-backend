using AutoMapper;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        // Person
        CreateMap<Person, PersonResponse>();
        CreateMap<CreatePersonRequest, Person>();
        CreateMap<UpdatePersonRequest, Person>()
            .ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember != null));

        // HomeGame
        CreateMap<HomeGame, HomeGameResponse>()
            .ForMember(d => d.MemberCount, o => o.MapFrom(s => s.Members.Count))
            .ForMember(d => d.TournamentCount, o => o.MapFrom(s => s.Tournaments.Count));
        CreateMap<CreateHomeGameRequest, HomeGame>();

        // HomeGameMember
        CreateMap<HomeGameMember, HomeGameMemberResponse>()
            .ForMember(d => d.UserName, o => o.MapFrom(s => s.User.FullName))
            .ForMember(d => d.UserEmail, o => o.MapFrom(s => s.User.Email))
            .ForMember(d => d.UserPhotoUrl, o => o.MapFrom(s => s.User.PhotoUrl))
            .ForMember(d => d.PersonName, o => o.MapFrom(s => s.Person != null ? s.Person.FullName : null))
            .ForMember(d => d.PersonNickname, o => o.MapFrom(s => s.Person != null ? s.Person.Nickname : null))
            .ForMember(d => d.PersonPhotoUrl, o => o.MapFrom(s => s.Person != null ? s.Person.PhotoUrl : null));

        // Tournament
        CreateMap<Tournament, TournamentResponse>();
        CreateMap<CreateTournamentRequest, Tournament>();

        // TournamentEntry
        CreateMap<TournamentEntry, EntryResponse>()
            .ForMember(d => d.Person, o => o.MapFrom(s => s.Person));

        // User
        CreateMap<User, UserResponse>()
            .ForMember(d => d.Name, o => o.MapFrom(s => s.FullName))
            .ForMember(d => d.PhotoUrl, o => o.MapFrom(s => s.PhotoUrl));

        // TournamentBlindLevel
        CreateMap<TournamentBlindLevel, TournamentBlindLevelResponse>();

        // BlindStructure
        CreateMap<CreateBlindStructureRequest, BlindStructure>();
        CreateMap<CreateBlindStructureRequest.BlindLevelItem, BlindLevel>();

        // Ranking
        CreateMap<CreateRankingRequest, Ranking>();

        // CostExtra
        CreateMap<CreateCostExtraRequest, CostExtra>();
    }
}
