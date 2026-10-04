-- Ce verifică: PFA-urile fără CIF care intră în perimetrul contabil (colaborare activă).
-- Interpretare: fiecare rând e un profil de completat; după fix, aceste PFA-uri nu mai intră în lotul
-- lunar și apar ca alertă de profil.
-- STRICT READ-ONLY: doar SELECT.
select p.id, p.full_name, p.legal_name, p.status, p.created_at_utc,
       exists (select 1 from public.pfa_accounting_engagements e where e.pfa_registration_id = p.id and e.status = 'Active') as colaborare_activa
from public.pfa_registrations p
where nullif(trim(coalesce(p.cui, '')), '') is null
order by colaborare_activa desc, p.created_at_utc;
