using BPSR_SharpCombat.Components;
using BPSR_SharpCombat.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Settings service for persistent configuration (must be before services that depend on it)
builder.Services.AddSingleton<SettingsService>();

// Add player cache for name lookups
builder.Services.AddSingleton<PlayerCache>();

// Add packet capture and combat data services
builder.Services.AddSingleton<PacketProcessor>();
builder.Services.AddSingleton<PacketCaptureService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PacketCaptureService>());
builder.Services.AddSingleton<EncounterService>();
builder.Services.AddSingleton<CombatDataService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CombatDataService>());


// Skill name translation service (loads wwwroot/data/skills_en.json)
builder.Services.AddSingleton<SkillNameService>();

// Window manager service used by the renderer to request new windows and read persisted window state
// This service depends on IJSRuntime (scoped) so register it as scoped
builder.Services.AddScoped<WindowManagerService>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.MapDefaultEndpoints();

// Expose a small shutdown endpoint so external hosts (like the Electron wrapper) can request a graceful stop.
app.MapPost("/api/host/shutdown", () => {
    // request host to stop; this returns quickly while shutdown proceeds
    app.Lifetime.StopApplication();
    return Results.Accepted();
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();