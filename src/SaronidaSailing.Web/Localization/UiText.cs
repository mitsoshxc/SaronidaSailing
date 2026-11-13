using System.Globalization;
using SaronidaSailing.Core.Services;

namespace SaronidaSailing.Web.Localization;

public sealed record UiString(string Greek, string English)
{
    public string For(string locale) => locale == ContentLocales.English ? English : Greek;

    public string Current => For(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
}

public sealed record LanguageLinkText(string Code, string Lang, string Name);

public static class UiText
{
    public static string OpensInNewTab(string locale) =>
        locale == ContentLocales.English ? " (opens in a new tab)" : " (ανοίγει σε νέα καρτέλα)";

    public static UiString Home { get; } = new("Αρχική", "Home");

    public static UiString TripsMenu { get; } = new("Εκδρομές", "Trips");

    public static UiString TripsTitle { get; } = new("Εκδρομές & κράτηση", "Trips & booking");

    public static UiString Boat { get; } = new("Το σκάφος", "The boat");

    public static UiString Contact { get; } = new("Επικοινωνία", "Contact");

    public static UiString Terms { get; } = new("Όροι", "Terms");

    public static UiString Privacy { get; } = new("Πολιτική απορρήτου", "Privacy policy");

    public static UiString Accessibility { get; } = new("Προσβασιμότητα", "Accessibility");

    public static UiString InstagramMenu { get; } = new("Instagram", "Instagram");

    public static UiString Book { get; } = new("Κράτηση", "Book");

    public static UiString SkipLink { get; } = new("Μετάβαση στο περιεχόμενο", "Skip to content");

    public static UiString MainNavigation { get; } = new("Κύρια πλοήγηση", "Main navigation");

    public static UiString FooterPages { get; } = new("Σελίδες", "Pages");

    public static UiString FooterContact { get; } = new("Επικοινωνία", "Contact");

    public static UiString FooterSocial { get; } = new("Ακολουθήστε μας", "Follow us");

    public static UiString MenuOpen { get; } = new("Άνοιγμα μενού", "Open menu");

    public static UiString MenuClose { get; } = new("Κλείσιμο μενού", "Close menu");

    public static UiString BackToHome { get; } = new("Πίσω στην αρχική", "Back to home");

    public static UiString LogoLink { get; } = new("Saronida Sailing, αρχική σελίδα", "Saronida Sailing, home page");

    public static UiString BackToTop { get; } = new("Επιστροφή στην αρχή της σελίδας", "Back to top of page");

    public static IReadOnlyList<(string Name, UiString Text)> All { get; } =
    [
        (nameof(Home), Home),
        (nameof(TripsMenu), TripsMenu),
        (nameof(TripsTitle), TripsTitle),
        (nameof(Boat), Boat),
        (nameof(Contact), Contact),
        (nameof(Terms), Terms),
        (nameof(Privacy), Privacy),
        (nameof(Accessibility), Accessibility),
        (nameof(InstagramMenu), InstagramMenu),
        (nameof(Book), Book),
        (nameof(SkipLink), SkipLink),
        (nameof(MainNavigation), MainNavigation),
        (nameof(FooterPages), FooterPages),
        (nameof(FooterContact), FooterContact),
        (nameof(FooterSocial), FooterSocial),
        (nameof(MenuOpen), MenuOpen),
        (nameof(MenuClose), MenuClose),
        (nameof(BackToHome), BackToHome),
        (nameof(LogoLink), LogoLink),
        (nameof(BackToTop), BackToTop),
    ];

    public static UiString MenuName(string pageKey) => pageKey switch
    {
        "home" => Home,
        "trips" => TripsMenu,
        "boat" => Boat,
        "contact" => Contact,
        "terms" => Terms,
        "privacy" => Privacy,
        "accessibility" => Accessibility,
        _ => throw new ArgumentException($"Unknown page key '{pageKey}'.", nameof(pageKey)),
    };

    public static UiString PageTitle(string pageKey) => pageKey == "trips" ? TripsTitle : MenuName(pageKey);

    public static LanguageLinkText LanguageLink(string currentLocale) =>
        currentLocale == ContentLocales.English
            ? new LanguageLinkText("ΕΛ", ContentLocales.Greek, "Ελληνική έκδοση")
            : new LanguageLinkText("EN", ContentLocales.English, "English version");
}
