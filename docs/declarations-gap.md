# Gap list — Motor declarații fiscale PFA

Audit al motorului de declarații față de `specs/Spec — Motor declarații fiscale PFA ridesharing.md`
(repo frontend), secțiunile 4–6. Scris la 03.10.2026, pe `main`.

Convenție: **Există**, **Parțial**, **Lipsește**, **Contrazice** (încalcă o regulă nenegociabilă).

## Ce există

| Spec | Cod | Observații |
| --- | --- | --- |
| Monthly Tax Engine | `Application.Accounting.Tax.MonthlyTaxEngine` (funcție pură) + `Months/*` (pre-check, joburi în lot, generare) | D100, D301, D390 din facturile de comision confirmate ale lunii documentului. |
| `CommissionInvoice` | `PlatformDocument` (tip `CommissionInvoice`) + `DocumentExtraction` | Are `TaxPointDate`, dar opțional; furnizorul se ia din `SupplierTaxProfile` după codul TVA. |
| `TaxRule` | Tabele separate, fiecare versionată: `VatRate`, `D100Rule`, `SupplierTaxProfile` (cota D100 pe entitate), `AnafDeclarationSchema` (versiune formular + validator), `ExpenseCategoryRule`, `FixedAssetRule`; CAS/CASS/impozit în `FiscalEstimates/Parameters/tax-{an}.json` | Nu există un `ResolveRule` unic și nici validarea configurării. |
| `TaxProfile` / art. 317 | Setarea versionată `art317` (`PfaAccountingSetting`) + `VatRegistrationRequest` (fluxul D700) | D700 se generează când clientul răspunde „Nu” în onboarding. |
| `PersonalTaxProfile` | `FiscalProfiles` (răspunsurile anuale ale PFA-ului) + `StaffTaxInputs` (alte venituri, pierderi reportate, CASS opțional) | Fără confirmarea explicită a veniturilor externe (F53). |
| `AnnualTaxEngine` | `FiscalEstimates.TaxEngine2026` (estimări „Cât să pui deoparte”) | Calculează CAS/CASS/impozit, dar ca estimare, nu din REF-ul final. |
| `DeclarationRecord` | `Declaration` + `DeclarationVersion` (+ `DeclarationLine` cu sursa fiecărui rând) | Versiunile rectificative există; validarea pe 3 niveluri; recipisa din SPV se leagă după tip + perioadă. |
| Validator | `ridelance-anaf-validator` prin `AnafValidatorClient` | Versiunea validatorului e pe schema ANAF, nu pe record. |
| Semnare / depunere / SPVWS2 | `TransitionDeclarationVersion`, `Spv/*` | Indexul ANAF nu se salvează separat de numărul recipisei. |

## Reguli nenegociabile

| Regulă | Stare |
| --- | --- |
| 1. Versionare pe perioadă | **Parțial**: cotele și schemele au perioade; **contrazic**: codul de obligație `634` (scris în `D100MapperV2`), termenul „25 a lunii următoare” (`AnafFormat.DueDate`), lista țărilor UE (`AccountingOptions.EuCountries`, fără perioade), rotunjirea (opțiuni). |
| 2. Fără reguli pe brand | **Există** în calcul: furnizorul se identifică după codul TVA. Brandul apare doar la afișare și la completitudinea documentelor (pre-check: „lipsește raportul Bolt”). Nu există validarea configurării care să refuze o regulă pe brand (scenariul 8). |
| 3. O sursă pentru declarații pereche | **Există** pentru D301/D390 (D390 grupează liniile D301). Lipsește controlul explicit Σ D390 = D301 (F17). D207 nu există. |
| 4. Anul venitului ≠ anul formularului | **Lipsește** (nu există declarații anuale); lunar, schema se alege după perioadă. |
| 5. Depusele nu se șterg | **Există** (`IAccountingRecord`, versiuni). |
| 6. Nu se inventează cifre | **Contrazice**: fără `TaxPointDate`, exigibilitatea cade pe sfârșitul perioadei facturate sau pe data facturii (`FiscalDate.Of`). |

## Reguli de calcul

| ID | Stare |
| --- | --- |
| F01–F02 | **Există**: întrebarea din onboarding, D700 generat la „Nu”. |
| F03 | **Parțial**: `Registered` cere codul primit, dar nu verifică recipisa validă + vectorul fiscal. |
| F04 | **Parțial**: blochează luna, dar nu creează task D700. |
| F10–F11 | **Există**. |
| F12 | **Contrazice**: luna e luna documentului încărcat (`PlatformDocument.Period`), iar exigibilitatea are fallback. |
| F13 | **Parțial**: cursul după `ExchangeRateDateRule` (opțiune); sursa cursului nu se salvează pe factură (Q1). |
| F14–F16 | **Există**. |
| F17 | **Lipsește** controlul. |
| F18 | **Există**. |
| F20 | **Contrazice**: D100 e în luna facturii, nu a plății/decontării. |
| F21–F22 | **Parțial**: cota pe furnizor (`SupplierTaxProfile`), certificatul pe furnizor; fără regulă de fallback. |
| F23 | **Parțial**: certificat lipsă blochează toată luna, nu doar D100; nu există decizie `NeedsLegalConfirmation` confirmabilă de Admin. |
| F24 | **Contrazice**: `634` în cod. |
| F25 | **Lipsește** validarea. |
| F30–F32 (D207) | **Lipsește**. Nu există XSD D207 în repo. |
| F40–F43 (D205) | **Lipsește**; `D100_RENT_INDIVIDUAL` există ca interfață goală. |
| F50–F55 (D212) | **Lipsește** (doar estimări). |
| F60–F61 (C801) | **Lipsește**. |

