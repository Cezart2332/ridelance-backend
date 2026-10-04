# Registre, rapoarte de platformă și plăți D301

Încasări și plăți și Clarifică RJIP lucrează pe aceleași `LedgerEntry`. Editarea schimbă datele, clasificarea stabilește natura contabilă. REF agregă veniturile impozabile, plățile deductibile justificate și amortizarea. Generarea unei declarații nu creează mișcări de bani.

## Raport și decont

`amount` dintr-un raport înseamnă veniturile totale înainte de comision, inclusiv alte servicii. La factura de comision reprezintă valoarea serviciilor facturate, nu soldul restant după reținerea comisionului. Extracțiile deja confirmate nu sunt rescrise automat.

Exemplul Uber august: 41.737 lei curse + 845 lei alte servicii = 42.582 lei venit; comision 9.973,50 lei; total net din raport 32.608,50 lei. Testul de ledger verifică brutul și comisionul după potrivirea unui virament de aceeași valoare, fără dubluri la reimport.

Numerarul Bolt este înregistrat prin rapoarte Z/FiscalLink; raportul platformei îl verifică, fără a-l adăuga încă o dată. „Sincronizează și reconciliază” rulează importul existent și afișează observațiile: documente lipsă, diferențe, calculul brut/numerar/comision și alte valori extrase. Nu produce venituri și comisioane dintr-o diferență neexplicată.

Bolt explică faptul că suma pentru impozitul de 2% este restituită operatorului pentru plata către ANAF: https://bolt.eu/en-ro/support/articles/10946560992402/. Termenii de plată permit alte ajustări și plăți anterioare: https://bolt.eu/ro-ro/legal/rides/tnc-serviceprovider-airwallex/. Reconcilierea lunii august cu rambursări și solduri necesită decontul detaliat și viramentele efective. În această versiune aceste componente sunt semnalate, nu aplicate automat cu un semn presupus. O taxă restituită de platformă nu reprezintă dovada achitării ei la ANAF.

## Plata D301

1. Generează/verifică declarația în Fiscalitate.
2. Importă plata efectivă din bancă.
3. În Încasări și plăți, selectează „Asociază D301”, luna obligației și confirmă că întreaga plată este TVA nerecuperabil aferent comisioanelor deductibile. Justificarea este păstrată în audit.
4. Plata existentă devine `Expense / NON_RECOVERABLE_VAT`, cu PDF-ul D301 drept document, fără o a doua înregistrare. Data și suma bancară se păstrează. D301 august plătită în septembrie contribuie la RJIP septembrie și REF anual.

Asocierea cere dosarul și documentul propriu, versiunea curentă nerejectată cu PDF și valoare pozitivă, o plată bancară în lei din lună deschisă și confirmare explicită. Nu înlocuiește alt document atașat și nu acceptă un dosar marcat plătitor de TVA. Sunt permise plăți parțiale în limita obligației D301 rotunjite la leu. O plată mixtă nu se asociază integral; trebuie verificată și defalcată. Nu se salvează reguli de contrapartidă care ar transforma toate plățile ANAF în cheltuieli deductibile.

Regula `NON_RECOVERABLE_VAT` este inserată prin migrarea `20261005120000_AddNonRecoverableVatCategory`; nu se clasifică automat după numele ANAF și nu este candidat la mijloc fix. Clasificarea generică `Tax` rămâne exclusă din REF. Categoria este ascunsă din selectarea generică a unui nou tip de cheltuială în UI, pentru a folosi fluxul de asociere.

## Validare și limite

Testele verifică plata în altă lună, REF/RJIP, idempotența, plăți parțiale, declarații/dosare incompatibile, perioade închise, documente deja asociate și venitul total Uber. Testul browser verifică alegerea lunii obligației, confirmarea tratamentului și afișarea diferențelor de reconciliere.

Aceste verificări nu confirmă situația unui dosar din producție. Sumele încasate în alte perioade, soldurile reportate și ajustările Bolt necesită documente de decontare. Asocierea D301 nu produce o nouă declarație și nu reprezintă confirmarea efectuării unei plăți către ANAF în lipsa tranzacției bancare.
