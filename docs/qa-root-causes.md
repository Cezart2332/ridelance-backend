# QA — cauze comune și plan de unificare

Raportul QA din browser (date de test din producție) are 24 de puncte. Multe vin din aceeași cauză:
ecranele citesc același lucru din surse sau câmpuri diferite. Datele din producție nu sunt accesibile
de aici. De aceea fiecare punct e marcat **bug de cod** (reprodus și reparat local, cu test pe seed-ul
`QaScenarioTests`) sau **de confirmat cu date** (script read-only în `scripts/diagnostics/`).

Regula țintă:
- `LedgerEntry` (AccountingEntry) e singura sursă pentru clasificarea și sumele unei tranzacții.
- `DeclarationVersion` (DeclarationRecord) e singura sursă pentru statusul unei declarații.
- Ecranele doar citesc.

## De unde citește fiecare ecran

| Ecran | Clasificarea tranzacției | Statusul declarației | Sume |
| --- | --- | --- | --- |
| Admin → Bancă (`BankActivityPanel`) | — (tranzacțiile brute din bancă) | — | `bank_transactions`, **conexiunea curentă** a utilizatorului |
| Admin → Bancă → Tranzacții contabile (`TransactionsTab`) | `LedgerEntry.Status` (Verificat) + `Category` | — | `LedgerEntry.Amount`, `DeductibleAmount` |
| RJIP / excepții | `LedgerEntry.ReconciliationStatus` + `TransactionType` + `ProposedClassification` | — | ledger, mișcările efective |
| REF | `TransactionType` + `ReconciliationStatus` (Matched/Partial) | — | `TaxableIncomeAmount`, `DeductibleAmount` |
| Reconciliere lună / „Închide luna” | `ReconciliationStatus` | — | ledger + `bank_transactions` (**alt filtru** decât importul) |
| Bancă → Perioade contabile | — | — | doar `pfa_accounting_periods.status` (**fără controale**) |
| Lista lunară de declarații | — | `DeclarationVersion.Status` → `statusLabels.DECLARATION_STATUS` | versiunea curentă |
| Cardul clientului (Luna aceasta) | — | același status → `workspace/status.ts` (**alt dicționar de etichete**) | aceeași |
| PFA → Tranzacții (`ClientLedger`) | `Status` și `ReconciliationStatus`, cu `Status=NeedsReview` înaintea „Document lipsă” | — | ledger |
| PFA → Taxe & declarații | — | **`tax_obligations`** (introduse manual de contabil) | `TaxObligation.AmountDue` |
| Venituri și taxe (estimări) | — | — | **`pfa_monthly_incomes`, `deductible_expenses`, `tax_obligations`** (modulul vechi, nu ledger-ul) |
| Numerar / casă de marcat | — | — | **`cash_register_states`** (contabilitate) și **`pfa_fiscal_profiles.cash_*`** (setări fiscale) |

## Surse duble de adevăr

1. **Clasificarea unei tranzacții bancare** are trei câmpuri independente: `Status` (Verificat),
   `ReconciliationStatus` și `TransactionType` / `ProposedClassification`.
   - „Verifică” și editarea categoriei din Bancă schimbă doar `Status` și `Category`.
   - RJIP, excepțiile și REF citesc `ReconciliationStatus` și `TransactionType`.
   - Rezultatul: un rând „Verificat · Service auto · deductibil 100” în Bancă e „Plată neidentificată”
     în RJIP și lipsește din REF (punctele 1 și 8).
2. **Statusul declarației** are două dicționare de etichete pentru aceeași valoare. De exemplu,
   `SUBMITTED` e „Depus” în listă și „Fără recipisă” în card, iar `ACCEPTED` e „Recipisă validă” în
   listă și „Depusă” în card. Datele sunt aceleași, doar eticheta diferă (punctul 5).
3. **Tranzacțiile bancare ale PFA-ului** au trei filtre:
   - widgetul: conexiunea curentă, doar `booking_date`;
   - importul în ledger: conexiunea declarată, Linked, consimțământul curent, cont activ;
   - controlul de sold: fără filtru de consimțământ sau cont activ.
   Așa apar „0 tranzacții” în widget și diferențele de sold (punctul 3).
4. **Poate închide luna?** Reconcilierea are `CanClose`, dar „Perioade contabile” nu îl citește
   (punctul 6).
5. **Obligațiile de plată ale PFA-ului** vin din `tax_obligations`, nu din declarații (punctul 11).
6. **Venitul și taxele estimate** vin din `pfa_monthly_incomes` și `deductible_expenses`, nu din
   ledger. „Net realizat” > venitul din ledger e cifra altei surse (punctul 12).
7. **Numerarul** e ținut în `cash_register_states` și în `pfa_fiscal_profiles` (punctul 13).
8. **Starea lunii** în lista de declarații e cea salvată la ultima procesare (`pfa_month_checks`), nu
   cea de acum. De aici „Document lipsă 0” după ce documentele s-au schimbat (punctul 15).

