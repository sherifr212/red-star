using Vite.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddViteServices();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebSockets();
    // Before UseStaticFiles: wwwroot/dist doesn't exist in Development (it's Publish-only
    // output, see RedStar.WebApp.csproj's ViteProductionBuild target), so static-file middleware
    // must not get first crack at asset requests -- they need to reach the Vite dev-server proxy.
    app.UseViteDevelopmentServer();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
