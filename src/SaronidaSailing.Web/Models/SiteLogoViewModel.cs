namespace SaronidaSailing.Web.Models;

public sealed record SiteLogoViewModel(string IdPrefix, bool Dark, string Href, string Label, string? FocusMarker = null);