## Plan de unificare (pe care îl aplic)

- **Clasificarea bancară:** un singur serviciu (`BankClassifications`) care setează împreună tipul,
  categoria, `ReconciliationStatus` și propunerea. Bancă, excepțiile RJIP, „Verifică” și editarea
  categoriei trec toate prin el. „Verifică” cere o clasificare.
- **Etichetele de status ale declarațiilor:** un singur dicționar (`statusLabels.DECLARATION_STATUS`),
  folosit de listă și de card. „Recipisă validă” doar cu recipisă asociată. Tranzițiile manuale doar
  pentru Admin, cu motiv.
- **Tranzacțiile bancare ale PFA-ului:** o singură interogare (`PfaBankTransactions`) pentru import,
  controlul de sold și widget (când utilizatorul are un PFA).
- **Poate închide luna:** perioadele primesc `canClose` și motivele din aceeași reconciliere; butonul
  din „Perioade contabile” îl respectă.
- **Payout-urile platformei:** lipsa payout-urilor nu mai e „toate reconciliate”. Un raport confirmat
  fără decontare în bancă oprește luna.
- **Obligațiile PFA-ului:** se citesc din declarațiile generate (sumă, termen din regula fiscală,
  status). `tax_obligations` rămâne doar pentru plățile înregistrate manual.
- **Estimările:** cifrele cardului vin dintr-un singur calcul, cu invariantele „total = Σ componente”
  și „net ≤ brut” testate. Mutarea sursei pe ledger e un pas separat, după rezultatele diagnosticelor.
- **Numerarul:** `cash_register_states` e sursa. Setările fiscale citesc și scriu acolo. Dacă există
  venit cash, reconcilierea cere Z-uri.

## Puncte: cod vs date

| # | Cod | De confirmat cu date | Script |
| --- | --- | --- | --- |
| 1 | Verificarea și categoria din Bancă nu actualizează `ReconciliationStatus` / tipul (sursă dublă) | dacă rândul de 200 lei e de fapt un transfer către titular | 03 |
| 2 | „Toate payout-urile sunt reconciliate” fără niciun payout; raportul confirmat fără decontare nu oprește luna | dacă banca de test primește payout-uri Bolt/Uber în august | 04 |
| 3 | trei filtre diferite pentru tranzacțiile PFA-ului; widgetul ignoră `value_date` | care tranzacții lipsesc sau sunt în plus și de ce | 01, 02 |
| 4 | D390 cu bază 0 se poate genera; fără `TaxPointDate` nu trebuie generat nimic | declarațiile din august sunt generate înainte de regula F12 | 05 |
| 5 | două dicționare de etichete; tranzițiile manuale permise contabilului, fără motiv | ce declarații au status de depunere fără recipisă | 05 |
| 6 | butonul „Închide luna” din Perioade nu citește controalele | — | — |
| 7 | titularul cu prenume compus („Ionescu Andrei-Victor”) nu se potrivește cu „Victor Ionescu” | profilul de test („testr test”) | 03 |
| 8 | „Verifică” fără clasificare | — | — |
| 9 | asocierea bonului poate propune o plată de alt tip (comision bancar); preselecție în dialog | — | — |
| 10 | contorul amestecă încasări; „De verificat” acoperă „Document lipsă” | de ce octombrie e gol (curse ≠ tranzacții) | 08 |
| 11 | obligațiile PFA-ului vin din `tax_obligations` | — | — |
| 12 | eticheta „Taxe estimate” arată suma rămasă de pus deoparte; sursele de venit diferă | cifrele din `pfa_monthly_incomes` | — |
| 13 | două câmpuri de numerar | valorile din profil | 09 |
| 14 | D207 blochează când D100 are beneficiari fără plăți în registru (D100 vechi, generat din facturi) | — | — |
| 15 | PFA fără CIF intră în lot; starea lunii nu e recalculată | PFA-uri duplicate de test | 06, 07 |
| 16 | o rulare identică se salvează din nou la fiecare marcare „stale” | frecvența și motivul | 10 |

## Scripturi de rulat

Toate sunt în `scripts/diagnostics/`, doar `SELECT`. Fiecare are sus ce verifică și cum se citește rezultatul.

1. `01_bank_vs_ledger.sql` — tranzacțiile bancare 2026 vs înregistrări, în ambele direcții, și diferența de sold pe iulie, august, septembrie.
2. `02_bank_widget_zero.sql` — filtrul widgetului Bancă, rulat pe date.
3. `03_owner_profile_vs_counterparties.sql` — titularul în profil și IBAN-uri vs contrapartidele „Victor Ionescu”.
4. `04_platform_reports_payouts_august.sql` — rapoartele Uber/Bolt din august, payout-urile și înregistrările generate.
5. `05_declarations_without_receipt.sql` — declarații cu status de depunere/recipisă fără recipisă, cu istoricul tranzițiilor.
6. `06_declaration_list_duplicates.sql` — rândurile multiple per PFA per lună.
7. `07_pfa_without_cif.sql` — PFA-uri fără CIF.
8. `08_october_transactions.sql` — octombrie: curse, tranzacții bancare, înregistrări.
9. `09_cash_profile_fields.sql` — câmpurile de numerar / casă de marcat și venitul cash.
10. `10_estimate_runs_requires_clarification.sql` — rulările REQUIRES_CLARIFICATION.

