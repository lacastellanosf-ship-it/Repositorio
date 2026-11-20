using System.Globalization;
using Citaspp.Web.Contracts;
using Microsoft.JSInterop;

namespace Citaspp.Web.Client;

public sealed class FirebaseClient(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private IJSObjectReference? module;

    public bool IsConfigured { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public string? UserEmail { get; private set; }
    public event Action? StateChanged;

    public async Task InitializeAsync()
    {
        var result = await (await GetModuleAsync()).InvokeAsync<FirebaseStatus>("initialize");
        IsConfigured = result.Configured;
        IsAuthenticated = result.Authenticated;
        UserEmail = result.Email;
        StateChanged?.Invoke();
    }

    public async Task<int> SignInAsync(string email, string password)
    {
        var result = await (await GetModuleAsync())
            .InvokeAsync<FirebaseSignInResult>("signIn", email, password);
        IsAuthenticated = true;
        UserEmail = result.Email;
        StateChanged?.Invoke();
        return result.MigratedAppointments;
    }

    public async Task SignOutAsync()
    {
        await (await GetModuleAsync()).InvokeVoidAsync("signOut");
        IsAuthenticated = false;
        UserEmail = null;
        StateChanged?.Invoke();
    }

    public async Task<AppointmentResponse[]> GetAppointmentsAsync(DateTimeOffset start, DateTimeOffset end)
        => await (await GetModuleAsync()).InvokeAsync<AppointmentResponse[]>(
            "getAppointments",
            start.ToString("O", CultureInfo.InvariantCulture),
            end.ToString("O", CultureInfo.InvariantCulture));

    public async Task<AppointmentResponse[]> GetAllAppointmentsAsync()
        => await (await GetModuleAsync()).InvokeAsync<AppointmentResponse[]>("getAllAppointments");

    public async Task<AppointmentResponse?> GetAppointmentAsync(int id)
        => await (await GetModuleAsync()).InvokeAsync<AppointmentResponse?>("getAppointment", id);

    public async Task<int> CreateAppointmentAsync(AppointmentResponse appointment)
        => await (await GetModuleAsync()).InvokeAsync<int>("createAppointment", appointment);

    public async Task UpdateAppointmentAsync(AppointmentResponse appointment)
        => await (await GetModuleAsync()).InvokeVoidAsync("updateAppointment", appointment);

    public async Task DeleteAppointmentAsync(int id)
        => await (await GetModuleAsync()).InvokeVoidAsync("deleteAppointment", id);

    public async Task DeleteAppointmentsAsync(int[] ids)
        => await (await GetModuleAsync()).InvokeVoidAsync("deleteAppointments", ids);

    public async Task SaveMovedAppointmentsAsync(AppointmentResponse[] appointments)
        => await (await GetModuleAsync()).InvokeVoidAsync("saveMovedAppointments", appointments);

    public async Task<int> SubscribeAppointmentsAsync<T>(
        DateTimeOffset start,
        DateTimeOffset end,
        DotNetObjectReference<T> callback)
        where T : class
        => await (await GetModuleAsync()).InvokeAsync<int>(
            "subscribeAppointments",
            start.ToString("O", CultureInfo.InvariantCulture),
            end.ToString("O", CultureInfo.InvariantCulture),
            callback);

    public async Task UnsubscribeAppointmentsAsync(int subscriptionId)
        => await (await GetModuleAsync()).InvokeVoidAsync("unsubscribeAppointments", subscriptionId);

    public static string UserFriendlyError(Exception exception)
    {
        var message = exception.Message;
        if (message.Contains("auth/invalid-credential", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("auth/wrong-password", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("auth/user-not-found", StringComparison.OrdinalIgnoreCase))
        {
            return "El correo o la contraseña no son correctos.";
        }

        if (message.Contains("auth/too-many-requests", StringComparison.OrdinalIgnoreCase))
        {
            return "Se han realizado demasiados intentos. Espera un momento y vuelve a intentarlo.";
        }

        if (message.Contains("permission-denied", StringComparison.OrdinalIgnoreCase))
        {
            return "Firebase rechazó el acceso. Comprueba que tu UID esté autorizado en firestore.rules.";
        }

        return "No se pudo conectar con Firebase. Comprueba la configuración y las reglas de Firestore.";
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            await module.DisposeAsync();
        }
    }

    private async ValueTask<IJSObjectReference> GetModuleAsync()
    {
        module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./firebase-client.js");
        return module;
    }

    public sealed record FirebaseStatus(bool Configured, bool Authenticated, string? Email);
    public sealed record FirebaseSignInResult(string? Email, int MigratedAppointments);
}
