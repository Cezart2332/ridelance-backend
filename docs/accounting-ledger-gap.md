# Gap list — Flux contabil (Accounting Ledger → RJIP)

Audit al modulului contabil existent față de `specs/Spec — Flux contabil RIDElance (Accounting Ledger → RJIP).md`
(repo frontend), secțiunile 3–8. Scris la 30.09.2026, pe `main`.

Convenție: **Există** = implementat și conform; **Parțial** = există, dar incomplet față de spec;
**Lipsește**; **Contrazice** = cod care încalcă un invariant al spec-ului.

## Echivalențe de nume (spec → cod)

Spec-ul cere să extindem ce există, nu să creăm entități paralele.

| Spec | Cod existent | Observații |
| --- | --- | --- |
| `AccountingEntry` | `Domain.Accounting.LedgerEntry` (`ledger_entries`) | Suma e **cu semn** (încasare > 0, plată < 0); direcția se derivă din semn, nu e câmp separat. |
| `EntryDirection` | semnul lui `LedgerEntry.Amount` | Păstrăm convenția existentă: o folosesc importatorii, registrele, UI-ul și testele. |
| `EntryChannel { Bank, Cash, Manual }` | `PaymentMethod { Bank, Cash }` | Lipsește `Manual` (card/cont neconectat). |
| `SourceType` | `LedgerSource { Bank, Uber, Bolt, Oblio, Upload, CashZ, Manual }` | Lipsește `EFactura`. `CashZ` = `CashRegister`. |
| `ReconciliationStatus` | — | `LedgerEntryStatus` (`AutoImported`, `NeedsReview`, `Verified`, `Locked`) e starea de lucru, nu reconcilierea. |
| `BankTxType` | `LedgerEntry.TransactionType` (`Income`, `Expense`, `Transfer`, `OwnerContribution`, `Loan`, `Tax`, `Other`) | Lipsesc: retragere titular (R40), transfer intern (R43), decontare platformă (R20). |
| `IsLocked` | `LedgerEntry.Status == Locked` | Există. |
| `FiscalCategory` | `LedgerEntry.Category` (+ `ExpenseCategoryRule`) | Există. |
| `DeductibleAmount` | `LedgerEntry.DeductibleAmount` | Există, dar calculat pe toată suma, nu pe partea business. |
| `BusinessAmount` / `PersonalAmount` | — | Lipsesc. |
| `SettlementGroupId` | — | Lipsește. |
| `LinkedBankTransactionId` | `LedgerEntry.BankTransactionId` | Există. |
| `LinkedPlatformReportId` | `LedgerEntry.PlatformDocumentId` | Există (documentul Uber/Bolt). |
| `LinkedInvoiceId` | — | Lipsește legătura cu factura e-Factura. |
| `LinkedZReportId` | `ZReport.LedgerEntryId` (legătură inversă) | Există. |
| `LinkedManualDocumentId` | `ExpenseDocument.LedgerEntryId` (legătură inversă) + `LedgerEntry.SourceDocumentId` (fișierul) | Există. |
| `LinkedReceiptId` (bon fiscal) | — | Nu există entitatea de bon fiscal. |
| `BankTransaction` (brut) | `Domain.Banking.BankTransaction` | Există (Open Banking), idempotent pe `ProviderTransactionId`. |
| `Invoice` (e-Factura, brut) | `Domain.Accounting.EFacturaMessage` | Există (furnizor, CUI, număr, dată, total), fără stare de plată. |
| `ZReport` | `Domain.Accounting.ZReport` | Există: număr, dată, total. Fără cash/card separat, fără bonuri. |
| `FiscalReceipt` | — | Lipsește. |
| `PlatformReport` / `CommissionInvoice` | `PlatformDocument` + `DocumentExtraction` (tip `PlatformReport` / `CommissionInvoice`) | Există, cu extracția AI; fără venit cash separat, ajustări, confidence per câmp. |
| `ManualDocument` | `ExpenseDocument` | Parțial: fără metodă de plată, CUI beneficiar, linii business/personal, confidence. |
| Închiderea lunii | `PfaAccountingPeriod` + `ClosePeriodCommand` + corecții | Parțial (vezi §8). |

