-- Ce verifică: cum e scris titularul în profil (PFA, utilizator, declarația de cont) față de
-- contrapartidele „Victor Ionescu” din bancă.
-- Interpretare: clasificarea compară contrapartida (sau, fără ea, detaliile) cu holder_name, full_name
-- și first_name + last_name ale utilizatorului. Dacă niciunul nu e „Victor Ionescu” / „Ionescu Andrei-Victor”
-- (de ex. utilizatorul e „testr test”), titularul nu poate fi recunoscut: profilul de test trebuie corectat.
-- STRICT READ-ONLY: doar SELECT. Nu modifică nimic.
-- PFA-ul de test: schimbă filtrul din CTE-ul `pfa` (CUI, nume sau id) dacă e altul.

with pfa as (
  select p.id, p.user_id, p.cui, p.full_name, p.legal_name, p.holder_name
  from public.pfa_registrations p
  where p.full_name ilike '%victor%' or p.legal_name ilike '%victor%' or p.holder_name ilike '%victor%'
)
select 'profil' as sursa, pfa.id::text, pfa.cui, pfa.full_name, pfa.legal_name, pfa.holder_name,
       u.first_name || ' ' || u.last_name as utilizator, null::text as iban, null::numeric as suma
from pfa join public.users u on u.id = pfa.user_id
union all
select 'cont declarat', d.id::text, null, d.bank_name, null, null, null, d.iban, null
from public.pfa_bank_account_declarations d join pfa on pfa.id = d.pfa_registration_id
union all
select 'cont bancar', a.id::text, null, a.owner_name, null, null, null, a.iban, null
from public.bank_accounts a join pfa on pfa.user_id = a.user_id
union all
select 'tranzactie', t.id::text, to_char(coalesce(t.booking_date, t.value_date), 'YYYY-MM-DD'), t.counterparty_name, t.remittance_info, null,
       (select e.transaction_type || ' / ' || e.reconciliation_status || ' / ' || coalesce(e.proposed_classification, '-') from public.ledger_entries e where e.bank_transaction_id = t.id limit 1),
       t.counterparty_iban, t.amount
from public.bank_transactions t join pfa on pfa.user_id = t.user_id
where t.counterparty_name ilike '%victor%' or t.remittance_info ilike '%victor%' or t.counterparty_name ilike '%ionescu%' or t.remittance_info ilike '%ionescu%'
order by 1, 3;
