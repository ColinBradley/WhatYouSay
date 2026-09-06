using Microsoft.EntityFrameworkCore;
using WhatYouSay.Web.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Api;
using WhatYouSay.Web.Telemetry;
using WhatYouSay.Web.Live;
using WhatYouSay.Data.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<WhatYouSayContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("WhatYouSay")));

builder.Services.AddScoped<TopicService>();
builder.Services.AddScoped<ResponseService>();
builder.Services.AddScoped<SummaryService>();
builder.Services.AddScoped<SummaryEditService>();
builder.Services.AddScoped<ReactionService>();
builder.Services.AddScoped<CommentService>();
builder.Services.AddScoped<TopicAdminService>();
builder.Services.AddScoped<AdminSession>();

builder.Services.AddSingleton<SummaryLiveUpdates>();

builder.Services.AddWhatYouSayTelemetry();

// AdminSession reads the request cookie through this; the summariser API does not need
// it, since the request reaches its handlers directly.
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<SummariserSession>();
builder.Services.AddProblemDetails();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WhatYouSayContext>();
    db.Database.Migrate();

    if (app.Environment.IsDevelopment())
    {
        await SeedData.EnsureSeededAsync(db);
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapSummariserApi();

// Listed in the routes but never built: /topics is the collection, 
// and the collection is what the home page shows.
app.MapGet("/topics", () => Results.Redirect("/", permanent: true));

app.MapGet("/topics/{code}/summary", (string code) =>
    Results.Redirect($"/topics/{code}", permanent: true));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