## §3 Surse de date

| Sursă | Stare | Detalii |
| --- | --- | --- |
| Open Banking | **Parțial** | `BankTransaction` există, idempotent pe ID-ul de la provider; ledger-ul se leagă prin `BankTransactionId`. Sincronizarea rulează la 12 ore de la ultima (`BankSyncJob`), **nu** fix la 00:00 și 12:00 Europe/Bucharest. |
| e-Factura (SPV) | **Parțial** | `EFacturaMessage` se importă automat pe CUI (job `EFacturaSyncJob`). Nu are `Unpaid/Paid/PartiallyPaid`, `PaidAmount`, nici potrivirea cu banca (R03–R04b). Facturile primite nu ajung deloc în ledger — conform R03, dar nici la plată. |
| Casă de marcat | **Parțial** | Z-ul se încarcă manual (PDF/poză, citire AI) și creează un entry `Income/Cash` — corect ca principiu (R10). Nu există bonuri individuale, verificarea Σ bonuri = Z (R11, R12), nici importul din casa de marcat. FiscalLink e conectat doar la nivel de gestiune (clienți, case), fără citirea bonurilor/Z. |
| Uber / Bolt | **Parțial** | Rapoarte și facturi de comision, cu extracția AI existentă și verificări (`DocumentChecker`). Idempotența: pe hash de fișier. Lipsesc venitul cash separat din raport (R24), ajustările, reconcilierea pe payout (R21–R23). Separat, CSV-urile Uber (`UberCsvImport`) și cursele Bolt (`BoltOrder`) alimentează dashboard-ul, nu ledger-ul — corect. |
| Upload client | **Parțial** | `ExpenseDocument` cu citire AI (comerciant, CUI, dată, total, produse) și propunere de plată bancară (±5 zile). Lipsesc „Cum ai plătit?” (R34), plata cash/card neconectat, CUI beneficiar (R31–R33), linii business/personal (R30). |

## §4 Model de date

| Cerință | Stare |
| --- | --- |
| `BusinessAmount + PersonalAmount == Amount` | **Lipsește** (nu există partea personală). |
| `DeductibleAmount + NonDeductibleAmount == Amount`, partea personală integral nedeductibilă | **Parțial**: `DeductibleAmount` = procent × toată suma. `NonDeductibleAmount` nu e stocat (se poate deriva). |
| Tranzacție bancară → mai multe entry-uri doar în același `SettlementGroupId`, cu net = suma tranzacției | **Lipsește**. Azi un `BankTransaction` are cel mult un entry „bancar”; rapoartele de platformă doar se leagă de el. |
| Document → cel mult o plată (sau plăți parțiale ≤ totalul facturii) | **Parțial**: `ExpenseDocument.LedgerEntryId` și `ZReport.LedgerEntryId` sunt singulare prin schemă. Plățile parțiale de factură nu există. |
| Entry blocat nemodificabil; corecții prin stornare în luna curentă | **Contrazice**: `CreatePeriodCorrectionCommand` modifică entry-ul blocat din luna închisă (cu audit), nu adaugă o stornare în luna curentă. |
| Rotunjire `decimal`, 2 zecimale, `AwayFromZero` | **Există** (deductibilitate, conversii valutare). |
| Nicio ștergere fizică | **Există** (`IAccountingRecord`, refuzat în `SaveChanges`). |

## §5 Pipeline

| Pas | Stare |
| --- | --- |
| Import idempotent | **Există** pe fiecare sursă (`ExternalId` + sursă, `BankTransactionId`, hash de fișier, număr Z). |
| Normalizare (CUI fără RO, comerciant normalizat) | **Parțial**: CUI-ul se normalizează în verificările documentelor; numele comerciantului nu se normalizează pentru potrivire (se caută primul cuvânt). |
| Matching | **Parțial**: bon ↔ bancă (sumă exactă ±0,01, dată ±5 zile, propunere confirmată de om); payout ↔ raport (Σ payout-uri = net ± 0,05, dată ≤ sfârșit + 7 zile, aplicat automat); factură Oblio ↔ bancă. Lipsesc factură e-Factura ↔ bancă și cash platformă ↔ Z. |
| „Un match ambiguu nu se aplică automat” | **Contrazice** parțial: potrivirea payout ↔ raport se aplică automat cu toleranță de 0,05 lei, iar factura Oblio se leagă de prima încasare cu aceeași sumă, fără să verifice dacă există mai mulți candidați. |
| Deduplicare între surse | **Parțial**: bon ↔ bancă se face prin legarea documentului de entry-ul bancar (un singur entry). Z vs Σ cash platformă nu se compară. |
| Emitere niciodată pe lună închisă | **Există**: importul în lună închisă primește `ClosedPeriodFlag` și nu intră în registre până la corecție. |
| Rulare după fiecare import și la cerere din Admin | **Există**: `LedgerImportJob` + `LedgerImport`. |

