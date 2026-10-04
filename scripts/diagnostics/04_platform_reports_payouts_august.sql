-- Ce verifică: rapoartele Uber/Bolt din august 2026, facturile de comision, payout-urile din bancă și ce
-- înregistrări au generat.
-- Interpretare:
--   - raport confirmat + factură confirmată + payout-uri (PlatformSettlement sau grup de decontare) cu
--     Σ payout = venit − comision → trebuie să existe venit brut + comision cu același settlement_group_id;
--   - fără payout-uri în bancă: venitul nu se poate recunoaște (lipsesc decontările), luna e NeedsReconciliation;
--   - payout-uri cu altă sumă: diferența e motivul (R23).
-- STRICT READ-ONLY: doar SELECT. Nu modifică nimic.
-- PFA-ul de test: schimbă filtrul din CTE-ul `pfa` (CUI, nume sau id) dacă e altul.

with pfa as (
  select p.id, p.user_id, p.cui, p.full_name, p.legal_name, p.holder_name
  from public.pfa_registrations p
  where p.full_name ilike '%victor%' or p.legal_name ilike '%victor%' or p.holder_name ilike '%victor%'
)
select 'document' as tip, d.platform::text, d.document_type::text, d.status::text, d.period, x.amount as venit, x.commission_amount as comision,
       x.period_from, x.period_to, x.tax_point_date, null::uuid as grup, null::text as descriere
from public.platform_documents d join pfa on pfa.id = d.pfa_registration_id
left join public.document_extractions x on x.platform_document_id = d.id and x.is_current
where d.period = '2026-08' and d.deleted_at_utc is null
union all
select 'payout banca', null, null, null, to_char(coalesce(t.booking_date, t.value_date), 'YYYY-MM-DD'), t.amount, null, null, null, null, null, t.counterparty_name || ' / ' || coalesce(t.remittance_info, '')
from public.bank_transactions t join pfa on pfa.user_id = t.user_id
where coalesce(t.booking_date, t.value_date) between date '2026-08-01' and date '2026-09-10'
  and (t.counterparty_name ilike '%bolt%' or t.counterparty_name ilike '%uber%' or t.remittance_info ilike '%bolt%' or t.remittance_info ilike '%uber%')
union all
select 'inregistrare', e.source::text, e.transaction_type::text, e.reconciliation_status::text, to_char(e.date, 'YYYY-MM-DD'), e.amount, null, null, null, null, e.settlement_group_id, e.description
from public.ledger_entries e join pfa on pfa.id = e.pfa_registration_id
where e.date between date '2026-08-01' and date '2026-09-10' and (e.source in ('Bolt', 'Uber') or e.settlement_group_id is not null)
order by 1, 5;
