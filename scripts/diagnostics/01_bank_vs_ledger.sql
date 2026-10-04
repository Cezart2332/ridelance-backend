-- Ce verifică: tranzacțiile bancare din 2026 ale PFA-ului de test față de înregistrările din ledger
-- legate de ele, în ambele direcții, și diferența de sold pe lună (iulie, august, septembrie).
-- Interpretare:
--   (A) tranzacții fără înregistrare: coloana `motiv` spune de ce importul le-a sărit (pending, cont
--       inactiv, conexiune nelinked, consimțământ vechi, conexiune nedeclarată, propunere de asociere).
--   (B) înregistrări bancare fără tranzacție sau cu sumă diferită.
--   (C) pe lună: `banca` = Σ tranzacții (filtrul controlului de sold), `rjip` = Σ înregistrări bancare care
--       intră în RJIP (fără payout-uri nereconciliate, fără stornări), `diferenta` trebuie să fie 0.
-- STRICT READ-ONLY: doar SELECT. Nu modifică nimic.
-- PFA-ul de test: schimbă filtrul din CTE-ul `pfa` (CUI, nume sau id) dacă e altul.

with pfa as (
  select p.id, p.user_id, p.cui, p.full_name, p.legal_name, p.holder_name
  from public.pfa_registrations p
  where p.full_name ilike '%victor%' or p.legal_name ilike '%victor%' or p.holder_name ilike '%victor%'
),
decl as (
  select d.pfa_registration_id, d.bank_connection_id from public.pfa_bank_account_declarations d join pfa on pfa.id = d.pfa_registration_id
),
tx as (
  select t.*, a.is_active, a.bank_connection_id, c.status as connection_status, c.provider_consent_id as connection_consent,
         coalesce(t.booking_date, t.value_date) as day
  from public.bank_transactions t
  join pfa on pfa.user_id = t.user_id
  join public.bank_accounts a on a.id = t.bank_account_id
  join public.bank_connections c on c.id = a.bank_connection_id
  where coalesce(t.booking_date, t.value_date) between date '2026-01-01' and date '2026-12-31'
)
-- (A) tranzacții fără înregistrare
select 'A_fara_inregistrare' as sectiune, tx.id, tx.day, tx.amount, tx.currency, tx.counterparty_name, tx.remittance_info,
  case
    when tx.is_pending then 'pending'
    when not tx.is_active then 'cont inactiv'
    when tx.connection_status <> 2 then 'conexiune nelinked (status ' || tx.connection_status || ')'
    when tx.provider_consent_id is distinct from tx.connection_consent then 'consimtamant vechi'
    when exists (select 1 from decl) and tx.bank_connection_id not in (select bank_connection_id from decl where bank_connection_id is not null) then 'alta conexiune decat cea declarata'
    when exists (select 1 from public.ledger_match_proposals m join pfa on pfa.id = m.pfa_registration_id where m.bank_transaction_id = tx.id and m.accepted is not false) then 'propunere de asociere in asteptare'
    else 'neimportata (import nerulat?)'
  end as motiv
from tx
where not exists (select 1 from public.ledger_entries e join pfa on pfa.id = e.pfa_registration_id where e.bank_transaction_id = tx.id)
union all
-- (B) înregistrări bancare fără tranzacție sau cu altă sumă
select 'B_inregistrare_fara_tranzactie', e.id, e.date, e.amount, e.currency, e.counterparty, e.description,
  case when t.id is null then 'tranzactie inexistenta' else 'suma diferita: banca ' || t.amount end
from public.ledger_entries e
join pfa on pfa.id = e.pfa_registration_id
left join public.bank_transactions t on t.id = e.bank_transaction_id
where e.payment_method = 'Bank' and e.date between date '2026-01-01' and date '2026-12-31'
  and e.settlement_group_id is null and e.storno_of_entry_id is null
  and (t.id is null or t.amount <> e.amount)
order by 1, 3;

-- (C) diferența de sold pe lună
with pfa as (
  select p.id, p.user_id, p.cui, p.full_name, p.legal_name, p.holder_name
  from public.pfa_registrations p
  where p.full_name ilike '%victor%' or p.legal_name ilike '%victor%' or p.holder_name ilike '%victor%'
)
select to_char(m.month, 'YYYY-MM') as luna,
  (select coalesce(sum(t.amount), 0) from public.bank_transactions t join pfa on pfa.user_id = t.user_id
     where not t.is_pending and t.currency = 'RON' and date_trunc('month', coalesce(t.booking_date, t.value_date)) = m.month) as banca,
  (select coalesce(sum(e.amount), 0) from public.ledger_entries e join pfa on pfa.id = e.pfa_registration_id
     where e.payment_method = 'Bank' and e.currency = 'RON' and e.storno_of_entry_id is null and not e.closed_period_flag
       and e.transaction_type <> 'PlatformSettlement' and e.reconciliation_status <> 'NeedsReconciliation'
       and date_trunc('month', e.date) = m.month) as rjip,
  (select coalesce(sum(e.amount), 0) from public.ledger_entries e join pfa on pfa.id = e.pfa_registration_id
     where e.payment_method = 'Bank' and e.transaction_type = 'PlatformSettlement' and date_trunc('month', e.date) = m.month) as payout_nereconciliat
from (select generate_series(date '2026-07-01', date '2026-09-01', interval '1 month') as month) m;
