using Citaspp;
using Citaspp.Web.Client.Pages;
using Citaspp.Web.Components;
using Citaspp.Web.Endpoints;
using Citaspp.Web.Middleware;
using Citaspp.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var contractXml = Path.Combine(AppContext.BaseDirectory, "Citaspp.Web.Contracts.xml");
    options.IncludeXmlComments(contractXml);
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();

var databasePath = builder.Configuration["CITASPP_DATABASE_PATH"];
if (string.IsNullOrWhiteSpace(databasePath))
{
    databasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Citaspp",
        "citas.db");
}
else
{
    databasePath = Path.GetFullPath(databasePath);
}

var repository = new AppointmentRepository(databasePath);
repository.Initialize();
builder.Services.AddSingleton(repository);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapAppointmentEndpoints();
app.MapConfigurationEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Citaspp.Web.Client._Imports).Assembly);

app.Run();
