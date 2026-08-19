using RedStar.Controller;

var builder = WebApplication.CreateBuilder(args);

// appsettings.json -> appsettings.local.json -> environment variables, same layering order as
// RedStar.Cli's RedStarOptionsFactory. WebApplication.CreateBuilder already wires appsettings.json,
// appsettings.{Environment}.json, and environment variables; only appsettings.local.json needs adding.
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);

builder.Services.Configure<LmStudioOptions>(builder.Configuration.GetSection(LmStudioOptions.SectionName));
builder.Services.AddHttpClient<ILmStudioGateway, LmStudioGateway>();
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();