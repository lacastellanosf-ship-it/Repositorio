using Citaspp.Web.Contracts;

namespace Citaspp.Web.Services;

public interface IAppointmentService
{
    AppointmentResponse[] GetAppointments(DateTimeOffset start, DateTimeOffset end);
    AppointmentResponse? GetAppointment(int id);
    AppointmentResponse CreateAppointment(CreateAppointmentRequest request);
    AppointmentResponse? UpdateAppointment(int id, UpdateAppointmentRequest request);
    bool DeleteAppointment(int id);
    int DeleteAppointments(int[] appointmentIds);
    int MoveAppointments(MoveAppointmentsRequest request);
    WorkerResponse[] GetWorkers();
    ServiceResponse[] GetServices();
    HolidayResponse[] GetHolidays();
    ScheduleSettingsResponse GetSettings();
    void UpdateSettings(UpdateScheduleSettingsRequest request);
    WorkerResponse? UpdateWorker(int id, UpdateWorkerRequest request);
    ServiceResponse CreateService(SaveServiceRequest request);
    ServiceResponse? UpdateService(int id, SaveServiceRequest request);
    bool DeleteService(int id);
    HolidayResponse SaveHoliday(DateOnly date, SaveHolidayRequest request);
    bool DeleteHoliday(DateOnly date);
}
