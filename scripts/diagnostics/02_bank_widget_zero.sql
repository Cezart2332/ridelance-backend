-- Ce verifică: de ce widgetul Bancă arată 0 tranzacții pe 2026. Widgetul (GetBankTransactionsQuery)
-- citește doar conexiunea curentă a utilizatorului: ultima conexiune cu status Linked (2) după
-- linked_at_utc, consimțământul ei și conturile ei active, cu booking_date (nu value_date).
-- Interpretare: dacă `conexiune_curenta` e alta decât `conexiune_declarata` sau tranzacțiile au doar
-- value_date / alt consimțământ, widgetul arată 0 deși ledgerul are înregistrări.
-- STRICT READ-ONLY: doar SELECT. Nu modifică nimic.
-- PFA-ul de test: schimbă filtrul din CTE-ul `pfa` (CUI, nume sau id) dacă e altul.

with pfa as (
  select p.id, p.user_id, p.cui, p.full_name, p.legal_name, p.holder_name
  from public.pfa_registrations p
  where p.full_name ilike '%victor%' or p.legal_name ilike '%victor%' or p.holder_name ilike '%victor%'
),
current_connection as (
  select c.* from public.bank_connections c join pfa on pfa.user_id = c.user_id
  where c.status = 2 order by c.linked_at_utc desc nulls last limit 1
)
select
  (select id from current_connection) as conexiune_curenta,
  (select provider_consent_id from current_connection) as consimtamant_curent,
  (select string_agg(d.bank_connection_id::text, ', ') from public.pfa_bank_account_declarations d join pfa on pfa.id = d.pfa_registration_id) as conexiune_declarata,
  c.id as conexiune, c.status, c.linked_at_utc, a.id as cont, a.is_active,
  count(t.id) as tranzactii_2026,
  count(t.id) filter (where t.provider_consent_id = c.provider_consent_id) as cu_consimtamantul_conexiunii,
  count(t.id) filter (where t.booking_date is null) as fara_booking_date,
  count(t.id) filter (where t.provider_consent_id = (select provider_consent_id from current_connection)
                       and a.bank_connection_id = (select id from current_connection) and a.is_active and t.booking_date is not null) as vazute_de_widget
from public.bank_connections c
join pfa on pfa.user_id = c.user_id
left join public.bank_accounts a on a.bank_connection_id = c.id
left join public.bank_transactions t on t.bank_account_id = a.id and coalesce(t.booking_date, t.value_date) >= date '2026-01-01'
group by c.id, c.status, c.linked_at_utc, a.id, a.is_active
order by c.linked_at_utc desc nulls last;
