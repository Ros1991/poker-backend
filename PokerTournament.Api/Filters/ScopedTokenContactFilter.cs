using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Responses;

namespace PokerTournament.Api.Filters;

/// <summary>
/// Token de escopo restrito (dealer por QR) nunca recebe contato nem documento de
/// jogador: antes de serializar, percorre a resposta (DTOs, listas, objetos aninhados
/// e anônimos) e zera Email/Whatsapp/Document/Notes (e a conta vinculada) de todo <see cref="PersonResponse"/>
/// e o link de WhatsApp de <see cref="EliminationResultResponse"/>.
/// Global de propósito: endpoint novo liberado ao dealer já nasce sem vazar.
/// </summary>
public class ScopedTokenContactFilter : IAsyncResultFilter
{
    private const int MaxDepth = 8;
    private const string DtoNamespace = "PokerTournament.Application.DTOs";

    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var scoped = !string.IsNullOrEmpty(context.HttpContext.User.FindFirst(TokenKinds.ClaimType)?.Value);
        if (scoped)
        {
            var value = context.Result switch
            {
                ObjectResult o => o.Value,
                JsonResult j => j.Value,
                _ => null,
            };
            if (value is not null)
                Strip(value, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
        }
        return next();
    }

    private static void Strip(object value, HashSet<object> visited, int depth)
    {
        if (depth > MaxDepth || value is string || value.GetType().IsValueType || !visited.Add(value))
            return;

        switch (value)
        {
            case PersonResponse person:
                person.Email = null;
                person.Whatsapp = null;
                person.Document = null;
                person.Notes = null;
                // Conta vinculada (SPEC-20261029-1725): o dealer não sabe de quem é a conta
                person.LinkedUserId = null;
                person.LinkedUserName = null;
                return;
            case EliminationResultResponse elimination:
                elimination.WhatsAppLink = null;
                return;
            case IEnumerable items:
                foreach (var item in items)
                    if (item is not null)
                        Strip(item, visited, depth + 1);
                return;
        }

        var type = value.GetType();
        if (!IsWalkable(type))
            return;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0
                || prop.PropertyType == typeof(string) || prop.PropertyType.IsValueType)
                continue;
            var child = prop.GetValue(value);
            if (child is not null)
                Strip(child, visited, depth + 1);
        }
    }

    // Só os DTOs do projeto e objetos anônimos dos controllers (nada de tipos do framework)
    private static bool IsWalkable(Type type) =>
        type.Namespace?.StartsWith(DtoNamespace, StringComparison.Ordinal) == true
        || (type.IsDefined(typeof(CompilerGeneratedAttribute), false) && type.Name.Contains("AnonymousType"));
}
