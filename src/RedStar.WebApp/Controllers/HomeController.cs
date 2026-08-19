using Microsoft.AspNetCore.Mvc;

namespace RedStar.WebApp.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();
}