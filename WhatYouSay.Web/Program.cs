using Microsoft.EntityFrameworkCore;
using WhatYouSay.Web.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Mcp;
using WhatYouSay.Web.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<WhatYouSayContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("WhatYouSay")));

builder.Services.AddScoped<SurveyService>();
builder.Services.AddScoped<ResponseService>();
builder.Services.AddScoped<SummaryService>();

builder.Services.AddWhatYouSayTelemetry();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SummariserSession>();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WhatYouSayContext>();
    db.Database.Migrate();

    if (app.Environment.IsDevelopment())
    {
        await SeedData.EnsureSeededAsync(db, app.Logger);
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

// MCP has a bearer token auth of its own, and no browser form posts, so it sits outside the
// antiforgery pipeline that the Razor pages need.
app.MapMcp("/mcp");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
