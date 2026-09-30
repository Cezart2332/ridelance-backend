# Gap list — Registre obligatorii PFA

Audit al modulului contabil față de `specs/Spec — Registre obligatorii PFA RIDElance.md` (repo
frontend), secțiunile 3–8. Scris la 30.09.2026, pe `main`, după spec-ul Ledger
(`docs/accounting-ledger-gap.md`).

Convenție: **Există** = implementat și conform; **Parțial** = există, dar incomplet; **Lipsește**;
**Contrazice** = cod care încalcă o regulă a spec-ului.

## Echivalențe de nume (spec → cod)

| Spec | Cod existent | Observații |
| --- | --- | --- |
| `AccountingEntry` | `LedgerEntry` | Vezi gap-ul Ledger. |
| Tax Engine | `DeductibilityService` (funcții pure, la clasificare) | Se extinde: venit impozabil, achiziții de clasificat, amortizare. |
| `Asset` | `PfaAsset` (`pfa_assets`) | CRUD simplu: tip, descriere, dată, valoare, document, `InUse`/`Disposed`. Se extinde, nu se dublează. |
| `AssetStatus` | `AssetStatus { InUse, Disposed }` | `InUse` devine `Active`; se adaugă `PendingClassification`, `FullyDepreciated`. |
| `DepreciationLine` | — | Lipsește. |
| `InventoryCount` / `InventoryItem` | — | Registrul-inventar e generat din active, fără inventariere. |
| Snapshot lună | `AccountingPeriodSnapshot` (RJIP + REF JSON, PDF RJIP) | Există de la spec-ul Ledger. |
| An închis | — | Nu există închiderea anului. REF-ul e „Final” când toate lunile sunt închise. |
| Pachet anual | `RunHandoverPackageCommand` (ZIP, job) | Dosarul de predare, refolosibil pentru exporturile de registre. |

## §1 Reguli nenegociabile

| Regulă | Stare |
| --- | --- |
| Nicio sursă nu scrie direct în registre | **Există**: RJIP, REF și Registrul-inventar sunt query-uri peste ledger / active. |
| Operațiune unică în ledger | **Există** (spec Ledger). |
| RJIP = încasare/plată; REF = tratament fiscal | **Parțial**: REF citește `Amount` al veniturilor, nu un venit impozabil stabilit de Tax Engine. |
| Registrul-inventar e snapshot de inventariere | **Contrazice**: e lista activelor la 31.12, fără inventariere, numerar, bancă, creanțe, datorii. |
| Fișa MF doar pentru mijloace fixe | **Lipsește** Fișa MF. |
| Regenerare identică pentru o perioadă închisă | **Parțial**: snapshot-ul RJIP la închiderea lunii există; exportul nu îl folosește și nu există test de identitate. |

## §3 RJIP

| Cerință | Stare |
| --- | --- |
| Model 14-1-1/b, coloane, total lunar, antet PFA/CUI | **Există** (PDF + XLSX). Antetul are perioada, nu anul și luna. |
| Export PDF + CSV, pe lună / interval / an | **Parțial**: PDF + XLSX pe interval; lipsește CSV și totalul anual. |
| Coloana Document: „Extras bancar” la bancă, justificativul în explicații | **Contrazice**: la bancă se scrie eticheta înregistrării („Extras 10.10.2026”), justificativul nu apare. |
| Venit brut Uber/Bolt la încasări, comisionul la plăți (R21) | **Există** (spec Ledger). |
| Lună închisă → exportul e snapshot-ul, identic cu regenerarea | **Lipsește** (vezi §1). |

## §4 REF

| Cerință | Stare |
| --- | --- |
| Calcul oricând, pe an și sursă, fără RJIP | **Parțial**: pe an, o singură sursă; în același fișier cu RJIP (nu depinde de el, dar nu e separat). |
| `TaxableIncomeAmount` + `IncomeSource` pe entry, completate de Tax Engine | **Lipsește**. |
| Cheltuieli: `DeductibleAmount` + amortizarea anului | **Parțial**: fără amortizare. |
| Achiziție MF: plata integral în RJIP, `DeductibleAmount = 0` | **Lipsește**: o achiziție mare se deduce după categorie (sau deloc, fără categorie). |
| Drill-down pe rând până la entry-uri | **Lipsește**. |
| REF curent vs final (snapshot după închiderea anului), PDF | **Parțial**: statusul „Final” e calculat, nu snapshot. |

## §5 Registrul-inventar

| Cerință | Stare |
| --- | --- |
| Inventariere la start, 31.12, încetare | **Lipsește** (doar lista activelor). |
| Precompletare: MF (valoare rămasă), OI, bancă, numerar, creanțe, datorii, stocuri | **Lipsește**. Banca: furnizorul Open Banking nu întoarce încă soldul (vezi Q-bancă). |
| Flux PFA (confirmă / corectează / scoate / adaugă), numerar obligatoriu | **Lipsește**. |
| Admin: diferențele cer notă; finalizare, PDF, read-only | **Lipsește**. |
| Mașina nu devine automat activ | **Există** implicit: activele se adaugă doar manual. |

## §6 Fișa MF și Assets