Nu există migrații de date sau scripturi de corecție. Le scriu separat, idempotente, după rezultate.

## Stare după reparații (04.10.2026)

Teste: 1052 unitare + 28 arhitectură verzi. Seed-ul local e în `tests/UnitTests/Accounting/LedgerTests.Qa.cs`
(titularul „Ionescu Andrei-Victor”, transferurile 200/600/100, „From Victor I” 630, ghiseul.ro 567,
comisionul lunar de 50, Booking 822,28, Bolt 20.758,20 / 2.273,23, Uber 4.173,86 / 557,31 cu payout-urile).

| # | Reparat în cod | Test | Așteaptă date |
| --- | --- | --- | --- |
| 1 | Bancă → `BankClassifications.Sync`; ecranul Bancă arată explicația, documentul, excepția și deductibilul din REF | `QA1_ABankScreenClassificationIsTheSameEverywhere` | 03: dacă 200 lei e transfer către titular, nu Service auto |
| 2 | controlul de payout-uri cere decontarea rapoartelor confirmate | `QA2_PlatformReportsWithPayoutsBecomeIncomeAndCommission`, `QA2_ConfirmedReportsWithoutPayoutsAreNotReconciled` | 04: dacă banca de test primește payout-uri |
| 3 | `PfaBankTransactions` comun importului și controlului de sold; widgetul pe conexiunea declarată și data valutei | `QA3_TheBalanceCheckUsesTheSameTransactionsAsTheImport` | 01, 02: cifrele −142,00 / −95,01 / +272,78 |
| 4 | D301/D390 cu bază 0 nu există; fără TaxPointDate nicio declarație (regula F12) | `QA4_NoTaxPointDateOrZeroBaseGivesNoVatDeclaration` | 05: declarațiile din august generate înainte de F12 |
| 5 | un singur dicționar de etichete; tranzițiile manuale doar Admin, cu motiv | `QA5_ManualStatusChangesAreAdminOnlyWithAReason` | 05: statusuri fără recipisă |
| 6 | perioadele au `canClose` și motive din aceeași reconciliere | `QA6_PeriodsShowCanCloseFromTheSameReconciliation` | — |
| 7 | titularul cu prenume compus / inițiale | `QA7_ACompoundFirstNameOwnerIsRecognised`, `QA7_OwnerTransfersBecomeTransfersToConfirm` | 03: profilul „testr test” |
| 8 | „Verifică” cere clasificare | `QA8_VerifyRequiresAClassification` | — |
| 9 | bonul se propune doar pentru plată cu același comerciant, niciodată comision/propunere; fără preselecție | `QA9_AReceiptIsNeverMatchedWithAPaymentOfAnotherKind` | — |
| 10 | „Document lipsă” înaintea „De verificat”; contor separat pentru încasări; rânduri care se deschid | `QA10_ClientTransactionsShowRealStatesAndSeparateCounters` | 08: octombrie gol |
| 11 | `GET /pfa/declarations` din DeclarationRecord; „Taxe & declarații” le arată | `QA11_ThePfaSeesItsMonthlyDeclarations` | — |
| 12 | cifra veche „Taxe estimate (anual)” scoasă; perioada în etichete; net ≤ brut | `QA12_TheTotalIsTheSumOfTheComponentsAndNetIsAtMostGross` | sursa de venit a estimărilor rămâne `pfa_monthly_incomes` (mutarea pe ledger, pas separat) |
| 13 | numerarul citit și scris în `cash_register_states`; venit cash fără Z oprește luna | `QA13_CashIncomeWithoutZReportsBlocksTheMonth` | 09 |
| 14 | D207 agregă și D100 vechi; se oprește doar la diferență față de Σ D100 | `QA14_D207AggregatesTheMonthlyD100WhenTheRegistryIsEmpty`, `F32_QA14_…` | — |
| 15 | PFA fără CIF = alertă de profil, nu rând; starea lunii pe loc | `QA15_APfaWithoutCifIsAProfileAlertNotABatchRow` | 06, 07: duplicate de test |
| 16 | o rulare identică nu se mai salvează | `FiscalProfileTests` (QA 16) | 10 |
| 17–24 | REF drill-down fără rând gol; active „Mijloc fix / Cheltuială curentă”, date zz.ll.aaaa; Fișa MF; RJIP fără repetare și fără coloane ascunse; Document cu referință în Bancă; Bancă pe luna aleasă; un formatter de sume | `AssetSheetTests` + verificare în browser (mock) | — |
