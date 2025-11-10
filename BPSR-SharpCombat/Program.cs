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

// Expose a simple version endpoint so the renderer can show the app version
app.MapGet("/api/host/version", () => {
    try
    {
        var entry = System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly();
        string? version = null;
        try
        {
            var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(entry.Location);
            version = fvi.ProductVersion;
        }
        catch { }

        if (string.IsNullOrWhiteSpace(version))
        {
            version = entry.GetName().Version?.ToString();
        }

        if (string.IsNullOrWhiteSpace(version)) version = "0.0.0";

        return Results.Ok(new { version });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { version = "0.0.0" });
    }
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