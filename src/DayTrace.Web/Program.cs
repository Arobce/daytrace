// M1 scaffold only: endpoints, categorizer and aggregation arrive in M4.
// Binds to 127.0.0.1 only (CLAUDE.md rule 6); never a wildcard.
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5217");

var app = builder.Build();
app.MapGet("/api/health", () => Results.Json(new { app = "DayTrace", version = "1.0.0" }));
app.Run();