## §6 Reguli de business

| Regulă | Stare | Notă |
| --- | --- | --- |
| R01 | **Parțial** | Tranzacția devine entry imediat; plățile se clasifică după categorie și primesc deductibilitate automat (ex. OMV → carburant 50%) înainte de orice document. Spec: nu se marchează automat deductibilă, `Unmatched` până la match. |
| R02 | **Lipsește** | Nu există „Document lipsă” / „Asociază bon” pe tranzacție. |
| R03 | **Parțial** | Facturile primite nu intră în RJIP (corect), dar nu au stare de plată. |
| R04 / R04b | **Lipsește** | Nu există potrivire factură e-Factura ↔ plată bancară. |
| R10 | **Parțial** | Z → un entry `Income/Cash`, cu `NeedsReview` până la verificare. |
| R11 / R12 | **Lipsește** | Nu există bonuri. |
| R20 | **Contrazice** | Cu `IncomeRecognition = NetPayout` (valoarea implicită, nesetată în configurare), payout-ul devine **venit** în ledger, RJIP și REF. Invariantul 4 interzice asta. |
| R21 | **Contrazice** | În modul `GrossReport`, venitul brut și comisionul intră la **sfârșitul lunii raportului**, fără legătură cu tranzacția bancară, și sunt excluse din RJIP; payout-ul net rămâne în RJIP ca transfer. Spec: brut + comision la data decontării, legate de tranzacție, cu același `SettlementGroupId`; payout-ul nu mai apare ca rând separat. |
| R22 | **Contrazice** | Fără raport, payout-ul e venit (în `NetPayout`). Spec: rămâne `NeedsReconciliation`, fără cifre. |
| R23 | **Parțial** | Diferența payout vs raport apare ca notă în rezultatul importului, nu în Admin pe payout. |
| R24 / R25 | **Lipsește** | Venitul cash din raport nu e extras; nu se compară cu Σ Z. |
| R30 | **Lipsește** | Fără linii business/personal. |
| R31–R33 | **Lipsește** | CUI-ul beneficiarului nu e citit de pe bon. |
| R34 / R35 | **Lipsește** | Nu există plată cash/card neconectat pentru un bon fără bancă. |
| R36 | **Parțial** | Există propunerea bon → plată bancară la încărcare; nu există sensul invers (bon manual deja înregistrat, apoi tranzacția bancară la sync). |
| R40–R43 | **Lipsește** (automat) | Tipurile `OwnerContribution`, `Transfer`, `Tax` există doar la introducere/corecție manuală. Nu se recunosc automat transferul către titular, aportul, plata la ANAF/Trezorerie, transferul între conturile PFA. Lipsesc tipurile pentru retragere titular și transfer intern. |

## §7 RJIP și REF

| Cerință | Stare |
| --- | --- |
| RJIP = read model peste ledger, nu tabel | **Există** (`GetRjipQueryHandler`, calculat la cerere; nimic nu scrie rânduri RJIP). |
| Selecție: fără `NeedsReconciliation`, fără facturi neplătite | **Parțial**: nu există `NeedsReconciliation`; payout-urile intră. |
| Ordonare `OperationDate`, `DocumentDate`, `CreatedAt`; Nr. crt. continuu pe lună | **Parțial**: ordonare pe dată și `CreatedAt`; nu există `DocumentDate`; nr. crt. îl pune UI-ul/exportul. |
| Coloane numerar/bancă, totaluri lunare | **Există**. |
| Channel `Manual` configurabil (Q1) | **Lipsește** (nu există canalul). |
| Snapshot final (PDF + date) la închiderea lunii | **Lipsește**. Exportul PDF/XLSX există, dar nu se salvează la închidere. |
| REF din tax engine, pe `DeductibleAmount`, fără aporturi/transferuri/partea personală | **Parțial**: REF citește ledger-ul (nu RJIP) și folosește `DeductibleAmount` — corect ca principiu. Venitul brut = entry-urile `Income`, deci include payout-urile în `NetPayout` (contrazice invariantul 4). Partea personală nu există. |
| Control sold bancar vs RJIP bancă | **Lipsește**. |

