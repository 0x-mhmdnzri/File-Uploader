using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApp.Pages;

public class IndexModel : PageModel
{
    public string ApiBase { get; }
    public string ApiKey { get; }

    public IndexModel(IConfiguration cfg)
    {
        // Must match WebApi Properties/launchSettings.json "http" profile.
        ApiBase = cfg["ApiBase"] ?? "http://localhost:5073";
        ApiKey = cfg["ApiKey"] ?? "";
    }

    public void OnGet()
    {
    }
}
