-- Ce verifică: rulările de taxe estimate cu REQUIRES_CLARIFICATION (frecvență, PFA, motiv).
-- Interpretare: zeci de rulări identice (același motiv, aceleași sume) la ~5 minute = jobul recalculează la
-- fiecare marcare „stale” fără să se fi schimbat datele; după fix, o rulare identică nu se mai salvează.
-- STRICT READ-ONLY: doar SELECT.
select r.pfa_registration_id, p.full_name, r.tax_year, r.status, r.missing_inputs_json,
       count(*) as rulari, min(r.created_at_utc) as prima, max(r.created_at_utc) as ultima,
       round(extract(epoch from (max(r.created_at_utc) - min(r.created_at_utc))) / 60 / greatest(count(*) - 1, 1)) as minute_intre_rulari,
       count(distinct r.snapshot_json) as snapshoturi_distincte
from public.fiscal_estimate_runs r
join public.pfa_registrations p on p.id = r.pfa_registration_id
where r.status = 'REQUIRES_CLARIFICATION' or exists (
  select 1 from public.fiscal_calculations c where c.run_id = r.id and c.status = 'REQUIRES_CLARIFICATION')
group by r.pfa_registration_id, p.full_name, r.tax_year, r.status, r.missing_inputs_json
order by rulari desc;
