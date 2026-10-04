-- Ce verifică: toate câmpurile care spun „are numerar / casă de marcat” pentru PFA-ul de test și venitul
-- cash efectiv.
-- Interpretare: cash_register_states (contabilitate), pfa_fiscal_profiles (setările fiscale) și răspunsul
-- din onboarding trebuie să spună același lucru; dacă există venit cash (rapoarte cash_amount sau
-- înregistrări Cash), reconcilierea cere rapoarte Z.
-- STRICT READ-ONLY: doar SELECT. Nu modifică nimic.
-- PFA-ul de test: schimbă filtrul din CTE-ul `pfa` (CUI, nume sau id) dacă e altul.

with pfa as (
  select p.id, p.user_id, p.cui, p.full_name, p.legal_name, p.holder_name
  from public.pfa_registrations p
  where p.full_name ilike '%victor%' or p.legal_name ilike '%victor%' or p.holder_name ilike '%victor%'
)
select 'cash_register_states' as sursa, s.cash_requested::text as a, s.cash_enabled::text as b, s.status::text as c, s.activation_date::text as d
from public.cash_register_states s join pfa on pfa.id = s.pfa_registration_id
union all
select 'pfa_fiscal_profiles', f.cash_revenue_status::text, f.cash_register_status::text, null, null
from public.pfa_fiscal_profiles f join pfa on pfa.id = f.pfa_registration_id
union all
select 'venit cash din rapoarte (luna)', d.period, sum(x.cash_amount)::text, null, null
from public.platform_documents d join pfa on pfa.id = d.pfa_registration_id
join public.document_extractions x on x.platform_document_id = d.id and x.is_current
where d.document_type = 'PlatformReport' and x.cash_amount is not null group by d.period
union all
select 'inregistrari cash (luna)', to_char(e.date, 'YYYY-MM'), sum(e.amount)::text, count(*)::text, null
from public.ledger_entries e join pfa on pfa.id = e.pfa_registration_id
where e.payment_method = 'Cash' group by to_char(e.date, 'YYYY-MM')
union all
select 'rapoarte Z (luna)', to_char(z.date, 'YYYY-MM'), sum(z.total)::text, count(*)::text, null
from public.z_reports z join pfa on pfa.id = z.pfa_registration_id group by to_char(z.date, 'YYYY-MM')
order by 1, 2;
