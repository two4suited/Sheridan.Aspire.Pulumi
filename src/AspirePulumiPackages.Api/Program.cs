var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Hello from aspire.pulumi.packages");
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
