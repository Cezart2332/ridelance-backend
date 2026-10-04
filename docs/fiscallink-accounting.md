# FiscalLink Cloud → contabilitate PFA

RIDElance citește bonurile fiscale și rapoartele Z emise cu succes **prin FiscalLink Cloud**.
Importul nu trimite comenzi către imprimantă și nu emite un nou raport Z. Documentele emise
exclusiv local, fără să apară în istoricul cloud, nu sunt acoperite de acest transport.

## Activare

1. Aplică migrația `20261005110000_ConnectFiscalLinkAccounting` la publicarea backendului.
2. Configurează pe server `FiscalLink__BaseUrl`, `FiscalLink__ManagementKey` și
   `FiscalLink__CommandKey`. BaseUrl este rădăcina `/api/tenants/{tenantId}/integrator/`, cu `/`
   final. Gestiune și Comenzi sunt chei distincte, obținute din FiscalLink Cloud → Setări → Chei API.
   Cheile rămân pe server; nu se configurează în frontend.
3. PFA-ul conectează/activează casa în Conexiuni → FiscalLink. În secțiunea „Bonuri și rapoarte Z”,
   „Sincronizează acum” rulează importul pentru PFA-ul autentificat. Nu acceptă un ID de PFA din client.
4. Importul zilnic existent al evidenței contabile include acum sursa FiscalLink.

Documentația integratorului este disponibilă în
`https://cloud.fiscallink.ro/org/{tenantId}/docs`. Contractul verificat în aplicația oficială
la 04.10.2026 documentează `GET cash-registers/{serial}/commands` și
`GET cash-registers/{serial}/commands/{commandId}` cu `X-Api-Key` (Comenzi).
Lista caselor se citește cu cheia Gestiune și `clientId` al PFA-ului.

## Date și controale

- Se parcurge istoricul paginat pentru casele clientului, inclusiv casele offline.
- Se importă numai `Succeeded` pentru `cashRegister.printFiscalReceipt` și
  `cashRegister.printReportZ`. X, bonuri nefiscale, storno și comenzi eșuate/pending nu creează venit.
- Se folosesc numărul și totalul din răspunsul fiscal (`body`, eventual `data`), nu prețurile
  cerute în payload. Pentru Z, totalul structurat poate fi suma grupelor fiscale.
- RIDElance folosește Z-ul exclusiv pentru numerar. Cardul este încasat de platformă. Un răspuns
  cu alte modalități de plată explicite rămâne la verificare.
- Bonul este unic după PFA, serie, număr și zi în ora României; nu generează încasare.
- Z-ul este unic după PFA, serie, număr și zi. Creează o singură încasare `CASH_Z / INCOME / CASH`.
  Se păstrează răspunsul JSON original într-un document criptat, legat de încasare.
- Un Z încărcat manual cu aceeași zi, număr și total se reutilizează. Diferențele de total
  nu suprascriu înregistrarea contabilului și nu generează o a doua încasare.
- Bonurile verifică totalul Z-ului pentru aceeași casă și zi. Diferențele cer verificare.
- Un import într-o lună închisă este marcat pentru corecție și nu intră în registrele închise.
- Începutul și sfârșitul colaborării contabile limitează documentele importate.
- Pagina arată numărul documentelor, ultima sincronizare reușită și eroarea/observațiile ultimei încercări.

## Numerar și decontarea Uber/Bolt

Raportul platformei conține brutul curselor, numerarul încasat și comisionul. Venitul online
este **brut − cash**. Din el platforma reține comisionul; restul trebuie să coincidă cu payout-ul
bancar. Numerarul intră separat prin Z, fără adunarea lui încă o dată din raport sau API-ul curselor.

Exemplu: brut 1.000, cash 300, comision 200, payout bancar 500 → încasare cash 300,
venit online 700 și comision 200. Venit brut în REF 1.000, cheltuială deductibilă 200 dacă regula
o permite, net 800. Controlul lunar compară cash-ul din rapoartele platformelor cu suma Z-urilor.

## Verificare

Testele HTTP folosesc răspunsuri controlate pentru autentificare, paginare, citirea detaliilor,
filtrarea comenzilor și respingerea datelor incomplete/străine. Testele contabile verifică importul
repetat, Z manual, diferențe, două case, perioade închise și reconcilierea cash + bancă în RJIP/REF.
Testele browser verifică sincronizarea, configurația lipsă și erorile pe desktop și mobil.

Aceste teste nu înlocuiesc verificarea cu o casă reală: după configurarea cheilor, emite un bon și
un Z prin Cloud, rulează sincronizarea și confruntă numărul, data și totalul cu documentele fiscale.
Răspunsurile fiscale fără date structurate suficiente sunt semnalate; nu se ghicesc numere sau sume.