## Pipeline și stări

| Spec | Cod | Stare |
| --- | --- | --- |
| Draft → DataValidated → XmlGenerated → AnafValidatorOk → Signed → Submitted → IndexReceived → ReceiptValid / ReceiptError | `Generated → Validated → ReadyToSign → Signed → Submitted → Accepted / Rejected` | **Parțial**: lipsesc `IndexReceived` și indexul ANAF; `Accepted`/`Rejected` = `ReceiptValid`/`ReceiptError`. |
| `FormVersion`, `RulesetVersion`, `GeneratedFromSnapshotId`, `XmlHash` | schema pe versiune, snapshot JSON | **Lipsesc** hash-ul XML și versiunea regulilor; nu se poate verifica „recalcularea dă același `XmlHash`”. |
| Rectificări | versiune `Rectificative`; D710 blocat (lipsește schema) | **Parțial**. |
| Task automat de rectificare la modificarea ledger-ului | — | **Lipsește**. |

## Decizii implicite pentru întrebările deschise

- **Q1** Cursul: BNR din ziua `TaxPointDate` sau ultima publicare anterioară (regula actuală, acum în `TaxRule` `ExchangeRate`); sursa se salvează pe linie.
- **Q2** Payout nereconciliat: avertisment sub pragul din `TaxRule` `Materiality` (seed 1 leu), `Stop` peste.
- **Q3** Chiria de la persoană fizică: ramura D205 există, regula de reținere e seed-uită neconfirmată, deci nu produce rânduri până la confirmare.
- **Q4** D212: modelul de date și PDF-ul de lucru; depunerea rămâne pe aplicația web ANAF, cu indexul și recipisa salvate pe record.
- **Q5** C801: RIDElance păstrează statusul, documentul și NUI-ul; nu depune.

## Ordinea de implementare

1. `TaxRule` + `ResolveRule` + validarea configurării + seed; mutarea `634`, a termenelor, a țărilor UE și a rotunjirii.
2. D301/D390 pe `TaxPointDate` explicit, control F17; F04 cu task D700; F03.
3. `NonResidentPayment` + `NonResidentTaxDecision`, D100 pe luna plății, confirmarea Admin (F20–F25).
4. Starea record-ului: index ANAF, `ReceiptError` → `NeedsAttention`, `XmlHash`, versiunea regulilor, task de rectificare.
5. D207 (F30–F32), D205 (F40–F43), D212 (F50–F55), C801 (F60–F61).
6. UI Admin (lot lunar, card PFA, anual) și PFA.

## Stare după implementare (03.10.2026)

| Pas | Commit | Stare |
| --- | --- | --- |
| 1. `TaxRule` versionat, `TaxRuleSet.Resolve/Find/Validate`, seed = migrare | `9145b15` | **Există**. `634`, codul bugetar, termenele, statele UE, cursul și rotunjirea vin din reguli. `634` rămâne doar ca enumerare în patch-ul XSD din `AnafSchemas.cs` (adaptarea schemei, nu regulă fiscală). |
| 2. D301/D390 pe `TaxPointDate` explicit (F10–F18), F03, F04 cu task D700 | `7f64066` | **Există**. |
| 3. D100 pe data plății (F20–F25), `NonResidentTaxEngine`, coada de confirmare | `676fdfa` | **Există**. Regulile pe furnizor rămân în registrul de furnizori, expuse ca `TaxRule`. |
| 4. Record: `IndexReceived`, index ANAF, recipisă cu erori → „Necesită atenție”, `XmlHash`, `RulesetVersion`, task de rectificare | `677af63` | **Există**. |
| 5. D207, D205, D212, C801 (F30–F61) | `55ed118` | **Există**. Anualele sunt `Declaration` cu perioada `yyyy`, fără XML (depunere manuală, Q4); validarea recalculează și compară snapshot-ul, hash-ul e stabil la recalcul. |
| 6. UI Admin (tab „Anual”, coada de reguli, C801, index ANAF) și PFA (întrebarea F53, sumele D212) | acest commit | **Există**. |

Rămân deschise:

- D100 cu obligația de chirie: generatorul XML D100 scrie o singură obligație; o lună cu reținere pe chirie e blocată cu motivul afișat și se depune manual până la un mapper cu mai multe obligații (și până la confirmarea Q3).
- D207 nu are generator XML (nu există XSD D207 în repo); modelul se transcrie în Soft J.
- Formularul D212 2027: câmpurile sunt pe secțiuni, fără numerele de rând ANAF; se completează când ANAF publică modelul.
- Pierderile reportate: istoricul vine din D212 anterioară depusă prin RIDElance sau din suma preluată în profilul fiscal; expirarea după 7 ani nu e modelată.
- Regulile pe brand: testul `NoBrandBranchesInTheTaxEngine` caută în motor; `PlatformLabels` e doar afișare.
