using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Citaspp.Web.Contracts;
using Microsoft.JSInterop;

namespace Citaspp.Web.Client;

internal sealed class LocalApiMessageHandler(
    IJSRuntime jsRuntime,
    FirebaseClient firebaseClient) : HttpMessageHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] DefaultWorkerColors =
        ["#13CBE8", "#FF9718", "#C9DE17", "#9346E2", "#E83C7D"];

    private readonly SemaphoreSlim requestLock = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await requestLock.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            try
            {
                var configurationSection = GetConfigurationSection(request);
                if (firebaseClient.IsConfigured && firebaseClient.IsAuthenticated)
                {
                    await RefreshCloudConfigurationAsync(state);
                }

                var result = await RouteAsync(request, state, cancellationToken);
                if (result.Persist)
                {
                    if (configurationSection is not null &&
                        firebaseClient.IsConfigured &&
                        firebaseClient.IsAuthenticated)
                    {
                        await SaveCloudConfigurationAsync(configurationSection, state);
                    }

                    await SaveStateAsync(state, cancellationToken);
                }

                return CreateResponse(request, result);
            }

            catch (LocalApiException exception)
            {
                return CreateProblemResponse(request, exception.StatusCode, exception.Message);
            }
            catch (JsonException)
            {
                return CreateProblemResponse(request, (int)HttpStatusCode.BadRequest, "La solicitud no es válida.");
            }
            catch (JSException exception)
            {
                return CreateProblemResponse(
                    request,
                    (int)HttpStatusCode.BadGateway,
                    FirebaseClient.UserFriendlyError(exception));
            }
        }
        finally
        {
            requestLock.Release();
        }
    }

    private async Task<RouteResult> RouteAsync(
        HttpRequestMessage request,
        LocalScheduleState state,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("La solicitud no tiene una dirección.");
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var apiIndex = Array.IndexOf(segments, "api");
        if (apiIndex < 0 || segments.Length - apiIndex < 2)
        {
            return new RouteResult((int)HttpStatusCode.NotFound);
        }

        segments = segments[apiIndex..];
        if (segments[1] == "appointments")
        {
            return await RouteAppointmentsAsync(request, uri, segments, state, cancellationToken);
        }

        if (segments[1] == "settings")
        {
            return await RouteSettingsAsync(request, segments, state, cancellationToken);
        }

        if (segments[1] == "workers")
        {
            return await RouteWorkersAsync(request, segments, state, cancellationToken);
        }

        if (segments[1] == "services")
        {
            return await RouteServicesAsync(request, segments, state, cancellationToken);
        }

        if (segments[1] == "holidays")
        {
            return await RouteHolidaysAsync(request, segments, state, cancellationToken);
        }

        return new RouteResult((int)HttpStatusCode.NotFound);
    }

    private async Task<RouteResult> RouteAppointmentsAsync(
        HttpRequestMessage request,
        Uri uri,
        string[] segments,
        LocalScheduleState state,
        CancellationToken cancellationToken)
    {
        if (firebaseClient.IsConfigured)
        {
            if (!firebaseClient.IsAuthenticated)
            {
                throw new LocalApiException((int)HttpStatusCode.Unauthorized, "Inicia sesión para acceder a las citas.");
            }

            return await RouteCloudAppointmentsAsync(request, uri, segments, state, cancellationToken);
        }

        if (segments.Length == 2 && request.Method == HttpMethod.Get)
        {
            var query = ParseQuery(uri.Query);
            if (!query.TryGetValue("start", out var startText) ||
                !query.TryGetValue("end", out var endText) ||
                !DateTimeOffset.TryParse(startText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                !DateTimeOffset.TryParse(endText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
                start >= end)
            {
                return new RouteResult((int)HttpStatusCode.BadRequest);
            }

            var appointments = state.Appointments
                .Where(appointment => appointment.StartsAt >= start && appointment.StartsAt < end)
                .OrderBy(appointment => appointment.StartsAt)
                .Select(appointment => MapAppointment(state, appointment))
                .ToArray();
            return new RouteResult((int)HttpStatusCode.OK, appointments);
        }

        if (segments.Length == 2 && request.Method == HttpMethod.Post)
        {
            var body = await ReadBodyAsync<CreateAppointmentRequest>(request, cancellationToken);
            ValidateRequest(body);
            var saved = SaveAppointment(state, null, body.CustomerName, body.Phone, body.WorkerId, body.ServiceId, body.StartsAt);
            return new RouteResult(
                (int)HttpStatusCode.Created,
                MapAppointment(state, saved),
                $"/api/appointments/{saved.Id}",
                Persist: true);
        }

        if (segments.Length == 3 && segments[2] == "move" && request.Method == HttpMethod.Post)
        {
            var body = await ReadBodyAsync<MoveAppointmentsRequest>(request, cancellationToken);
            ValidateRequest(body);
            var affected = MoveAppointments(state, body);
            return new RouteResult((int)HttpStatusCode.OK, new BulkAppointmentsResponse(affected), Persist: true);
        }

        if (segments.Length == 3 && segments[2] == "bulk-delete" && request.Method == HttpMethod.Post)
        {
            var body = await ReadBodyAsync<DeleteAppointmentsRequest>(request, cancellationToken);
            ValidateRequest(body);
            var ids = body.AppointmentIds.Distinct().ToHashSet();
            if (ids.Any(id => id <= 0))
            {
                throw new LocalApiException((int)HttpStatusCode.BadRequest, "Selecciona al menos una cita válida.");
            }

            if (ids.Count == 0 || ids.Any(id => state.Appointments.All(appointment => appointment.Id != id)))
            {
                throw new LocalApiException((int)HttpStatusCode.NotFound, "No se encontraron todas las citas seleccionadas.");
            }

            state.Appointments.RemoveAll(appointment => ids.Contains(appointment.Id));
            return new RouteResult((int)HttpStatusCode.OK, new BulkAppointmentsResponse(ids.Count), Persist: true);
        }

        if (segments.Length == 3 && int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            var existing = state.Appointments.FirstOrDefault(appointment => appointment.Id == id);
            if (request.Method == HttpMethod.Get)
            {
                return existing is null
                    ? new RouteResult((int)HttpStatusCode.NotFound)
                    : new RouteResult((int)HttpStatusCode.OK, MapAppointment(state, existing));
            }

            if (request.Method == HttpMethod.Put)
            {
                if (existing is null)
                {
                    return new RouteResult((int)HttpStatusCode.NotFound);
                }

                var body = await ReadBodyAsync<UpdateAppointmentRequest>(request, cancellationToken);
                ValidateRequest(body);
                var saved = SaveAppointment(state, id, body.CustomerName, body.Phone, body.WorkerId, body.ServiceId, body.StartsAt);
                return new RouteResult((int)HttpStatusCode.OK, MapAppointment(state, saved), Persist: true);
            }

            if (request.Method == HttpMethod.Delete)
            {
                if (existing is null)
                {
                    return new RouteResult((int)HttpStatusCode.NotFound);
                }

                state.Appointments.Remove(existing);
                return new RouteResult((int)HttpStatusCode.NoContent, Persist: true);
            }
        }

        return new RouteResult((int)HttpStatusCode.NotFound);
    }

    private async Task<RouteResult> RouteCloudAppointmentsAsync(
        HttpRequestMessage request,
        Uri uri,
        string[] segments,
        LocalScheduleState state,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 2 && request.Method == HttpMethod.Get)
        {
            var query = ParseQuery(uri.Query);
            if (!query.TryGetValue("start", out var startText) ||
                !query.TryGetValue("end", out var endText) ||
                !DateTimeOffset.TryParse(startText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                !DateTimeOffset.TryParse(endText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
                start >= end)
            {
                return new RouteResult((int)HttpStatusCode.BadRequest);
            }

            var appointments = await firebaseClient.GetAppointmentsAsync(start, end);
            return new RouteResult(
                (int)HttpStatusCode.OK,
                appointments
                    .OrderBy(appointment => appointment.StartsAt)
                    .Select(appointment => MapAppointment(state, appointment))
                    .ToArray());
        }

        if (segments.Length == 2 && request.Method == HttpMethod.Post)
        {
            var body = await ReadBodyAsync<CreateAppointmentRequest>(request, cancellationToken);
            ValidateRequest(body);
            await RefreshCloudAppointmentsAsync(state);
            var draft = SaveAppointment(
                state,
                null,
                body.CustomerName,
                body.Phone,
                body.WorkerId,
                body.ServiceId,
                body.StartsAt);
            var createdId = await firebaseClient.CreateAppointmentAsync(draft);
            var saved = draft with { Id = createdId };
            state.Appointments[^1] = saved;
            return new RouteResult(
                (int)HttpStatusCode.Created,
                saved,
                $"/api/appointments/{saved.Id}");
        }

        if (segments.Length == 3 && segments[2] == "move" && request.Method == HttpMethod.Post)
        {
            var body = await ReadBodyAsync<MoveAppointmentsRequest>(request, cancellationToken);
            ValidateRequest(body);
            await RefreshCloudAppointmentsAsync(state);
            var affected = MoveAppointments(state, body);
            var ids = body.AppointmentIds.ToHashSet();
            var moved = state.Appointments.Where(appointment => ids.Contains(appointment.Id)).ToArray();
            await firebaseClient.SaveMovedAppointmentsAsync(moved);
            return new RouteResult((int)HttpStatusCode.OK, new BulkAppointmentsResponse(affected));
        }

        if (segments.Length == 3 && segments[2] == "bulk-delete" && request.Method == HttpMethod.Post)
        {
            var body = await ReadBodyAsync<DeleteAppointmentsRequest>(request, cancellationToken);
            ValidateRequest(body);
            await RefreshCloudAppointmentsAsync(state);
            var ids = body.AppointmentIds.Distinct().ToHashSet();
            if (ids.Any(id => id <= 0))
            {
                throw new LocalApiException((int)HttpStatusCode.BadRequest, "Selecciona al menos una cita válida.");
            }

            if (ids.Count == 0 || ids.Any(id => state.Appointments.All(appointment => appointment.Id != id)))
            {
                throw new LocalApiException((int)HttpStatusCode.NotFound, "No se encontraron todas las citas seleccionadas.");
            }

            await firebaseClient.DeleteAppointmentsAsync(ids.ToArray());
            return new RouteResult((int)HttpStatusCode.OK, new BulkAppointmentsResponse(ids.Count));
        }

        if (segments.Length == 3 && int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            if (request.Method == HttpMethod.Get)
            {
                var existing = await firebaseClient.GetAppointmentAsync(id);
                return existing is null
                    ? new RouteResult((int)HttpStatusCode.NotFound)
                    : new RouteResult((int)HttpStatusCode.OK, MapAppointment(state, existing));
            }

            if (request.Method == HttpMethod.Put)
            {
                await RefreshCloudAppointmentsAsync(state);
                if (state.Appointments.All(appointment => appointment.Id != id))
                {
                    return new RouteResult((int)HttpStatusCode.NotFound);
                }

                var body = await ReadBodyAsync<UpdateAppointmentRequest>(request, cancellationToken);
                ValidateRequest(body);
                var saved = SaveAppointment(
                    state,
                    id,
                    body.CustomerName,
                    body.Phone,
                    body.WorkerId,
                    body.ServiceId,
                    body.StartsAt);
                await firebaseClient.UpdateAppointmentAsync(saved);
                return new RouteResult((int)HttpStatusCode.OK, saved);
            }

            if (request.Method == HttpMethod.Delete)
            {
                var existing = await firebaseClient.GetAppointmentAsync(id);
                if (existing is null)
                {
                    return new RouteResult((int)HttpStatusCode.NotFound);
                }

                await firebaseClient.DeleteAppointmentAsync(id);
                return new RouteResult((int)HttpStatusCode.NoContent);
            }
        }

        return new RouteResult((int)HttpStatusCode.NotFound);
    }

    private async Task RefreshCloudAppointmentsAsync(LocalScheduleState state)
    {
        state.Appointments = (await firebaseClient.GetAllAppointmentsAsync()).ToList();
        if (state.Appointments.Count > 0)
        {
            state.NextAppointmentId = Math.Max(
                state.NextAppointmentId,
                state.Appointments.Max(appointment => appointment.Id) + 1);
        }
    }

    private async Task RefreshCloudConfigurationAsync(LocalScheduleState state)
    {
        var configuration = await firebaseClient.GetConfigurationAsync();
        state.Settings = configuration.Settings;
        state.Workers = configuration.Workers.ToList();
        state.Services = configuration.Services.ToList();
        state.Holidays = configuration.Holidays
            .Select(holiday => new HolidayResponse(ToLocalDateOffset(holiday.Date), holiday.Name))
            .ToList();
        state.NextServiceId = Math.Max(
            state.NextServiceId,
            state.Services.Count == 0 ? 1 : state.Services.Max(service => service.Id) + 1);
    }

    private async Task SaveCloudConfigurationAsync(string section, LocalScheduleState state)
    {
        var sections = section switch
        {
            "settings" => new Dictionary<string, object>
            {
                ["settings"] = state.Settings,
                ["workers"] = state.Workers.OrderBy(worker => worker.Id).ToArray()
            },
            "workers" => new Dictionary<string, object>
            {
                ["workers"] = state.Workers.OrderBy(worker => worker.Id).ToArray()
            },
            "services" => new Dictionary<string, object>
            {
                ["services"] = state.Services.OrderBy(service => service.Id).ToArray()
            },
            "holidays" => new Dictionary<string, object>
            {
                ["holidays"] = state.Holidays
                    .OrderBy(holiday => holiday.Date)
                    .Select(holiday => new SharedHolidayResponse(
                        DateOnly.FromDateTime(holiday.Date.LocalDateTime),
                        holiday.Name))
                    .ToArray()
            },
            _ => throw new InvalidOperationException("La sección de configuración no es válida.")
        };
        await firebaseClient.SaveConfigurationSectionsAsync(sections);
    }

    private static string? GetConfigurationSection(HttpRequestMessage request)
    {
        var segments = request.RequestUri?.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments is null)
        {
            return null;
        }

        var apiIndex = Array.IndexOf(segments, "api");
        if (apiIndex < 0 || segments.Length <= apiIndex + 1)
        {
            return null;
        }

        return segments[apiIndex + 1] is "settings" or "workers" or "services" or "holidays"
            ? segments[apiIndex + 1]
            : null;
    }

    private async Task<RouteResult> RouteSettingsAsync(
        HttpRequestMessage request,
        string[] segments,
        LocalScheduleState state,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 2 && request.Method == HttpMethod.Get)
        {
            return new RouteResult((int)HttpStatusCode.OK, state.Settings);
        }

        if (segments.Length != 2 || request.Method != HttpMethod.Put)
        {
            return new RouteResult((int)HttpStatusCode.NotFound);
        }

        var body = await ReadBodyAsync<UpdateScheduleSettingsRequest>(request, cancellationToken);
        ValidateRequest(body);
        if (body.EndTime <= body.StartTime)
        {
            throw new LocalApiException((int)HttpStatusCode.BadRequest, "La hora de inicio debe ser anterior a la hora de fin.");
        }

        if (body.WorkDays.Length == 0 || body.WorkDays
                .Any(day => !Enum.TryParse<DayOfWeek>(day, true, out var parsed) || !Enum.IsDefined(parsed)))
        {
            throw new LocalApiException((int)HttpStatusCode.BadRequest, "Hay un día laborable no válido.");
        }

        if (firebaseClient.IsConfigured && firebaseClient.IsAuthenticated)
        {
            await RefreshCloudAppointmentsAsync(state);
        }

        var normalizedDays = body.WorkDays
            .Select(day => Enum.Parse<DayOfWeek>(day, true))
            .Distinct()
            .OrderBy(day => (int)day)
            .Select(day => day.ToString())
            .ToArray();
        if (normalizedDays.Length != body.WorkDays.Length)
        {
            throw new LocalApiException((int)HttpStatusCode.BadRequest, "No se pueden repetir los días laborables.");
        }

        if (state.Appointments.Any(appointment => appointment.WorkerId > body.WorkerCount))
        {
            throw new LocalApiException(
                (int)HttpStatusCode.BadRequest,
                "No se puede reducir el número de trabajadores por debajo de los que tienen citas reservadas.");
        }

        state.Workers.RemoveAll(worker => worker.Id > body.WorkerCount);
        for (var id = 1; id <= body.WorkerCount; id++)
        {
            if (state.Workers.All(worker => worker.Id != id))
            {
                state.Workers.Add(new WorkerResponse(id, $"Trabajador {id}", DefaultWorkerColors[(id - 1) % DefaultWorkerColors.Length]));
            }
        }

        state.Settings = new ScheduleSettingsResponse(
            body.StartTime,
            body.EndTime,
            normalizedDays,
            body.WorkerCount);
        return new RouteResult((int)HttpStatusCode.OK, state.Settings, Persist: true);
    }

    private async Task<RouteResult> RouteWorkersAsync(
        HttpRequestMessage request,
        string[] segments,
        LocalScheduleState state,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 2 && request.Method == HttpMethod.Get)
        {
            return new RouteResult(
                (int)HttpStatusCode.OK,
                state.Workers.OrderBy(worker => worker.Id).ToArray());
        }

        if (segments.Length != 3 ||
            !int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
            request.Method != HttpMethod.Put)
        {
            return new RouteResult((int)HttpStatusCode.NotFound);
        }

        var index = state.Workers.FindIndex(worker => worker.Id == id);
        if (index < 0)
        {
            return new RouteResult((int)HttpStatusCode.NotFound);
        }

        var body = await ReadBodyAsync<UpdateWorkerRequest>(request, cancellationToken);
        ValidateRequest(body);
        var name = body.Name.Trim();
        if (state.Workers.Any(worker =>
                worker.Id != id &&
                string.Equals(worker.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new LocalApiException((int)HttpStatusCode.Conflict, "Ya existe un trabajador con ese nombre.");
        }

        var color = body.Color is null ? state.Workers[index].Color : ValidateColor(body.Color);
        var saved = new WorkerResponse(id, name, color);
        state.Workers[index] = saved;
        return new RouteResult((int)HttpStatusCode.OK, saved, Persist: true);
    }

    private async Task<RouteResult> RouteServicesAsync(
        HttpRequestMessage request,
        string[] segments,
        LocalScheduleState state,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 2 && request.Method == HttpMethod.Get)
        {
            return new RouteResult(
                (int)HttpStatusCode.OK,
                state.Services.OrderBy(service => service.Name, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        if (segments.Length == 2 && request.Method == HttpMethod.Post)
        {
            var body = await ReadBodyAsync<SaveServiceRequest>(request, cancellationToken);
            ValidateRequest(body);
            var saved = CreateService(state, body);
            return new RouteResult((int)HttpStatusCode.Created, saved, $"/api/services/{saved.Id}", Persist: true);
        }

        if (segments.Length == 3 &&
            int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            if (request.Method == HttpMethod.Put)
            {
                if (state.Services.All(service => service.Id != id))
                {
                    return new RouteResult((int)HttpStatusCode.NotFound);
                }

                var body = await ReadBodyAsync<SaveServiceRequest>(request, cancellationToken);
                ValidateRequest(body);
                var saved = SaveService(state, id, body);
                return new RouteResult((int)HttpStatusCode.OK, saved, Persist: true);
            }

            if (request.Method == HttpMethod.Delete)
            {
                if (state.Services.All(service => service.Id != id))
                {
                    return new RouteResult((int)HttpStatusCode.NotFound);
                }

                if (firebaseClient.IsConfigured && firebaseClient.IsAuthenticated)
                {
                    await RefreshCloudAppointmentsAsync(state);
                }

                if (state.Appointments.Any(appointment => appointment.ServiceId == id))
                {
                    throw new LocalApiException(
                        (int)HttpStatusCode.Conflict,
                        "No se puede eliminar un servicio que ya tiene citas reservadas.");
                }

                state.Services.RemoveAll(service => service.Id == id);
                return new RouteResult((int)HttpStatusCode.NoContent, Persist: true);
            }
        }

        return new RouteResult((int)HttpStatusCode.NotFound);
    }

    private async Task<RouteResult> RouteHolidaysAsync(
        HttpRequestMessage request,
        string[] segments,
        LocalScheduleState state,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 2 && request.Method == HttpMethod.Get)
        {
            return new RouteResult(
                (int)HttpStatusCode.OK,
                state.Holidays.OrderBy(holiday => holiday.Date).ToArray());
        }

        if (segments.Length != 3 ||
            !DateOnly.TryParseExact(segments[2], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return new RouteResult((int)HttpStatusCode.NotFound);
        }

        var index = state.Holidays.FindIndex(holiday => DateOnly.FromDateTime(holiday.Date.LocalDateTime) == date);
        if (request.Method == HttpMethod.Put)
        {
            var body = await ReadBodyAsync<SaveHolidayRequest>(request, cancellationToken);
            ValidateRequest(body);
            var saved = new HolidayResponse(ToLocalDateOffset(date), body.Name.Trim());
            if (index < 0)
            {
                state.Holidays.Add(saved);
            }
            else
            {
                state.Holidays[index] = saved;
            }

            return new RouteResult((int)HttpStatusCode.OK, saved, Persist: true);
        }

        if (request.Method == HttpMethod.Delete)
        {
            if (index < 0)
            {
                return new RouteResult((int)HttpStatusCode.NotFound);
            }

            state.Holidays.RemoveAt(index);
            return new RouteResult((int)HttpStatusCode.NoContent, Persist: true);
        }

        return new RouteResult((int)HttpStatusCode.NotFound);
    }

    private static AppointmentResponse SaveAppointment(
        LocalScheduleState state,
        int? appointmentId,
        string customerName,
        string phone,
        int workerId,
        int serviceId,
        DateTimeOffset startsAt)
    {
        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(phone))
        {
            throw new LocalApiException((int)HttpStatusCode.BadRequest, "El nombre y el teléfono del cliente son obligatorios.");
        }

        if (phone.Any(character => !char.IsDigit(character)))
        {
            throw new LocalApiException((int)HttpStatusCode.BadRequest, "El teléfono solo puede contener dígitos.");
        }

        if (phone.Length != 9 && phone.Length != 12)
        {
            throw new LocalApiException((int)HttpStatusCode.BadRequest, "Introduce un teléfono de 9 dígitos o uno internacional de 12 dígitos.");
        }

        if (appointmentId is not null && state.Appointments.All(appointment => appointment.Id != appointmentId))
        {
            throw new LocalApiException((int)HttpStatusCode.NotFound, "No se encontró la cita que se intenta editar.");
        }

        if (appointmentId is null)
        {
            var appointmentLocalStart = startsAt.LocalDateTime;
            var now = DateTime.Now;
            if (appointmentLocalStart.Date < now.Date)
            {
                throw new LocalApiException((int)HttpStatusCode.BadRequest, "No se pueden crear citas en días pasados.");
            }

            if (appointmentLocalStart.Date == now.Date && startsAt <= DateTimeOffset.Now)
            {
                throw new LocalApiException((int)HttpStatusCode.BadRequest, "No se pueden crear citas en una hora que ya ha pasado.");
            }
        }

        if (state.Workers.All(worker => worker.Id != workerId))
        {
            throw new LocalApiException((int)HttpStatusCode.BadRequest, "El trabajador seleccionado ya no está disponible.");
        }

        var service = state.Services.FirstOrDefault(candidate => candidate.Id == serviceId)
            ?? throw new LocalApiException((int)HttpStatusCode.BadRequest, "El servicio seleccionado ya no está disponible.");
        var end = startsAt.AddMinutes(service.DurationMinutes);
        var localStart = startsAt.LocalDateTime;
        var isWeekend = localStart.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var isHoliday = state.Holidays.Any(holiday =>
            DateOnly.FromDateTime(holiday.Date.LocalDateTime) == DateOnly.FromDateTime(localStart));
        var workDays = state.Settings.WorkDays
            .Select(day => Enum.Parse<DayOfWeek>(day, true))
            .ToHashSet();

        if (!workDays.Contains(localStart.DayOfWeek) && !isWeekend && !isHoliday)
        {
            throw new LocalApiException((int)HttpStatusCode.BadRequest, "Ese día no está configurado como jornada laboral.");
        }

        var localEnd = end.LocalDateTime;
        if (TimeOnly.FromDateTime(localStart) < state.Settings.StartTime ||
            TimeOnly.FromDateTime(localEnd) > state.Settings.EndTime)
        {
            throw new LocalApiException((int)HttpStatusCode.BadRequest, "La cita queda fuera del horario de trabajo.");
        }

        if (localStart.Minute % 15 != 0 || localStart.Second != 0 || localStart.Millisecond != 0)
        {
            throw new LocalApiException((int)HttpStatusCode.BadRequest, "Las citas deben comenzar en una franja de 15 minutos.");
        }

        if (state.Appointments.Any(existing =>
                existing.Id != appointmentId &&
                existing.WorkerId == workerId &&
                existing.StartsAt < end &&
                existing.EndsAt > startsAt))
        {
            throw new LocalApiException(
                (int)HttpStatusCode.Conflict,
                "Ese trabajador ya tiene una cita que se solapa con ese horario.");
        }

        var saved = new AppointmentResponse(
            appointmentId ?? state.NextAppointmentId++,
            customerName.Trim(),
            phone.Trim(),
            workerId,
            serviceId,
            service.Name,
            service.DurationMinutes,
            startsAt,
            end);

        if (appointmentId is null)
        {
            state.Appointments.Add(saved);
        }
        else
        {
            var index = state.Appointments.FindIndex(appointment => appointment.Id == appointmentId);
            state.Appointments[index] = saved;
        }

        return saved;
    }

    private static int MoveAppointments(LocalScheduleState state, MoveAppointmentsRequest request)
    {
        var ids = request.AppointmentIds.Distinct().ToHashSet();
        if (ids.Count == 0 || ids.Any(id => id <= 0))
        {
            throw new LocalApiException((int)HttpStatusCode.BadRequest, "Selecciona al menos una cita válida.");
        }

        var selected = state.Appointments.Where(appointment => ids.Contains(appointment.Id)).ToArray();
        if (selected.Length != ids.Count)
        {
            throw new LocalApiException((int)HttpStatusCode.NotFound, "No se encontraron todas las citas seleccionadas.");
        }

        var target = request.FirstAppointmentStartsAt;
        var localTarget = target.LocalDateTime;
        if (localTarget.Second != 0 || localTarget.Millisecond != 0 || localTarget.Minute % 15 != 0)
        {
            throw new LocalApiException((int)HttpStatusCode.BadRequest, "Las citas deben comenzar en una franja de 15 minutos.");
        }

        var offset = target - selected.MinBy(appointment => appointment.StartsAt)!.StartsAt;
        var moved = selected
            .Select(appointment => appointment with
            {
                StartsAt = appointment.StartsAt.Add(offset),
                EndsAt = appointment.EndsAt.Add(offset)
            })
            .ToArray();
        var workDays = state.Settings.WorkDays
            .Select(day => Enum.Parse<DayOfWeek>(day, true))
            .ToHashSet();
        var holidays = state.Holidays
            .Select(holiday => DateOnly.FromDateTime(holiday.Date.LocalDateTime))
            .ToHashSet();

        foreach (var appointment in moved)
        {
            var start = appointment.StartsAt.LocalDateTime;
            var end = appointment.EndsAt.LocalDateTime;
            var isWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var isHoliday = holidays.Contains(DateOnly.FromDateTime(start));
            if (!workDays.Contains(start.DayOfWeek) && !isWeekend && !isHoliday)
            {
                throw new LocalApiException(
                    (int)HttpStatusCode.BadRequest,
                    "El nuevo horario incluye un día no laborable que no es festivo ni fin de semana.");
            }

            if (TimeOnly.FromDateTime(start) < state.Settings.StartTime ||
                TimeOnly.FromDateTime(end) > state.Settings.EndTime)
            {
                throw new LocalApiException((int)HttpStatusCode.BadRequest, "El nuevo horario queda fuera de la jornada laboral.");
            }
        }

        for (var first = 0; first < moved.Length; first++)
        {
            for (var second = first + 1; second < moved.Length; second++)
            {
                if (Overlaps(moved[first], moved[second]))
                {
                    throw new LocalApiException(
                        (int)HttpStatusCode.Conflict,
                        "Las citas seleccionadas se solaparían entre sí al moverlas.");
                }
            }
        }

        foreach (var appointment in moved)
        {
            if (state.Appointments.Any(existing =>
                    !ids.Contains(existing.Id) &&
                    existing.WorkerId == appointment.WorkerId &&
                    Overlaps(existing, appointment)))
            {
                throw new LocalApiException(
                    (int)HttpStatusCode.Conflict,
                    "Ese trabajador ya tiene una cita que se solapa con ese horario.");
            }
        }

        for (var index = 0; index < selected.Length; index++)
        {
            var appointmentIndex = state.Appointments.FindIndex(appointment => appointment.Id == moved[index].Id);
            state.Appointments[appointmentIndex] = moved[index];
        }

        return moved.Length;
    }

    private static bool Overlaps(AppointmentResponse first, AppointmentResponse second)
        => first.StartsAt < second.EndsAt && first.EndsAt > second.StartsAt;

    private static ServiceResponse CreateService(LocalScheduleState state, SaveServiceRequest request)
    {
        ValidateService(state, null, request);
        var saved = new ServiceResponse(state.NextServiceId++, request.Name.Trim(), request.DurationMinutes);
        state.Services.Add(saved);
        return saved;
    }

    private static ServiceResponse SaveService(LocalScheduleState state, int id, SaveServiceRequest request)
    {
        ValidateService(state, id, request);
        var saved = new ServiceResponse(id, request.Name.Trim(), request.DurationMinutes);
        var index = state.Services.FindIndex(service => service.Id == id);
        state.Services[index] = saved;
        return saved;
    }

    private static void ValidateService(LocalScheduleState state, int? id, SaveServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new LocalApiException((int)HttpStatusCode.BadRequest, "Escribe un nombre para el servicio.");
        }

        if (request.DurationMinutes < 15 || request.DurationMinutes > 480 || request.DurationMinutes % 15 != 0)
        {
            throw new LocalApiException(
                (int)HttpStatusCode.BadRequest,
                "La duración debe ser de 15 a 480 minutos y múltiplo de 15.");
        }

        var trimmedName = request.Name.Trim();
        if (state.Services.Any(service =>
                service.Id != id &&
                string.Equals(service.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new LocalApiException((int)HttpStatusCode.Conflict, "Ya existe un servicio con ese nombre.");
        }
    }

    private static string ValidateColor(string color)
    {
        var trimmed = color.Trim();
        if (trimmed.Length != 7 || trimmed[0] != '#' || trimmed.Skip(1).Any(character => !Uri.IsHexDigit(character)))
        {
            throw new LocalApiException((int)HttpStatusCode.BadRequest, "El color debe estar en formato hexadecimal #RRGGBB.");
        }

        return trimmed.ToUpperInvariant();
    }

    private static AppointmentResponse MapAppointment(LocalScheduleState state, AppointmentResponse appointment)
    {
        var serviceName = state.Services.FirstOrDefault(service => service.Id == appointment.ServiceId)?.Name
            ?? appointment.ServiceName;
        return appointment with { ServiceName = serviceName };
    }

    private async Task<LocalScheduleState> LoadStateAsync(CancellationToken cancellationToken)
    {
        var json = await jsRuntime.InvokeAsync<string?>("citasppStorage.readSnapshot", cancellationToken);
        if (json is null)
        {
            var initial = LocalScheduleState.CreateDefault();
            await SaveStateAsync(initial, cancellationToken);
            return initial;
        }

        return JsonSerializer.Deserialize<LocalScheduleState>(json, JsonOptions)
            ?? throw new JsonException("El almacenamiento local de Citaspp está vacío o no es válido.");
    }

    private Task SaveStateAsync(LocalScheduleState state, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        return jsRuntime.InvokeVoidAsync("citasppStorage.writeSnapshot", cancellationToken, json).AsTask();
    }

    private static async Task<T> ReadBodyAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = request.Content;
        if (content is null)
        {
            throw new LocalApiException((int)HttpStatusCode.BadRequest, "Falta el contenido de la solicitud.");
        }

        var json = await content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new LocalApiException((int)HttpStatusCode.BadRequest, "La solicitud no es válida.");
    }

    private static void ValidateRequest<T>(T request) where T : class
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true))
        {
            var message = results.FirstOrDefault()?.ErrorMessage ?? "La solicitud no es válida.";
            throw new LocalApiException((int)HttpStatusCode.BadRequest, message);
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            result[Uri.UnescapeDataString(parts[0])] =
                parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
        }

        return result;
    }

    private static DateTimeOffset ToLocalDateOffset(DateOnly date)
    {
        var localDate = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Local);
        return new DateTimeOffset(localDate);
    }

    private static HttpResponseMessage CreateResponse(HttpRequestMessage request, RouteResult result)
    {
        var response = new HttpResponseMessage((HttpStatusCode)result.StatusCode)
        {
            RequestMessage = request
        };
        if (result.Body is not null)
        {
            var json = JsonSerializer.Serialize(result.Body, JsonOptions);
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (result.Location is not null)
        {
            response.Headers.Location = new Uri(result.Location, UriKind.Relative);
        }

        return response;
    }

    private static HttpResponseMessage CreateProblemResponse(
        HttpRequestMessage request,
        int statusCode,
        string detail)
        => CreateResponse(request, new RouteResult(
            statusCode,
            new LocalProblemDetails("Solicitud no válida", detail, statusCode)));

    private sealed record RouteResult(int StatusCode, object? Body = null, string? Location = null, bool Persist = false);

    private sealed record LocalProblemDetails(string Title, string Detail, int Status);

    private sealed class LocalApiException(int statusCode, string message) : Exception(message)
    {
        public int StatusCode { get; } = statusCode;
    }

    private sealed class LocalScheduleState
    {
        public LocalScheduleState()
        {
        }

        public ScheduleSettingsResponse Settings { get; set; } =
            new(new TimeOnly(9, 0), new TimeOnly(17, 0), ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"], 2);
        public List<WorkerResponse> Workers { get; set; } = [];
        public List<ServiceResponse> Services { get; set; } = [];
        public List<HolidayResponse> Holidays { get; set; } = [];
        public List<AppointmentResponse> Appointments { get; set; } = [];
        public int NextAppointmentId { get; set; } = 1;
        public int NextServiceId { get; set; } = 3;

        public static LocalScheduleState CreateDefault()
        {
            var state = new LocalScheduleState
            {
                Workers =
                [
                    new WorkerResponse(1, "Trabajador 1", DefaultWorkerColors[0]),
                    new WorkerResponse(2, "Trabajador 2", DefaultWorkerColors[1])
                ],
                Services =
                [
                    new ServiceResponse(1, "Servicio 30 min", 30),
                    new ServiceResponse(2, "Servicio 60 min", 60)
                ],
                Holidays =
                [
                    Holiday(2026, 1, 1, "Año Nuevo"),
                    Holiday(2026, 1, 6, "Epifanía del Señor"),
                    Holiday(2026, 4, 2, "Jueves Santo"),
                    Holiday(2026, 4, 3, "Viernes Santo"),
                    Holiday(2026, 4, 23, "Día de Castilla y León"),
                    Holiday(2026, 5, 1, "Fiesta del Trabajo"),
                    Holiday(2026, 6, 24, "San Juan (local León)"),
                    Holiday(2026, 8, 15, "Asunción de la Virgen"),
                    Holiday(2026, 10, 5, "San Froilán (local León)"),
                    Holiday(2026, 10, 12, "Fiesta Nacional de España"),
                    Holiday(2026, 11, 2, "Traslado de Todos los Santos"),
                    Holiday(2026, 12, 7, "Traslado del Día de la Constitución"),
                    Holiday(2026, 12, 8, "Inmaculada Concepción"),
                    Holiday(2026, 12, 25, "Navidad")
                ]
            };
            return state;
        }

        private static HolidayResponse Holiday(int year, int month, int day, string name)
            => new(ToLocalDateOffset(new DateOnly(year, month, day)), name);
    }
}
