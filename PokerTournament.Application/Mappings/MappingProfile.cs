using AutoMapper;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Plans;
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
        // OwnerPlan/PlanFeatures: plano EFETIVO do dono, preenchido via PlanService depois do Map
        // (o flattening do AutoMapper levaria Owner.Plan cru, sem o vencimento)
        CreateMap<HomeGame, HomeGameResponse>()
            // Contagens: preenchidas pelo HomeGamesController com as mesmas regras das listas (sem torneio
            // cancelado — SPEC-20261026-1907); o Map não carrega as coleções só para contar.
            .ForMember(d => d.MemberCount, o => o.Ignore())
            .ForMember(d => d.TournamentCount, o => o.Ignore())
            .ForMember(d => d.OwnerPlan, o => o.Ignore())
            .ForMember(d => d.PlanFeatures, o => o.Ignore())
            // SPEC-20261029-1723: dependem do plano e do nome do dono (HomeGamePrimary.ApplyAsync no controller)
            .ForMember(d => d.IsNameDefault, o => o.Ignore())
            .ForMember(d => d.DisplayName, o => o.Ignore());
        // Sem IsPrimary no pedido: criar/editar nunca mexe no principal pelo corpo (SPEC-20261029-1723)
        CreateMap<CreateHomeGameRequest, HomeGame>();

        // HomeGameMember
        CreateMap<HomeGameMember, HomeGameMemberResponse>()
            .ForMember(d => d.UserName, o => o.MapFrom(s => s.User.FullName))
            .ForMember(d => d.UserEmail, o => o.MapFrom(s => s.User.Email))
            .ForMember(d => d.UserPhotoUrl, o => o.MapFrom(s => s.User.PhotoUrl))
            .ForMember(d => d.PersonName, o => o.MapFrom(s => s.Person != null ? s.Person.FullName : null))
            .ForMember(d => d.PersonNickname, o => o.MapFrom(s => s.Person != null ? s.Person.Nickname : null))
            .ForMember(d => d.PersonPhotoUrl, o => o.MapFrom(s => s.Person != null ? s.Person.PhotoUrl : null));

        // Tournament — navegações podem não estar carregadas (null-check obrigatório)
        CreateMap<Tournament, TournamentResponse>()
            .ForMember(d => d.HomeGameName, o => o.MapFrom(s => s.HomeGame != null ? s.HomeGame.Name : null))
            .ForMember(d => d.RankingName, o => o.MapFrom(s => s.Ranking != null ? s.Ranking.Name : null))
            .ForMember(d => d.OwnerPlan, o => o.Ignore())
            .ForMember(d => d.PlanFeatures, o => o.Ignore());
        // Knockout (SPEC-20261026-1909): formato e bounty resolvidos no TournamentService (herdar da estrutura, manter na
        // edição, validar contra o buy-in) — o Map não pode gravar null no formato
        CreateMap<CreateTournamentRequest, Tournament>()
            .ForMember(d => d.KnockoutType, o => o.Ignore())
            .ForMember(d => d.BountyAmount, o => o.Ignore())
            // Máximo de add-ons (SPEC-20261027-1311): resolvido no TournamentService (null = herdar/manter); o Map gravaria
            // 0 (ilimitado) no lugar do null
            .ForMember(d => d.MaxAddons, o => o.Ignore());

        // TournamentEntry
        CreateMap<TournamentEntry, EntryResponse>()
            .ForMember(d => d.Person, o => o.MapFrom(s => s.Person));

        // User
        // Plano da conta (SPEC-20261023-1205): gravado + efetivo (Mesa depois do vencimento)
        CreateMap<User, UserResponse>()
            .ForMember(d => d.Name, o => o.MapFrom(s => s.FullName))
            .ForMember(d => d.PhotoUrl, o => o.MapFrom(s => s.PhotoUrl))
            .ForMember(d => d.EffectivePlan, o => o.Ignore())
            .AfterMap((s, d) => PlanCatalog.FillUserPlan(d, s));

        // TournamentBlindLevel
        CreateMap<TournamentBlindLevel, TournamentBlindLevelResponse>();

        // BlindStructure
        CreateMap<CreateBlindStructureRequest, BlindStructure>()
            .ForMember(d => d.DefaultKnockoutType, o => o.Ignore())
            .ForMember(d => d.DefaultBountyAmount, o => o.Ignore())
            .ForMember(d => d.DefaultMaxAddons, o => o.Ignore());
        CreateMap<CreateBlindStructureRequest.BlindLevelItem, BlindLevel>();

        // Ranking
        CreateMap<CreateRankingRequest, Ranking>();

        // CostExtra
        CreateMap<CreateCostExtraRequest, CostExtra>();
    }
}
