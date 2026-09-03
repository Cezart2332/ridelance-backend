namespace Domain.Documents;

public enum DocumentCategory
{
    Buletin = 0,
    AtestatSofer = 1,
    CarteIdentitate = 2,
    PermisConducere = 3,
    AtestatTransport = 4,
    AdeverintaMedicala = 5,
    CazierJudiciar = 6,
    ITP = 7,
    RCA = 8,
    EcusonUber = 9,
    EcusonBolt = 10,
    AsigurareCalatori = 11,
    ExtrasBancar = 12,
    RaportUber = 13,
    RaportBolt = 14,
    Cheltuiala = 15,
    CertificatInregistrare = 16,
    CertificatConstatator = 17,
    DovadaPlataArr = 18,
    AutorizatieTransportAlternativ = 19,
    Talon = 20,
    CarteIdentitateAuto = 21,
    ContractVehicul = 22,
    AcordLeasing = 23,
    DovadaPlataCopieConformaEcusoane = 24,
    CopieConforma = 25,
    FacturaComisionUber = 26,
    FacturaComisionBolt = 27,
    DecontTvaIntracomunitar = 28,
    DecontTaxaNerezident = 29,
    CertificatTvaIntracomunitar = 30,
    DosarAutorizatieArr = 31,
    DosarCopieConformaEcusoane = 32,
    /// <summary>Specimenul de semnătură desenat la finalul dosarului de înființare.</summary>
    SpecimenSemnatura = 33,
    /// <summary>Avizul psihologic, separat de cel medical: se eliberează de altcineva și expiră altfel.</summary>
    AvizPsihologic = 36,
    /// <summary>CASCO — opțional, dar cerut explicit în lista documentelor de mașină.</summary>
    Casco = 37,
    /// <summary>Rezoluția/încheierea ONRC de la înființare — opțională, pentru arhivă.</summary>
    RezolutieOnrc = 34,
    /// <summary>Orice alt act primit la înființarea PFA-ului — opțional, pentru arhivă.</summary>
    AlteDocumenteInfiintare = 35,
    /// <summary>
    /// Pachetul de semnături întors semnat: împuterniciri ARR/ANAF, contractul de servicii și
    /// acordul GDPR, într-un singur fișier. Eterogen prin definiție, deci nu se verifică AI.
    /// </summary>
    DocumenteSemnate = 38,
    Other = 99
}
