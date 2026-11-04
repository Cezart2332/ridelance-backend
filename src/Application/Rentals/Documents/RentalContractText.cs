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
/// Unde șablonul spune „dacă părțile au convenit” (penalitățile, conducătorul suplimentar),
/// lipsa valorii are o formulare proprie, nu o linie goală. Ce chiar nu se știe la generare —
/// avariile, documentele predate, când nu s-a făcut încă predarea — rămâne linie de completat.
/// </remarks>
internal static class RentalContractText
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public static IReadOnlyList<RentalDocumentArticle> Contract(
        Rental rental,
        Car car,
        CompanyProfile company,
        Tenant tenant,
        CheckRecord? checkIn = null,
        DateTime? nowUtc = null)
    {
        bool person = tenant.Type == TenantType.Individual;
        string companyName = CompanyName(company);
        string? extraKm = rental.ExtraKmCostBani > 0 ? Lei(rental.ExtraKmCostBani) : null;
        var pickup = HandoverState.Pickup(rental, checkIn);

        return
        [
            Chapter("I. PĂRȚILE CONTRACTANTE",
                P(T("1.1. "), V(companyName), T(", cu sediul în "), V(company.RegisteredOffice, "5cm"),
                    T(", înregistrată la Registrul Comerțului sub nr. "), V(company.RegCom), T(", CUI "), V(company.Cui),
                    T(", cont IBAN "), V(company.Iban, "5cm"), T(", reprezentată prin "), V(company.LegalRepresentative, "4cm"),
                    T(", în calitate de "), V(company.LegalRepresentativeRole, "3cm"), T(", denumită în continuare Locator,")),
                P(T("și")),
                person
                    ? P(T("1.2. "), V(tenant.Name, "5cm"), T(", cu domiciliul în "), V(tenant.Address, "5cm"),
                        T(", CNP "), V(tenant.Cnp), T(", identificat(ă) cu CI seria "), V(tenant.IdSeries, "1cm"),
                        T(" nr. "), V(tenant.IdNumber, "2cm"), T(", denumit(ă) în continuare Locatar,"))
                    : P(T("1.2. "), V(tenant.Name, "5cm"), T(", cu sediul în "), V(tenant.Address, "5cm"),
                        T(", CUI "), V(tenant.Cui), T(", înregistrat(ă) la Registrul Comerțului sub nr. "), V(tenant.RegCom),
                        T(", reprezentat(ă), după caz, prin "), V(tenant.RepresentativeName, "4cm"), T(", denumit(ă) în continuare Locatar,")),
                P(T("au convenit încheierea prezentului contract de închiriere, în următoarele condiții:"))),

            Chapter("II. OBIECTUL CONTRACTULUI",
                P(T("2.1. Locatorul dă în folosința Locatarului autovehiculul marca "), V(car.Brand), T(", model "), V(car.Model),
                    T(", număr de înmatriculare "), V(car.PlateNumber), T(", serie de șasiu (VIN) "), V(car.Vin, "5cm"),
                    T(", an fabricație "), V(car.Year > 0 ? car.Year.ToString(CultureInfo.InvariantCulture) : null, "1.5cm"), T(", denumit în continuare Autovehicul.")),
                P(T("2.2. Autovehiculul se predă pe bază de proces-verbal de predare-primire, în care se vor menționa kilometrajul, nivelul de combustibil/starea de încărcare, documentele și accesoriile predate, precum și eventualele avarii existente.")),
                P(T("2.3. Autovehiculul va fi utilizat pentru "), V("activități de transport alternativ (ridesharing)"),
                    T(". Utilizarea pentru activități de transport alternativ/ridesharing este permisă dacă această destinație a fost convenită de părți și sunt îndeplinite condițiile prevăzute de lege."))),

            Chapter("III. DURATA CONTRACTULUI. CHIRIA. GARANȚIA",
                [
                    P(T("3.1. Contractul se încheie pentru perioada "), V(Period(rental)), T(", începând cu data de "), V(Date(rental.StartAtUtc)),
                        T(" și până la data de "), V(Date(rental.EndAtUtc)), T(".")),
                    P([T("3.2. Chiria este de "), V(Lei(rental.WeeklyRentBani)), T(" lei / săptămână și se achită "),
                        .. PaymentDue(rental.PaymentDueDay), .. PaymentMethod(rental.PaymentMethod), T(".")]),
                    P(T("3.3. La predarea Autovehiculului, Locatarul achită o garanție în cuantum de "), V(Lei(rental.DepositBani), "2.5cm"),
                        T(" lei. Garanția se restituie la încetarea contractului, după predarea Autovehiculului și achitarea tuturor sumelor datorate.")),
                    P(T("3.4. Locatorul poate reține din garanție sumele reprezentând chirii neachitate, amenzi sau taxe aferente perioadei de folosință, lipsuri, franșize și prejudicii imputabile Locatarului, cu justificarea sumelor reținute.")),
                    rental.LatePenaltyPercentPerDay is > 0 and decimal penalty
                        ? P(T("3.5. Pentru întârzierea la plata chiriei se datorează penalități de "), V(penalty.ToString("0.##", Ro), "1.5cm"),
                            T(" % pe zi de întârziere, dacă părțile au convenit aplicarea acestora."))
                        : P(T("3.5. Pentru întârzierea la plata chiriei, părțile nu au convenit penalități.")),
                    .. OtherCosts(rental),
                ]),

            Chapter("IV. PREDAREA ȘI FOLOSIREA AUTOVEHICULULUI",
                P(T("4.1. Autovehiculul se predă Locatarului în stare corespunzătoare de funcționare, împreună cu documentele, cheile și accesoriile menționate în procesul-verbal de predare-primire.")),
                P(T("4.2. Locatarul se obligă să folosească Autovehiculul cu prudență, potrivit destinației stabilite, cu respectarea legislației rutiere și a instrucțiunilor de exploatare.")),
                string.IsNullOrWhiteSpace(rental.AdditionalDriverName)
                    ? P(T("4.3. Autovehiculul poate fi condus numai de Locatar. Încredințarea Autovehiculului unei alte persoane se poate face numai cu acordul Locatorului."))
                    : P(T("4.3. Autovehiculul poate fi condus de Locatar sau de următorul conducător autorizat: "), V(rental.AdditionalDriverName, "5cm"),
                        T(", CNP "), V(rental.AdditionalDriverCnp, "3.5cm"), T(", permis de conducere seria/nr. "), V(rental.AdditionalDriverLicense, "3cm"),
                        T(". Încredințarea Autovehiculului unei alte persoane se poate face numai cu acordul Locatorului.")),
                P(T("4.4. Autovehiculul va fi utilizat pe teritoriul României. Ieșirea din țară se poate face numai cu acordul prealabil al Locatorului și cu respectarea condițiilor de asigurare.")),
                rental.HasKmLimit
                    ? P(T("4.5. Kilometrajul inclus este de "), V(rental.MileageLimit?.ToString("N0", Ro), "2.5cm"),
                        T(" km / săptămână. Pentru depășirea limitei stabilite se va achita suma de "), V(extraKm, "2cm"), T(" lei/km."))
                    : P(T("4.5. Kilometrajul inclus este "), V("nelimitat"), T(".")),
                P([T("4.6. Autovehiculul se predă cu nivelul de combustibil/starea de încărcare de "), V(Phrase(pickup.Fuel), "2.5cm"),
                    T(" și se restituie în aceleași condiții, dacă părțile nu stabilesc altfel."), .. FuelRule(rental.FuelRule)]),
                P(T("4.7. Locatarului îi este interzis să subînchirieze Autovehiculul, să îl folosească la competiții, să efectueze modificări asupra acestuia fără acordul Locatorului sau să îl utilizeze în scopuri contrare legii."))),

            Chapter("V. OBLIGAȚIILE PĂRȚILOR",
                P(T("5.1. Locatorul se obligă să predea Autovehiculul în stare corespunzătoare de folosință și să asigure efectuarea reviziilor și reparațiilor determinate de uzura normală, dacă părțile nu convin altfel.")),
                P(T("5.2. Locatarul suportă cheltuielile cu combustibilul/energia, parcările, taxele de drum și amenzile rezultate din utilizarea Autovehiculului pe durata contractului.")),
                P(T("5.3. Locatarul va informa Locatorul, în cel mai scurt timp, despre orice defecțiune, martor de avertizare, avarie sau incident și nu va continua utilizarea Autovehiculului dacă aceasta poate agrava defecțiunea.")),
                P(T("5.4. În cazul în care Autovehiculul este indisponibil din cauza unei defecțiuni care nu este imputabilă Locatarului, chiria pentru perioada de indisponibilitate "), V(Unavailability(rental.UnavailabilityRule), "5cm"),
                    T(". Dacă indisponibilitatea este cauzată din culpa Locatarului, acesta nu este exonerat de obligațiile asumate, în măsura permisă de lege.")),
                P(T("5.5. Pierderea ori deteriorarea din culpa Locatarului a cheilor, documentelor sau accesoriilor predate atrage obligația acestuia de a suporta costul înlocuirii sau remedierii."))),

            Chapter("VI. ASIGURAREA. ACCIDENTELE ȘI DAUNELE",
                P(T("6.1. Autovehiculul beneficiază de asigurare RCA valabilă și, după caz, de asigurare CASCO.")),
                P(T("6.2. Franșiza aplicabilă în cazul unei daune este de "), V(Franchise(rental.FranchiseBani), "3cm"),
                    T(". Aceasta va fi suportată de Locatar în cazul în care dauna îi este imputabilă, în condițiile prezentului contract și ale poliței de asigurare.")),
                P(T("6.3. În caz de accident, furt, vandalism sau alt eveniment, Locatarul va anunța de îndată Locatorul și va îndeplini formalitățile legale necesare, inclusiv constatarea amiabilă sau prezentarea la organele de poliție, după caz.")),
                P(T("6.4. Locatarul răspunde pentru prejudiciile neacoperite de asigurare atunci când acestea rezultă din fapta sa culpabilă, inclusiv conducerea sub influența alcoolului sau a substanțelor interzise, conducerea fără drept, folosirea de către o persoană neautorizată ori nerespectarea obligațiilor necesare instrumentării dosarului de daună.")),
                P(T("6.5. Eventualele daune constatate la restituire vor fi consemnate în procesul-verbal și, după caz, documentate prin fotografii și documente privind costul reparației."))),

            Chapter("VII. ÎNCETAREA CONTRACTULUI. RESTITUIREA AUTOVEHICULULUI",
                rental.NoticeDays is int notice
                    ? P(T("7.1. Contractul încetează la expirarea perioadei pentru care a fost încheiat, prin acordul părților sau prin denunțare, cu un preaviz de "), V(Days(notice)), T("."))
                    : P(T("7.1. Contractul încetează la expirarea perioadei pentru care a fost încheiat, prin acordul părților sau prin denunțare, cu un preaviz de "), B("1.5cm"), T(" zile.")),
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
                    P(T("8.5. Prezentul contract a fost încheiat astăzi, "), V(Date(nowUtc ?? DateTime.UtcNow)),
                        T(", în două exemplare, câte unul pentru fiecare parte / prin mijloace electronice, după caz.")),
                ],
                Signatures: new(["LOCATOR", "LOCATAR"], [companyName, tenant.Name])),

            // Anexa nr. 1 stă în același PDF, pe pagină nouă, ca în șablon.
            .. Handover(rental, car, company, tenant, annex: true, checkIn),
        ];
    }

    /// <summary>
    /// Procesul-verbal de predare-primire (Anexa nr. 1). <paramref name="annex" /> = pagina din
    /// contract; altfel e documentul de sine stătător, cu titlul lui.
    /// </summary>
    public static IReadOnlyList<RentalDocumentArticle> Handover(
        Rental rental,
        Car car,
        CompanyProfile company,
        Tenant tenant,
        bool annex,
        CheckRecord? checkIn = null)
    {
        var state = HandoverState.Pickup(rental, checkIn);

        IReadOnlyList<IReadOnlyList<RentalTextRun>> paragraphs =
        [
            P(T("Încheiat astăzi, "), V(Date(state.DateUtc)), T(", în baza Contractului de închiriere nr. "), V(rental.PublicCode),
                T(" / "), V(Date(rental.CreatedAtUtc)), T(".")),
            P(T("Locatorul predă, iar Locatarul primește Autovehiculul marca "), V(car.Brand), T(", model "), V(car.Model),
                T(", nr. de înmatriculare "), V(car.PlateNumber), T(", VIN "), V(car.Vin, "5cm"), T(".")),
            .. InventoryParagraphs(state, pickup: true),
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
    /// formă, cu rolurile inversate: Locatarul predă, Locatorul primește. Se completează din
    /// primirea consemnată în aplicație, dacă există.
    /// </summary>
    public static IReadOnlyList<RentalDocumentArticle> Return(
        Rental rental,
        Car car,
        CompanyProfile company,
        Tenant tenant,
        CheckRecord? checkOut = null)
    {
        var state = HandoverState.Return(rental, checkOut);

        return
        [
            new RentalDocumentArticle(
                null,
                [
                    P(T("Încheiat astăzi, "), V(Date(state.DateUtc)), T(", în baza Contractului de închiriere nr. "), V(rental.PublicCode),
                        T(" / "), V(Date(rental.CreatedAtUtc)), T(".")),
                    P(T("Locatarul predă, iar Locatorul primește Autovehiculul marca "), V(car.Brand), T(", model "), V(car.Model),
                        T(", nr. de înmatriculare "), V(car.PlateNumber), T(", VIN "), V(car.Vin, "5cm"), T(".")),
                    .. InventoryParagraphs(state, pickup: false),
                    P(T("Prin semnarea prezentului proces-verbal, părțile confirmă restituirea și primirea Autovehiculului, a documentelor, cheilor și accesoriilor menționate mai sus, în starea consemnată.")),
                ],
                Signatures: new(["AM PRIMIT, LOCATOR", "AM PREDAT, LOCATAR"], [CompanyName(company), tenant.Name])),
        ];
    }

    /// <summary>Kilometrajul, combustibilul, cheile, documentele, accesoriile, avariile, fotografiile.</summary>
    private static IEnumerable<IReadOnlyList<RentalTextRun>> InventoryParagraphs(HandoverState state, bool pickup)
    {
        string moment = pickup ? "la predare" : "la restituire";
        string handed = pickup ? "predate" : "restituite";
        bool registration = HandoverDocumentCodes.Contains(state.Documents, HandoverDocumentCodes.RegistrationCertificate);
        bool rca = HandoverDocumentCodes.Contains(state.Documents, HandoverDocumentCodes.Rca);

        yield return P(T($"Kilometraj {moment}: "), V(state.Mileage?.ToString("N0", Ro), "2.5cm"), T(" km."));
        yield return P(T("Nivel combustibil / stare baterie: "), V(Phrase(state.Fuel), "5cm"), T("."));
        yield return P(T($"Chei {handed}: "), V(state.Keys?.ToString(CultureInfo.InvariantCulture), "1.5cm"), T(" set/seturi."));
        yield return P(T($"Documente {handed}: "), C(registration), T(" certificat de înmatriculare "), C(rca), T(" RCA "),
            C(state.DocumentsOther is not null), T(" alte documente: "), V(Phrase(state.DocumentsOther), "5cm"), T("."));
        yield return P(T($"Accesorii/dotări {handed}: "), V(state.Accessories.Count > 0 ? string.Join(", ", state.Accessories) : null, "12cm"), T("."));
        yield return P(T(pickup ? "Starea autovehiculului / avarii existente la predare: " : "Starea autovehiculului / avarii constatate la restituire: "),
            V(Phrase(state.Damages), "12cm"), T("."));
        yield return P(T("Observații: "), V(Phrase(state.Notes), "14cm"), T("."));
        yield return P(T($"Fotografii realizate {moment}: "), C(state.Photos == true), T(" Da "), C(state.Photos == false), T(" Nu."));
    }

    /// <summary>„… se achită în fiecare zi de luni” — sau linia din șablon, dacă nu s-a stabilit ziua.</summary>
    private static RentalTextRun[] PaymentDue(DayOfWeek? day) => day is DayOfWeek d
        ? [T("în fiecare zi de "), V(DayName(d))]
        : [T("la data de "), B("2.5cm")];

    /// <summary>„…, prin transfer bancar în contul IBAN al Locatorului” — prepoziția depinde de modalitate.</summary>
    private static RentalTextRun[] PaymentMethod(RentalPaymentMethod? method) => method switch
    {
        RentalPaymentMethod.Cash => [T(", "), V("în numerar")],
        RentalPaymentMethod.BankTransfer => [T(", prin "), V("transfer bancar în contul IBAN al Locatorului")],
        RentalPaymentMethod.Card => [T(", "), V("cu card bancar")],
        RentalPaymentMethod.Other => [T(", prin "), V("altă modalitate convenită de părți")],
        _ => [T(", prin "), B("4cm")],
    };

    private static IReadOnlyList<RentalTextRun>[] OtherCosts(Rental rental) => rental.OtherCostsBani > 0
        ? [P(T("3.6. Pe lângă chirie și garanție, Locatarul achită costurile convenite la semnare, în sumă de "), V(Lei(rental.OtherCostsBani)), T(" lei."))]
        : [];

    private static RentalTextRun[] FuelRule(string? rule) => string.IsNullOrWhiteSpace(rule)
        ? []
        : [T(" Părțile stabilesc: "), V(Phrase(rule)), T(".")];

    private static string? Unavailability(RentalUnavailabilityRule? rule) => rule switch
    {
        RentalUnavailabilityRule.NotDue => "nu se datorează",
        RentalUnavailabilityRule.Proportional => "se reduce proporțional cu durata indisponibilității",
        RentalUnavailabilityRule.FullyDue => "se datorează integral",
        _ => null,
    };

    private static string? Franchise(long? bani) => bani switch
    {
        null => null,
        0 => "0,00 lei (fără franșiză)",
        long value => $"{Lei(value)} lei",
    };

    private static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "luni",
        DayOfWeek.Tuesday => "marți",
        DayOfWeek.Wednesday => "miercuri",
        DayOfWeek.Thursday => "joi",
        DayOfWeek.Friday => "vineri",
        DayOfWeek.Saturday => "sâmbătă",
        _ => "duminică",
    };

    private static RentalDocumentArticle Chapter(string title, params IReadOnlyList<RentalTextRun>[] paragraphs) =>
        new(title, paragraphs);

    private static RentalTextRun[] P(params RentalTextRun[] runs) => runs;

    private static RentalTextRun T(string text) => new(text);

    /// <summary>O valoare cunoscută, sau linia goală din șablon dacă lipsește.</summary>
    private static RentalTextRun V(string? value, string blankWidth = "3cm") =>
        string.IsNullOrWhiteSpace(value) ? B(blankWidth) : new RentalTextRun(value.Trim(), RentalTextKind.Value);

    /// <summary>
    /// Text liber urmat în șablon de punct: fără punctul pus de utilizator, altfel iese „spălat..”.
    /// </summary>
    private static string? Phrase(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('.', ' ', ';', ',');

    private static RentalTextRun B(string width) => new(width, RentalTextKind.Blank);

    private static RentalTextRun C(bool checkedBox) => new(checkedBox ? "x" : string.Empty, RentalTextKind.Checkbox);

    /// <summary>
    /// Numele firmei ca în șablon. „… S.R.L.” se adaugă doar unui SRL care nu și-a scris deja forma
    /// juridică; un PFA rămâne PFA — „Ion Pop PFA S.R.L.” ar fi altă persoană juridică.
    /// </summary>
    private static string CompanyName(CompanyProfile company)
    {
        string name = company.LegalName.Trim();
        if (company.OwnerType != OwnerType.Srl)
        {
            return name;
        }

        string compact = name.Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        string[] forms = ["SRL", "SRLD", "SA", "PFA", "II", "IF"];
        return forms.Any(f => compact.EndsWith(f, StringComparison.OrdinalIgnoreCase)) ? name : $"{name} S.R.L.";
    }

    private static string Period(Rental rental) =>
        "de " + Days(Math.Max(1, (int)Math.Round((rental.EndAtUtc - rental.StartAtUtc).TotalDays)));

    /// <summary>„o zi”, „5 zile”, dar „25 de zile”: de la 20 în sus, româna cere „de”.</summary>
    private static string Days(int days) => days switch
    {
        1 => "o zi",
        _ when days % 100 is > 0 and < 20 || days == 0 => $"{days} zile",
        _ => $"{days} de zile",
    };

    private static string Date(DateTime value) => RomanianTime.Date(value);

    private static string Lei(long bani) => (bani / 100m).ToString("N2", Ro);
}