| Cerință | Stare |
| --- | --- |
| „Posibil mijloc fix” propus de Tax Engine (prag configurabil), deductibil 0 până la decizie | **Lipsește**. |
| Decizie Admin: cheltuială sau MF; activ cu număr de inventar MF-0001 și date din document | **Lipsește** (activele n-au legătură cu plata). |
| Clasă, durată, punere în funcțiune; `Active` doar cu toate trei | **Lipsește**. |
| Plan liniar, rotunjirea în ultima lună, start după regula configurată | **Lipsește**. |
| Ieșire din gestiune: amortizarea se oprește din luna următoare | **Parțial**: există `Disposed` + dată, fără amortizare. |
| PDF 14-2-2 + lista activelor | **Lipsește**. |
| Editări Admin cu audit; recalcul doar pe lunile deschise | **Parțial**: auditul generic prin interceptor. |

## §7 Închideri

| Cerință | Stare |
| --- | --- |
| Lună: controalele Ledger | **Există** (spec Ledger §8). |
| Lună: nicio achiziție de clasificat; amortizarea lunii calculată | **Lipsește**. |
| Lună: Z vs cash și payout-uri nereconciliate = 0 sau explicate de Admin | **Parțial**: controlul cere 0; explicația nu există. |
| La închidere: liniile de amortizare blocate | **Lipsește**. |
| „Închide anul”: 12/12 luni, inventar final, REF final, pachet anual ZIP, redeschidere Admin | **Lipsește**. |

## §8 PFA vs Admin

| Cerință | Stare |
| --- | --- |
| PFA: tranzacții, asociere bon, „Document lipsă” | **Există** (spec Ledger §8). |
| PFA: lista activelor, inventarul anual, registrele finale read-only | **Lipsește**. |
| Admin: panou status per PFA (RJIP OK · REF calculat · Inventar de confirmat · Active în clasificare) | **Lipsește**. |

## Decizii implicite pentru întrebările deschise

Aplicate până la confirmarea contabilului; toate sunt în configurare sau în tabele de reguli.

- **Q1** Coloana Document la bancă: „Extras bancar”, justificativul în explicații (propunerea spec-ului).
- **Q2** REF: cele trei rânduri din spec (venit brut, cheltuieli deductibile, venit net / pierdere);
  amortizarea e vizibilă în drill-down-ul rândului 2.
- **Q3** Amortizarea începe în luna următoare punerii în funcțiune; pragul de mijloc fix e 2.500 lei.
  Ambele stau în `fixed_asset_rules` (cu perioadă de valabilitate), iar categoriile de consum
  (carburant, service, comision etc.) nu sunt propuse ca mijloace fixe.
- **Q4** Creanțe: doar ce se adaugă manual la inventar. Cursele din decembrie decontate în ianuarie nu
  sunt creanțe (sistem real, venitul intră la încasare).
- **Q5** Mașina: niciun activ automat. Adminul creează activul doar când mașina e proprietatea PFA
  (aport sau achiziție); leasing, comodat și proprietate personală rămân cheltuieli.
- **Q-bancă** Soldul bancar la data inventarului: soldul raportat de bancă, dacă furnizorul îl
  trimite, minus mișcările de după dată; altfel suma mișcărilor bancare din evidență. Se confirmă
  obligatoriu, ca numerarul.

## Ordinea de implementare

1. Model de date: `TaxableIncomeAmount`, `IncomeSource`, starea de clasificare MF pe `LedgerEntry`;
   `PfaAsset` extins, `DepreciationLine`, `FixedAssetRule`, `InventoryCount`, `InventoryItem`,
   `AccountingYear`, explicațiile controalelor. Migrații.
2. Tax Engine: venit impozabil, achiziții de clasificat (deductibil 0), plan de amortizare.
3. Assets: decizie Admin, număr de inventar, plan, ieșire din gestiune, audit.
4. RJIP: coloana Document, CSV, total anual, snapshot la lunile închise.
5. REF: venit impozabil + amortizare, drill-down, final din snapshot, fără dependență de RJIP.
6. Registrul-inventar: inventariere, flux PFA, revizuire Admin, finalizare, PDF.
7. Fișa MF 14-2-2 + lista activelor.
8. Închiderea lunii (controale noi) și a anului, pachet anual.
9. Panou status per PFA; ecranele PFA.

## Stare după implementare (01.10.2026)

Toți pașii de mai sus sunt făcuți, cu teste pe scenariile 1–9 și pe criteriile de acceptanță:

- regenerarea RJIP pentru o lună închisă = snapshot-ul închiderii (`Rjip_AClosedMonthRegeneratesExactlyItsSnapshot`);
- REF nu depinde de RJIP (`ArchitectureTests.Layers.RegisterTests`);
- niciun cod nu scrie în registre: RJIP, REF, Registrul-inventar și Fișa MF sunt generate; se salvează doar snapshot-uri.

Rămase deschise, de confirmat cu contabilul: Q2 (rândurile exacte REF), Q3 (pragul și luna de start sunt în
`fixed_asset_rules`, fără ecran de editare încă), Q4 (creanțele se adaugă doar manual la inventar), Q5.
Soldul bancar vine doar dacă banca îl trimite în `balances`; altfel inventarul pornește din mișcările importate.
