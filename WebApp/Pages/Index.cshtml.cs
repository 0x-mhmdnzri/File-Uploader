using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApp.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    public string ApiBase { get; }

    public IndexModel(ILogger<IndexModel> logger, IConfiguration cfg)
    {
        _logger = logger;
        ApiBase = cfg["ApiBase"] ?? "https://localhost:7013";
    }

    public void OnGet()
    {
    }
}