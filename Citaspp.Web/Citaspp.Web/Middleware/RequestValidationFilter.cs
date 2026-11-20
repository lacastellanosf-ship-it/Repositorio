using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Citaspp.Web.Middleware;

public sealed class RequestValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var argument in context.Arguments)
        {
            if (argument is null)
            {
                continue;
            }

            var validationResults = new List<ValidationResult>();
            if (Validator.TryValidateObject(argument, new ValidationContext(argument), validationResults, validateAllProperties: true))
            {
                continue;
            }

            foreach (var result in validationResults)
            {
                IEnumerable<string> memberNames = result.MemberNames.Any()
                    ? result.MemberNames
                    : ["request"];
                foreach (var memberName in memberNames)
                {
                    if (!errors.TryGetValue(memberName, out var messages))
                    {
                        messages = [];
                        errors.Add(memberName, messages);
                    }

                    messages.Add(result.ErrorMessage ?? "El valor no es válido.");
                }
            }
        }

        if (errors.Count == 0)
        {
            return await next(context);
        }

        var responseErrors = errors.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        return TypedResults.ValidationProblem(responseErrors);
    }
}
