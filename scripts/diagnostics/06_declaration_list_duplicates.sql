-- Ce verifică: rândurile multiple per PFA per lună din lista de declarații. Lista are un rând per
-- PFA în perioadă, deci „15 rânduri IONESCU ANDREI-VICTOR” înseamnă fie 15 PFA-uri cu același nume
-- (înregistrări de test), fie mai multe declarații de același tip în aceeași lună.
-- Interpretare: secțiunea 1 > 1 = PFA-uri duplicate (date de test); secțiunea 2 > 1 = bug de generare.
-- STRICT READ-ONLY: doar SELECT.
select 'pfa cu acelasi nume' as sectiune, coalesce(p.legal_name, p.full_name) as nume, count(*) as numar,
       string_agg(p.id::text || ' (' || coalesce(p.cui, 'fara CIF') || ', ' || p.status || ')', '; ') as detalii
from public.pfa_registrations p
group by coalesce(p.legal_name, p.full_name)
having count(*) > 1
union all
select 'declaratii duplicate', p.full_name || ' ' || d.period || ' ' || d.type, count(*), string_agg(d.id::text, '; ')
from public.declarations d join public.pfa_registrations p on p.id = d.pfa_registration_id
group by p.full_name, d.pfa_registration_id, d.period, d.type
having count(*) > 1
order by 1, 3 desc;
