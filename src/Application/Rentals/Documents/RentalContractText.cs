using System.Globalization;
using Application.Abstractions.Dossiers;
using Domain.Cars;
using Domain.Companies;
using Domain.Rentals;

namespace Application.Rentals.Documents;

/// <summary>
/// Textul contractului de închiriere și al procesului-verbal, după șablonul primit de la firmă
/// (<c>CTR inchiriere.pdf</c>), completat din ce știm deja.
/// </summary>
/// <remarks>
/// Textul e al șablonului, cuvânt cu cuvânt. Ce se schimbă: locurile goale se completează cu datele
/// firmei, ale chiriașului, ale mașinii și ale închirierii, iar unde șablonul dă variante
/// („domiciliul/sediul”, „CI / Registrul Comerțului”) rămâne cea care se potrivește chiriașului.
/// Ce nu știm — ziua de plată, penalitățile, franșiza, preavizul — rămâne linie de completat.
/// </remarks>
internal static class RentalContractText
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public static IReadOnlyList<RentalDocumentArticle> Contract(Rental rental, Car car, CompanyProfile company, Tenant tenant)
    {
        bool person = tenant.Type == TenantType.Individual;
        string companyName = CompanyName(company);
        string? extraKm = rental.ExtraKmCostBani > 0 ? Lei(rental.ExtraKmCostBani) : null;

        return
        [
            Chapter("I. PĂRȚILE CONTRACTANTE",
                P(T("1.1. "), V(companyName), T(", cu sediul în "), V(company.RegisteredOffice, "5cm"),
                    T(", înregistrată la Registrul Comerțului sub nr. "), V(company.RegCom), T(", CUI "), V(company.Cui),
                    T(", cont IBAN "), V(company.Iban, "5cm"), T(", reprezentată prin "), V(company.LegalRepresentative, "4cm"),
                    T(", în calitate de "), B("3cm"), T(", denumită în continuare Locator,")),
                P(T("și")),
                person
                    ? P(T("1.2. "), V(tenant.Name, "5cm"), T(", cu domiciliul în "), V(tenant.Address, "5cm"),
                        T(", CNP "), V(tenant.Cnp), T(", identificat(ă) cu CI seria "), V(tenant.IdSeries, "1cm"),
                        T(" nr. "), V(tenant.IdNumber, "2cm"), T(", denumit(ă) în continuare Locatar,"))
                    : P(T("1.2. "), V(tenant.Name, "5cm"), T(", cu sediul în "), V(tenant.Address, "5cm"),
                        T(", CUI "), V(tenant.Cui), T(", înregistrat(ă) la Registrul Comerțului sub nr. "), V(tenant.RegCom),
                        T(", reprezentat(ă), după caz, prin "), B("4cm"), T(", denumit(ă) în continuare Locatar,")),
                P(T("au convenit încheierea prezentului contract de închiriere, în următoarele condiții:"))),

            Chapter("II. OBIECTUL CONTRACTULUI",
                P(T("2.1. Locatorul dă în folosința Locatarului autovehiculul marca "), V(car.Brand), T(", model "), V(car.Model),
                    T(", număr de înmatriculare "), V(car.PlateNumber), T(", serie de șasiu (VIN) "), V(car.Vin, "5cm"),
                    T(", an fabricație "), V(car.Year.ToString(CultureInfo.InvariantCulture), "1.5cm"), T(", denumit în continuare Autovehicul.")),
                P(T("2.2. Autovehiculul se predă pe bază de proces-verbal de predare-primire, în care se vor menționa kilometrajul, nivelul de combustibil/starea de încărcare, documentele și accesoriile predate, precum și eventualele avarii existente.")),
                P(T("2.3. Autovehiculul va fi utilizat pentru "), V("activități de transport alternativ (ridesharing)"),
                    T(". Utilizarea pentru activități de transport alternativ/ridesharing este permisă dacă această destinație a fost convenită de părți și sunt îndeplinite condițiile prevăzute de lege."))),

            Chapter("III. DURATA CONTRACTULUI. CHIRIA. GARANȚIA",
                P(T("3.1. Contractul se încheie pentru perioada "), V(Period(rental)), T(", începând cu data de "), V(Date(rental.StartAtUtc)),
                    T(" și până la data de "), V(Date(rental.EndAtUtc)), T(".")),
                P(T("3.2. Chiria este de "), V(Lei(rental.WeeklyRentBani)), T(" lei / săptămână și se achită la data de "), B("2.5cm"),
                    T(", prin "), B("4cm"), T(".")),
                P(T("3.3. La predarea Autovehiculului, Locatarul achită o garanție în cuantum de "), V(rental.DepositBani > 0 ? Lei(rental.DepositBani) : null, "2.5cm"),
                    T(" lei. Garanția se restituie la încetarea contractului, după predarea Autovehiculului și achitarea tuturor sumelor datorate.")),
                P(T("3.4. Locatorul poate reține din garanție sumele reprezentând chirii neachitate, amenzi sau taxe aferente perioadei de folosință, lipsuri, franșize și prejudicii imputabile Locatarului, cu justificarea sumelor reținute.")),
                P(T("3.5. Pentru întârzierea la plata chiriei se datorează penalități de "), B("1.5cm"),
                    T(" % pe zi de întârziere, dacă părțile au convenit aplicarea acestora."))),

            Chapter("IV. PREDAREA ȘI FOLOSIREA AUTOVEHICULULUI",
                P(T("4.1. Autovehiculul se predă Locatarului în stare corespunzătoare de funcționare, împreună cu documentele, cheile și accesoriile menționate în procesul-verbal de predare-primire.")),
                P(T("4.2. Locatarul se obligă să folosească Autovehiculul cu prudență, potrivit destinației stabilite, cu respectarea legislației rutiere și a instrucțiunilor de exploatare.")),
                P(T("4.3. Autovehiculul poate fi condus de Locatar sau de următorul conducător autorizat: "), B("5cm"), T(", CNP "), B("3.5cm"),
                    T(", permis de conducere seria/nr. "), B("3cm"), T(". Încredințarea Autovehiculului unei alte persoane se poate face numai cu acordul Locatorului.")),
                P(T("4.4. Autovehiculul va fi utilizat pe teritoriul României. Ieșirea din țară se poate face numai cu acordul prealabil al Locatorului și cu respectarea condițiilor de asigurare.")),
                rental.HasKmLimit
                    ? P(T("4.5. Kilometrajul inclus este de "), V(rental.MileageLimit?.ToString("N0", Ro), "2.5cm"),
                        T(" km / săptămână. Pentru depășirea limitei stabilite se va achita suma de "), V(extraKm, "2cm"), T(" lei/km."))
                    : P(T("4.5. Kilometrajul inclus este "), V("nelimitat"), T(".")),
                P(T("4.6. Autovehiculul se predă cu nivelul de combustibil/starea de încărcare de "), V(rental.FuelLevelAtPickup, "2.5cm"),
                    T(" și se restituie în aceleași condiții, dacă părțile nu stabilesc altfel.")),
                P(T("4.7. Locatarului îi este interzis să subînchirieze Autovehiculul, să îl folosească la competiții, să efectueze modificări asupra acestuia fără acordul Locatorului sau să îl utilizeze în scopuri contrare legii."))),

            Chapter("V. OBLIGAȚIILE PĂRȚILOR",
                P(T("5.1. Locatorul se obligă să predea Autovehiculul în stare corespunzătoare de folosință și să asigure efectuarea reviziilor și reparațiilor determinate de uzura normală, dacă părțile nu convin altfel.")),
                P(T("5.2. Locatarul suportă cheltuielile cu combustibilul/energia, parcările, taxele de drum și amenzile rezultate din utilizarea Autovehiculului pe durata contractului.")),
                P(T("5.3. Locatarul va informa Locatorul, în cel mai scurt timp, despre orice defecțiune, martor de avertizare, avarie sau incident și nu va continua utilizarea Autovehiculului dacă aceasta poate agrava defecțiunea.")),
                P(T("5.4. În cazul în care Autovehiculul este indisponibil din cauza unei defecțiuni care nu este imputabilă Locatarului, chiria pentru perioada de indisponibilitate "), B("5cm"),
                    T(". Dacă indisponibilitatea este cauzată din culpa Locatarului, acesta nu este exonerat de obligațiile asumate, în măsura permisă de lege.")),
                P(T("5.5. Pierderea ori deteriorarea din culpa Locatarului a cheilor, documentelor sau accesoriilor predate atrage obligația acestuia de a suporta costul înlocuirii sau remedierii."))),

            Chapter("VI. ASIGURAREA. ACCIDENTELE ȘI DAUNELE",
                P(T("6.1. Autovehiculul beneficiază de asigurare RCA valabilă și, după caz, de asigurare CASCO.")),
                P(T("6.2. Franșiza aplicabilă în cazul unei daune este de "), B("3cm"),
                    T(". Aceasta va fi suportată de Locatar în cazul în care dauna îi este imputabilă, în condițiile prezentului contract și ale poliței de asigurare.")),
                P(T("6.3. În caz de accident, furt, vandalism sau alt eveniment, Locatarul va anunța de îndată Locatorul și va îndeplini formalitățile legale necesare, inclusiv constatarea amiabilă sau prezentarea la organele de poliție, după caz.")),
                P(T("6.4. Locatarul răspunde pentru prejudiciile neacoperite de asigurare atunci când acestea rezultă din fapta sa culpabilă, inclusiv conducerea sub influența alcoolului sau a substanțelor interzise, conducerea fără drept, folosirea de către o persoană neautorizată ori nerespectarea obligațiilor necesare instrumentării dosarului de daună.")),
                P(T("6.5. Eventualele daune constatate la restituire vor fi consemnate în procesul-verbal și, după caz, documentate prin fotografii și documente privind costul reparației."))),

            Chapter("VII. ÎNCETAREA CONTRACTULUI. RESTITUIREA AUTOVEHICULULUI",
                P(T("7.1. Contractul încetează la expirarea perioadei pentru care a fost încheiat, prin acordul părților sau prin denunțare, cu un preaviz de "), B("1.5cm"), T(" zile.")),
                P(T("7.2. Locatorul poate solicita încetarea contractului în cazul neplății chiriei, utilizării Autovehiculului de către persoane neautorizate, folosirii contrare destinației convenite, producerii intenționate a unor prejudicii sau încălcării grave ori repetate a obligațiilor asumate.")),
                P(T("7.3. La încetarea contractului, Locatarul este obligat să restituie Autovehiculul, documentele, cheile și accesoriile primite, la locul și data convenite, în starea în care le-a primit, mai puțin uzura normală.")),
                P(T("7.4. În cazul nerestituirii Autovehiculului la termen, Locatarul datorează contravaloarea folosinței pentru perioada de întârziere și eventualele prejudicii dovedite, fără a aduce atingere dreptului Locatorului de a solicita restituirea bunului pe căile prevăzute de lege."))),

            new RentalDocumentArticle(
                "VIII. DISPOZIȚII FINALE",
                [
                    P(T("8.1. Orice modificare a prezentului contract se face prin acordul părților, consemnat în scris sau prin act adițional.")),
                    P(T("8.2. Forța majoră exonerează partea care o invocă de răspundere pentru neexecutarea obligațiilor afectate, în condițiile legii, pe durata existenței evenimentului.")),
                    P(T("8.3. Eventualele neînțelegeri se vor soluționa pe cale amiabilă, iar în cazul în care acest lucru nu este posibil, de instanțele competente potrivit legii române.")),
                    P(T("8.4. Procesul-verbal de predare-primire și celelalte anexe semnate de părți fac parte integrantă din prezentul contract.")),
                    P(T("8.5. Prezentul contract a fost încheiat astăzi, "), V(Date(DateTime.UtcNow)),
                        T(", în două exemplare, câte unul pentru fiecare parte / prin mijloace electronice, după caz.")),
                ],
                Signatures: new(["LOCATOR", "LOCATAR"], [companyName, tenant.Name])),

            // Anexa nr. 1 stă în același PDF, pe pagină nouă, ca în șablon.
            .. Handover(rental, car, company, tenant, annex: true),
        ];
    }

    /// <summary>
    /// Procesul-verbal de predare-primire (Anexa nr. 1). <paramref name="annex" /> = pagina din
    /// contract; altfel e documentul de sine stătător, cu titlul lui.
    /// </summary>
    public static IReadOnlyList<RentalDocumentArticle> Handover(Rental rental, Car car, CompanyProfile company, Tenant tenant, bool annex)
    {
        IReadOnlyList<IReadOnlyList<RentalTextRun>> paragraphs =
        [
            P(T("Încheiat astăzi, "), V(Date(rental.StartAtUtc)), T(", în baza Contractului de închiriere nr. "), V(rental.PublicCode),
                T(" / "), V(Date(rental.CreatedAtUtc)), T(".")),
            P(T("Locatorul predă, iar Locatarul primește Autovehiculul marca "), V(car.Brand), T(", model "), V(car.Model),
                T(", nr. de înmatriculare "), V(car.PlateNumber), T(", VIN "), V(car.Vin, "5cm"), T(".")),
            .. InventoryParagraphs(rental, "la predare"),
            P(T("Prin semnarea prezentului proces-verbal, părțile confirmă predarea și primirea Autovehiculului, a documentelor, cheilor și accesoriilor menționate mai sus, în starea consemnată.")),
        ];

        return
        [
            new RentalDocumentArticle(
                annex ? "ANEXA NR. 1" : null,
                paragraphs,
                NewPage: annex,
                Heading: annex ? "PROCES-VERBAL DE PREDARE-PRIMIRE AUTOVEHICUL" : null,
                Signatures: new(["AM PREDAT, LOCATOR", "AM PRIMIT, LOCATAR"], [CompanyName(company), tenant.Name])),
        ];
    }

    /// <summary>
    /// Procesul-verbal de restituire. Șablonul are doar predarea; restituirea folosește aceeași
    /// formă, cu rolurile inversate: Locatarul predă, Locatorul primește.
    /// </summary>
    public static IReadOnlyList<RentalDocumentArticle> Return(Rental rental, Car car, CompanyProfile company, Tenant tenant) =>
    [
        new RentalDocumentArticle(
            null,
            [
                P(T("Încheiat astăzi, "), V(Date(rental.ClosedAtUtc ?? rental.EndAtUtc)), T(", în baza Contractului de închiriere nr. "), V(rental.PublicCode),
                    T(" / "), V(Date(rental.CreatedAtUtc)), T(".")),
                P(T("Locatarul predă, iar Locatorul primește Autovehiculul marca "), V(car.Brand), T(", model "), V(car.Model),
                    T(", nr. de înmatriculare "), V(car.PlateNumber), T(", VIN "), V(car.Vin, "5cm"), T(".")),
                P(T("Kilometraj la restituire: "), B("2.5cm"), T(" km.")),
                P(T("Nivel combustibil / stare baterie: "), B("5cm"), T(".")),
                P(T("Chei restituite: "), B("1.5cm"), T(" set/seturi.")),
                P(T("Documente restituite: "), C(false), T(" certificat de înmatriculare "), C(false), T(" RCA "), C(false), T(" alte documente: "), B("5cm"), T(".")),
                P(T("Accesorii/dotări restituite: "), B("12cm"), T(".")),
                P(T("Starea autovehiculului / avarii constatate la restituire: "), B("12cm"), T(".")),
                P(T("Observații: "), B("14cm"), T(".")),
                P(T("Fotografii realizate la restituire: "), C(false), T(" Da "), C(false), T(" Nu.")),
                P(T("Prin semnarea prezentului proces-verbal, părțile confirmă restituirea și primirea Autovehiculului, a documentelor, cheilor și accesoriilor menționate mai sus, în starea consemnată.")),
            ],
            Signatures: new(["AM PRIMIT, LOCATOR", "AM PREDAT, LOCATAR"], [CompanyName(company), tenant.Name])),
    ];

    /// <summary>Kilometrajul, combustibilul, cheile, documentele, accesoriile — din ce s-a notat la închiriere.</summary>
    private static IEnumerable<IReadOnlyList<RentalTextRun>> InventoryParagraphs(Rental rental, string moment)
    {
        bool keys = rental.Accessories.Any(a => a.Equals("Chei", StringComparison.OrdinalIgnoreCase));
        var accessories = rental.Accessories
            .Where(a => !a.Equals("Chei", StringComparison.OrdinalIgnoreCase))
            .Concat(string.IsNullOrWhiteSpace(rental.AccessoriesOther) ? [] : [rental.AccessoriesOther.Trim()])
            .ToList();

        yield return P(T($"Kilometraj {moment}: "), V(rental.StartMileage?.ToString("N0", Ro), "2.5cm"), T(" km."));
        yield return P(T("Nivel combustibil / stare baterie: "), V(rental.FuelLevelAtPickup, "5cm"), T("."));
        // Câte seturi de chei nu notează nimeni; se știe doar dacă s-au predat.
        yield return P(T("Chei predate: "), keys ? V("1") : B("1.5cm"), T(" set/seturi."));
        yield return P(T("Documente predate: "), C(false), T(" certificat de înmatriculare "), C(false), T(" RCA "), C(false), T(" alte documente: "), B("5cm"), T("."));
        yield return P(T("Accesorii/dotări predate: "), V(accessories.Count > 0 ? string.Join(", ", accessories) : null, "12cm"), T("."));
        yield return P(T($"Starea autovehiculului / avarii existente {moment}: "), B("12cm"), T("."));
        yield return P(T("Observații: "), V(rental.Notes, "14cm"), T("."));
        yield return P(T($"Fotografii realizate {moment}: "), C(false), T(" Da "), C(false), T(" Nu."));
    }

    private static RentalDocumentArticle Chapter(string title, params IReadOnlyList<RentalTextRun>[] paragraphs) =>
        new(title, paragraphs);

    private static RentalTextRun[] P(params RentalTextRun[] runs) => runs;

    private static RentalTextRun T(string text) => new(text);

    /// <summary>O valoare cunoscută, sau linia goală din șablon dacă lipsește.</summary>
    private static RentalTextRun V(string? value, string blankWidth = "3cm") =>
        string.IsNullOrWhiteSpace(value) ? B(blankWidth) : new RentalTextRun(value.Trim(), RentalTextKind.Value);

    private static RentalTextRun B(string width) => new(width, RentalTextKind.Blank);

    private static RentalTextRun C(bool checkedBox) => new(checkedBox ? "x" : string.Empty, RentalTextKind.Checkbox);

    /// <summary>Numele firmei ca în șablon, „… S.R.L.”, fără să dubleze forma juridică.</summary>
    private static string CompanyName(CompanyProfile company)
    {
        string name = company.LegalName.Trim();
        string compact = name.Replace(".", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        return compact.EndsWith("SRL", StringComparison.OrdinalIgnoreCase) ? name : $"{name} S.R.L.";
    }

    private static string Period(Rental rental)
    {
        int days = Math.Max(1, (int)Math.Round((rental.EndAtUtc - rental.StartAtUtc).TotalDays));
        // „de 5 zile”, dar „de 25 de zile”: de la 20 în sus, româna cere „de”.
        if (days == 1)
        {
            return "de o zi";
        }

        return days % 100 < 20 ? $"de {days} zile" : $"de {days} de zile";
    }

    private static string Date(DateTime value) => value.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static string Lei(long bani) => (bani / 100m).ToString("N2", Ro);
}
