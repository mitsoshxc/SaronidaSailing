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

    public static UiString From { get; } = new("Από", "From");

    public static UiString Details { get; } = new("Λεπτομέρειες", "Details");

    public static UiString Dates { get; } = new("Ημερομηνίες", "Dates");

    public static UiString FollowOnInstagram { get; } = new("Ακολουθήστε μας στο Instagram", "Follow us on Instagram");

    public static UiString AllTrips { get; } = new("Όλες οι εκδρομές", "All trips");

    public static UiString TripType { get; } = new("Τύπος εκδρομής", "Trip type");

    public static UiString TripDayLong { get; } = new("Ημερήσια κρουαζιέρα", "Day cruise");

    public static UiString TripDayShort { get; } = new("Ημερήσια", "Day");

    public static UiString TripIslandsLong { get; } = new("Νησιά 2–3 ημερών", "Islands, 2–3 days");

    public static UiString TripIslandsShort { get; } = new("Νησιά", "Islands");

    public static UiString TripWeekLong { get; } = new("Εβδομαδιαία ναύλωση", "Weekly charter");

    public static UiString TripWeekShort { get; } = new("Εβδομαδιαία", "Week");

    public static UiString AllPhotos { get; } = new("Όλες οι φωτογραφίες", "All photos");

    public static UiString Photos { get; } = new("Φωτογραφίες", "Photos");

    public static UiString Close { get; } = new("Κλείσιμο", "Close");

    public static UiString PreviousPhoto { get; } = new("Προηγούμενη φωτογραφία", "Previous photo");

    public static UiString NextPhoto { get; } = new("Επόμενη φωτογραφία", "Next photo");

    public static UiString Back { get; } = new("Πίσω", "Back");

    public static UiString CookieSettings { get; } = new("Ρυθμίσεις cookies", "Cookie settings");

    public static UiString MapTitle { get; } = new(
        "Χάρτης Google με το σημείο αναχώρησης",
        "Google map showing the departure point");

    public static UiString CookiesReject { get; } = new("Απόρριψη", "Reject");

    public static UiString CookiesAccept { get; } = new("Αποδοχή", "Accept");

    public static UiString CookiesChoose { get; } = new("Επιλογή ανά κατηγορία", "Choose by category");

    public static UiString CookiesRejectAll { get; } = new("Απόρριψη όλων", "Reject all");

    public static UiString CookiesSave { get; } = new("Αποθήκευση", "Save");

    public static UiString CookiesAcceptAll { get; } = new("Αποδοχή όλων", "Accept all");

    public static UiString CookiesAlwaysOn { get; } = new("Πάντα ενεργά", "Always on");

    public static UiString CookiesAccepted { get; } = new("Αποδεχτήκατε όλα τα cookies.", "You accepted all cookies.");

    public static UiString CookiesRejected { get; } = new(
        "Απορρίψατε τα προαιρετικά cookies.",
        "You rejected the optional cookies.");

    public static UiString CookiesSaved { get; } = new("Οι επιλογές σας αποθηκεύτηκαν.", "Your choices were saved.");

    public static UiString CookiesChangeHint { get; } = new(
        "Αλλάξτε την επιλογή σας από τις «Ρυθμίσεις cookies» στο κάτω μέρος κάθε σελίδας.",
        "Change your choice from “Cookie settings” at the bottom of every page.");

    public static UiString CookiesChange { get; } = new("Αλλαγή", "Change");

    public static UiString HeroIllustration { get; } = new(
        "Σχέδιο καταμαράν Lagoon με πανιά στο ηλιοβασίλεμα",
        "Drawing of a Lagoon catamaran under sail at sunset");

    public static UiString OnThisPage { get; } = new("Σε αυτή τη σελίδα", "On this page");

    public static UiString SeeAlso { get; } = new("Δείτε επίσης", "See also");

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
        (nameof(From), From),
        (nameof(Details), Details),
        (nameof(Dates), Dates),
        (nameof(FollowOnInstagram), FollowOnInstagram),
        (nameof(AllTrips), AllTrips),
        (nameof(AllPhotos), AllPhotos),
        (nameof(Photos), Photos),
        (nameof(Close), Close),
        (nameof(PreviousPhoto), PreviousPhoto),
        (nameof(NextPhoto), NextPhoto),
        (nameof(HeroIllustration), HeroIllustration),
        (nameof(Back), Back),
        (nameof(CookieSettings), CookieSettings),
        (nameof(MapTitle), MapTitle),
        (nameof(CookiesReject), CookiesReject),
        (nameof(CookiesAccept), CookiesAccept),
        (nameof(CookiesChoose), CookiesChoose),
        (nameof(CookiesRejectAll), CookiesRejectAll),
        (nameof(CookiesSave), CookiesSave),
        (nameof(CookiesAcceptAll), CookiesAcceptAll),
        (nameof(CookiesAlwaysOn), CookiesAlwaysOn),
        (nameof(CookiesAccepted), CookiesAccepted),
        (nameof(CookiesRejected), CookiesRejected),
        (nameof(CookiesSaved), CookiesSaved),
        (nameof(CookiesChangeHint), CookiesChangeHint),
        (nameof(CookiesChange), CookiesChange),
        (nameof(TripType), TripType),
        (nameof(TripDayLong), TripDayLong),
        (nameof(TripDayShort), TripDayShort),
        (nameof(TripIslandsLong), TripIslandsLong),
        (nameof(TripIslandsShort), TripIslandsShort),
        (nameof(TripWeekLong), TripWeekLong),
        (nameof(TripWeekShort), TripWeekShort),
        (nameof(OnThisPage), OnThisPage),
        (nameof(SeeAlso), SeeAlso),
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
