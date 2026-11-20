using Citaspp.Web.Contracts;
using Citaspp.Web.Middleware;
using Citaspp.Web.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Citaspp.Web.Endpoints;

public static class ConfigurationEndpoints
{
    public static void MapConfigurationEndpoints(this WebApplication app)
    {
        var settings = app.MapGroup("/api/settings")
            .WithTags("Schedule")
            .AddEndpointFilter<RequestValidationFilter>();

        settings.MapPut("/", (
            UpdateScheduleSettingsRequest request,
            IAppointmentService service,
            CancellationToken cancellationToken) =>
        {
            service.UpdateSettings(request);
            return TypedResults.Ok(service.GetSettings());
        })
            .WithName("UpdateScheduleSettings")
            .WithSummary("Update schedule hours, workdays, and worker count")
            .Produces<ScheduleSettingsResponse>()
            .ProducesValidationProblem();

        var workers = app.MapGroup("/api/workers")
            .WithTags("Schedule")
            .AddEndpointFilter<RequestValidationFilter>();

        workers.MapPut("/{id:int}", (Results<Ok<WorkerResponse>, NotFound> (
            int id,
            UpdateWorkerRequest request,
            IAppointmentService service,
            CancellationToken cancellationToken) =>
            service.UpdateWorker(id, request) is { } worker
                ? TypedResults.Ok(worker)
                : TypedResults.NotFound()))
            .WithName("UpdateWorker")
            .WithSummary("Update a worker's name and optional calendar color")
            .Produces<WorkerResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        var services = app.MapGroup("/api/services")
            .WithTags("Schedule")
            .AddEndpointFilter<RequestValidationFilter>();

        services.MapPost("/", (
            SaveServiceRequest request,
            IAppointmentService service,
            CancellationToken cancellationToken) =>
        {
            var savedService = service.CreateService(request);
            return TypedResults.Created($"/api/services/{savedService.Id}", savedService);
        })
            .WithName("CreateService")
            .WithSummary("Create an appointment service")
            .Produces<ServiceResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        services.MapPut("/{id:int}", (Results<Ok<ServiceResponse>, NotFound> (
            int id,
            SaveServiceRequest request,
            IAppointmentService service,
            CancellationToken cancellationToken) =>
            service.UpdateService(id, request) is { } savedService
                ? TypedResults.Ok(savedService)
                : TypedResults.NotFound()))
            .WithName("UpdateService")
            .WithSummary("Update an appointment service")
            .Produces<ServiceResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        services.MapDelete("/{id:int}", (Results<NoContent, NotFound> (
            int id,
            IAppointmentService service,
            CancellationToken cancellationToken) =>
            service.DeleteService(id)
                ? TypedResults.NoContent()
                : TypedResults.NotFound()))
            .WithName("DeleteService")
            .WithSummary("Delete a service that is not used by any appointments")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var holidays = app.MapGroup("/api/holidays")
            .WithTags("Schedule")
            .AddEndpointFilter<RequestValidationFilter>();

        holidays.MapPut("/{date}", (
            DateOnly date,
            SaveHolidayRequest request,
            IAppointmentService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(service.SaveHoliday(date, request)))
            .WithName("SaveHoliday")
            .WithSummary("Create or update a holiday")
            .Produces<HolidayResponse>()
            .ProducesValidationProblem();

        holidays.MapDelete("/{date}", (Results<NoContent, NotFound> (
            DateOnly date,
            IAppointmentService service,
            CancellationToken cancellationToken) =>
            service.DeleteHoliday(date)
                ? TypedResults.NoContent()
                : TypedResults.NotFound()))
            .WithName("DeleteHoliday")
            .WithSummary("Delete a holiday")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }
}
