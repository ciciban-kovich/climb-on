namespace ClimbOn.Infrastructure.Persistence.Accounts;

// Version 1 of the terms and the privacy policy, published by the migration that creates
// legal_documents. A published version is never edited (S112); a change is a new version (S114).
// Line endings are fixed, so the model matches the migration on a CRLF checkout too.
public static class LegalDocumentSeed
{
    public static readonly DateTimeOffset PublishedAt = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<LegalDocumentRow> Rows { get; } =
    [
        new()
        {
            Document = "terms",
            Version = 1,
            PublishedAt = PublishedAt,
            TextEnglish =
                """
                Terms of service, version 1

                ClimbOn helps climbers in Norway find climbing partners. To register you must be at least 18 years old.

                You are responsible for what you write and for how you meet and climb with others. Belay certifications shown in the app are self-declared and not checked by ClimbOn. Check your partner's belay competence yourself, indoors and outdoors.

                Do not harass others, create fake profiles or behave unsafely. Profiles and messages can be reported, and ClimbOn may warn or suspend an account that breaks these terms.

                You can delete your account at any time in the app.

                The service is provided as is, without guarantees of availability.
                """.ReplaceLineEndings("\n"),
            TextBokmal =
                """
                Brukervilkår, versjon 1

                ClimbOn hjelper klatrere i Norge med å finne klatrepartnere. Du må være minst 18 år for å registrere deg.

                Du er ansvarlig for det du skriver og for hvordan du møter og klatrer med andre. Brattkort som vises i appen er oppgitt av klatreren selv og kontrolleres ikke av ClimbOn. Sjekk selv sikringskompetansen til partneren din, både inne og ute.

                Ikke trakaser andre, opprett falske profiler eller oppfør deg utrygt. Profiler og meldinger kan rapporteres, og ClimbOn kan advare eller suspendere en konto som bryter disse vilkårene.

                Du kan slette kontoen din når som helst i appen.

                Tjenesten leveres som den er, uten garanti for tilgjengelighet.
                """.ReplaceLineEndings("\n"),
        },
        new()
        {
            Document = "privacy",
            Version = 1,
            PublishedAt = PublishedAt,
            TextEnglish =
                """
                Privacy policy, version 1

                ClimbOn processes the data you give it to suggest climbing partners, to let you invite and chat with them, and to notify you. The legal basis is the performance of our agreement with you.

                We store your email address, password hash, date of birth, language, profile, availability, invites, connections and the messages you write. Your date of birth is used only to check that you are at least 18; it is not shown to other climbers. Your email address is never shown to other climbers.

                Data is stored in the EEA. Email is sent through a provider in the EEA.

                You can download your data and delete your account in the app. After a deletion request your account is erased after 30 days.
                """.ReplaceLineEndings("\n"),
            TextBokmal =
                """
                Personvernerklæring, versjon 1

                ClimbOn behandler opplysningene du gir for å foreslå klatrepartnere, la deg invitere og chatte med dem, og varsle deg. Behandlingsgrunnlaget er oppfyllelse av avtalen med deg.

                Vi lagrer e-postadressen din, passord-hash, fødselsdato, språk, profil, tilgjengelighet, invitasjoner, forbindelser og meldingene du skriver. Fødselsdatoen brukes bare til å sjekke at du er minst 18 år; den vises ikke til andre klatrere. E-postadressen din vises aldri til andre klatrere.

                Opplysningene lagres i EØS. E-post sendes gjennom en leverandør i EØS.

                Du kan laste ned opplysningene dine og slette kontoen din i appen. Etter en sletteforespørsel slettes kontoen etter 30 dager.
                """.ReplaceLineEndings("\n"),
        },
    ];
}
