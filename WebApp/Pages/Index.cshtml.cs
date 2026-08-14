using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApp.Pages;

public class IndexModel : PageModel
{
    public string ApiBase { get; }
    public string ApiKey { get; }

    public IndexModel(IConfiguration cfg)
    {
        ApiBase = cfg["ApiBase"] ?? "https://localhost:7013";
        ApiKey = cfg["ApiKey"] ?? "";
    }

    public void OnGet()
    {
    }
}
