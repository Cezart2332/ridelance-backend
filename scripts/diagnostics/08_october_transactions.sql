-- Ce verifică: de ce „Tranzacții” la PFA arată „Nicio tranzacție” în octombrie, deși dashboardul
-- are 35 de curse Bolt. Tranzacțiile PFA sunt înregistrări din ledger (bancă, bonuri, Z); cursele nu sunt
-- tranzacții: devin venit prin raportul lunar și payout-ul din bancă.
-- Interpretare: curse > 0 și payout-uri = 0 → contul bancar nu primește decontările Bolt (sau importul
-- nu a rulat); payout-uri > 0 și înregistrări = 0 → importul ledger n-a rulat după sincronizarea băncii.
-- STRICT READ-ONLY: doar SELECT. Nu modifică nimic.
-- PFA-ul de test: schimbă filtrul din CTE-ul `pfa` (CUI, nume sau id) dacă e altul.

with pfa as (
  select p.id, p.user_id, p.cui, p.full_name, p.legal_name, p.holder_name
  from public.pfa_registrations p
  where p.full_name ilike '%victor%' or p.legal_name ilike '%victor%' or p.holder_name ilike '%victor%'
)
select 'curse bolt' as sursa, count(*)::text as numar, min(o.order_created_time)::text as de_la, max(o.order_created_time)::text as pana_la
from public.bolt_orders o join pfa on pfa.user_id = o.user_id
where o.order_created_time >= timestamp '2026-10-01' and o.order_created_time < timestamp '2026-11-01'
union all
select 'tranzactii banca', count(*)::text, min(coalesce(t.booking_date, t.value_date))::text, max(coalesce(t.booking_date, t.value_date))::text
from public.bank_transactions t join pfa on pfa.user_id = t.user_id
where coalesce(t.booking_date, t.value_date) between date '2026-10-01' and date '2026-10-31'
union all
select 'inregistrari ledger', count(*)::text, min(e.date)::text, max(e.date)::text
from public.ledger_entries e join pfa on pfa.id = e.pfa_registration_id
where e.date between date '2026-10-01' and date '2026-10-31'
union all
select 'ultimul import ledger', null, max(a.at_utc)::text, null
from public.audit_logs a join pfa on pfa.id = a.pfa_registration_id where a.entity = 'LedgerImport' or a.action like 'IMPORT%';
-- Notă: dacă bolt_orders are altă coloană de timp sau altă cheie către utilizator, adaptează doar filtrul.