## §8 UX și închiderea lunii

| Cerință | Stare |
| --- | --- |
| Ecran Tranzacții PFA cu status pe rând și contor „necesită atenție” | **Parțial**: există pagina de activitate bancară (categorii), fără statusurile contabile și fără „Asociază bon”. |
| „Adaugă cheltuială → Fotografiază bonul” + confirmare OCR + „Cum ai plătit?” | **Parțial**: încărcarea există în contabilitate (contabil/admin), nu ca flux PFA cu întrebarea de plată. |
| Ecran Reconciliere lunară (Admin) cu checklist | **Parțial**: există pre-check pe lună (`PreCheck`, documente lipsă / de verificat) și statistici; lipsesc controalele din spec (sync bancar 24h, SPV pe toată luna, Z pe zi, payout-uri, sold). |
| „Închide luna” activ doar când trec controalele | **Contrazice**: închiderea nu verifică niciun control. |
| La închidere: blocare entry-uri, snapshot, `Closed` | **Parțial**: blocare + `Closed` există; snapshot lipsește. |
| Redeschidere doar Admin, cu motiv și audit | **Lipsește** (există doar corecții pe luna închisă). |

## Ce se păstrează neschimbat

- Stocarea ledger-ului și cheile de idempotență ale surselor.
- RJIP ca proiecție calculată; exporturile PDF/XLSX existente.
- Declarațiile (D100/D301/D390) și motorul lor — în afara scope-ului; citesc documentele de comision, nu RJIP.
- Auditul (`AccountingAudit`) și interdicția de ștergere fizică.

## Ordinea implementării

1. **Model de date** (acest pas): canalul `Manual`, sursa `EFactura`, tipurile R40/R43/R20, `ReconciliationStatus`, `SettlementGroupId`, legătura cu factura e-Factura, partea personală, `DocumentDate`; invarianții în domeniu, cu teste. Deductibilitatea se calculează pe partea business.
2. **R20–R22 + R40–R43**: payout-ul nu mai e venit (`NeedsReconciliation`, exclus din RJIP și REF); reconcilierea cu raportul produce brut + comision la data decontării, cu `SettlementGroupId`; contrapartidele titular / ANAF / conturi proprii.
3. **R01–R04b**: potrivirea factură e-Factura ↔ bancă și starea facturii; R01 fără deductibilitate automată până la document.
4. **R30–R36**: bonul cu linii business/personal, CUI beneficiar, „Cum ai plătit?”.
5. **R10–R12, R24–R25**: bonuri și Z din casa de marcat (FiscalLink, după spec-ul registrului), control cash platformă ↔ Z.
6. **Reconciliere Admin + închiderea lunii** pe controale, snapshot, redeschidere cu motiv; corecții prin stornare.
7. **UX Tranzacții PFA**.

## Întrebări deschise — valori implicite până la decizie

- **Q1** (card/cont neconectat în RJIP): configurabil (`Accounting:ManualChannelMapping`), implicit **plată numerar** însoțită de **aport titular numerar** de aceeași sumă, ca soldul de casă să nu devină negativ.
- **Q2** (toleranțe): configurabile; bon ↔ bancă ±3 zile, factură ↔ bancă fără limită în urmă (plata ≥ data facturii), sumă 0 lei.
- **Q3** (data entry-urilor Uber/Bolt): data decontării (payout-ului).
- **Q4** (mai multe conturi, unele neconectate): conturile neconectate intră ca `Manual`; controlul de sold se face doar pe conturile conectate.
