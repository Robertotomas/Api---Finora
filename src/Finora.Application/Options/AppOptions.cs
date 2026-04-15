namespace Finora.Application.Options;

public class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Public URL of the web app (e.g. https://app.example.com) for invite links.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5173";
}
