using BPSR_SharpCombat.Components;
using BPSR_SharpCombat.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add packet capture and combat data services
builder.Services.AddSingleton<PacketProcessor>();
builder.Services.AddSingleton<PacketCaptureService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PacketCaptureService>());
builder.Services.AddSingleton<CombatDataService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CombatDataService>());

// Add player cache for name lookups
builder.Services.AddSingleton<PlayerCache>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.MapDefaultEndpoints();

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