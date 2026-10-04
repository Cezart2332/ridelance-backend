-- Ce verifică: versiunile curente de declarații cu status de depunere sau recipisă fără recipisă
-- asociată, cu istoricul tranzițiilor (status_history_json) și auditul lor.
-- Interpretare: `Accepted` (Recipisă validă) fără receipt_document_id e invalid (nu s-a încărcat recipisa
-- și nici SPV n-a asociat-o); `Submitted`/`IndexReceived` fără recipisă e normal doar până vine recipisa.
-- Un D390 cu sumă 0 și fără linii, sau o D301/D390 din facturi fără tax_point_date, e generată fără date.
-- STRICT READ-ONLY: doar SELECT.
select d.pfa_registration_id, p.full_name, d.period, d.type, v.version_no, v.status, v.amount,
       v.receipt_document_id, v.receipt_number, v.anaf_index,
       (select count(*) from public.declaration_lines l where l.declaration_version_id = v.id and l.superseded_at_utc is null) as linii,
       v.status_history_json,
       (select string_agg(a.action || ' ' || to_char(a.at_utc, 'YYYY-MM-DD HH24:MI'), ' | ' order by a.at_utc)
          from public.audit_logs a where a.entity_id = v.id::text) as audit
from public.declaration_versions v
join public.declarations d on d.id = v.declaration_id
join public.pfa_registrations p on p.id = d.pfa_registration_id
where v.version_no = (select max(o.version_no) from public.declaration_versions o where o.declaration_id = v.declaration_id)
  and (v.status in ('Accepted', 'Submitted', 'IndexReceived', 'Signed')
       or (d.type = 'D390' and coalesce(v.amount, 0) = 0
           and not exists (select 1 from public.declaration_lines l where l.declaration_version_id = v.id and l.base > 0)))
  and (v.status <> 'Accepted' or v.receipt_document_id is null or d.type = 'D390')
order by d.period, p.full_name, d.type;
