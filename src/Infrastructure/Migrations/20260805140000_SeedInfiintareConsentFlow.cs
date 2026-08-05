using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Versiunea 1.0 a acordului de consimțământ pentru înființarea societății.
    ///
    /// Textele stau în date, nu în frontend: juridicul le va schimba, iar acordurile deja date
    /// trebuie să rămână legate de versiunea afișată atunci. O versiune nouă se adaugă printr-o
    /// migrație nouă care dezactivează rândul vechi, niciodată prin UPDATE peste el.
    ///
    /// Scrisă ca SQL brut, nu cu <c>InsertData</c>: snapshot-ul modelului e intenționat în urmă
    /// în acest proiect, iar <c>InsertData</c> are nevoie de el ca să deducă tipurile coloanelor.
    ///
    /// ATENȚIE: textele de mai jos sunt draft (spec §5.2) și trebuie validate de juridicul
    /// Consulto înainte de producție.
    /// </summary>
    public partial class SeedInfiintareConsentFlow : Migration
    {
        private const string FlowId = "b1a7f4c0-0000-4000-8000-000000000001";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dollar-quoting: textele juridice conțin diacritice și ghilimele, iar escaparea
            // manuală într-un literal SQL e exact genul de lucru care se strică tăcut.
            migrationBuilder.Sql(
                $"""
                INSERT INTO public.legal_consent_flows
                    (id, context, version, effective_from, is_active, created_at_utc)
                VALUES
                    ('{FlowId}', 'infiintare-societate', '1.0', DATE '2026-08-01', TRUE,
                     TIMESTAMPTZ '2026-08-01 00:00:00+00');
                """);

            Step(
                migrationBuilder,
                2,
                0,
                "documente_generate",
                "Documentele care vor fi generate",
                "Ce urmează să semnezi",
                "Înțeleg că, pe baza datelor furnizate, Consulto va genera documentele necesare "
                + "înființării societății: actul constitutiv, declarațiile pe proprie răspundere, "
                + "cererea de înregistrare și, după caz, documentele privind sediul social. "
                + "Documentele vor fi generate ulterior transmiterii acestui formular.",
                "Am înțeles că documentele vor fi generate ulterior, pe baza datelor furnizate.");

            Step(
                migrationBuilder,
                3,
                1,
                "mandat_completare",
                "Mandat de completare și depunere",
                "Împuternicirea Consulto",
                "Împuternicesc Consulto să completeze, în numele meu, documentele necesare "
                + "înființării societății, cu datele furnizate în acest formular, și să efectueze "
                + "demersurile de înregistrare la Oficiul Registrului Comerțului.",
                "Împuternicesc Consulto să completeze și să depună documentele în numele meu.");

            Step(
                migrationBuilder,
                4,
                2,
                "autorizare_semnatura",
                "Autorizarea semnăturii",
                "Acorzi dreptul de aplicare",
                "Autorizez în mod expres platforma Consulto să aplice semnătura mea electronică pe "
                + "documentele menționate, în numele meu. Înțeleg că această acțiune echivalează cu "
                + "semnarea documentelor de către mine și produce efecte juridice conform "
                + "legislației în vigoare.",
                "Autorizez aplicarea semnăturii mele electronice și recunosc valoarea ei juridică.");

            Step(
                migrationBuilder,
                5,
                3,
                "buna_credinta",
                "Declarație de bună-credință",
                "Confirmi corectitudinea datelor",
                "Declar că informațiile furnizate sunt corecte, complete și că nu acționez sub "
                + "nicio formă de constrângere. Confirm că am luat decizia de semnare în mod liber "
                + "și informat.",
                "Declar că informațiile sunt corecte și că acționez în mod liber și informat.");

            Step(
                migrationBuilder,
                6,
                4,
                "date_audit_termeni",
                "Date colectate și termeni",
                "Acceptul final",
                "Sunt de acord ca platforma să colecteze datele necesare auditului: adresa IP, data "
                + "și ora semnării, informații despre dispozitiv și acțiunile efectuate în cadrul "
                + "procesului de semnare.",
                "Sunt de acord cu colectarea datelor de audit și am citit Termenii și Condițiile și "
                + "Politica de Confidențialitate.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"DELETE FROM public.legal_consent_steps WHERE legal_consent_flow_id = '{FlowId}';");

            migrationBuilder.Sql(
                $"DELETE FROM public.legal_consent_flows WHERE id = '{FlowId}';");
        }

        /// <summary>Id-urile sunt deterministe, ca migrația să dea același rezultat pe orice mediu.</summary>
        private static void Step(
            MigrationBuilder migrationBuilder,
            int suffix,
            int position,
            string key,
            string title,
            string subtitle,
            string body,
            string checkboxLabel) =>
            migrationBuilder.Sql(
                $"""
                INSERT INTO public.legal_consent_steps
                    (id, legal_consent_flow_id, position, key, title, subtitle, body, checkbox_label)
                VALUES
                    ('b1a7f4c0-0000-4000-8000-00000000000{suffix}',
                     '{FlowId}',
                     {position},
                     $tag${key}$tag$,
                     $tag${title}$tag$,
                     $tag${subtitle}$tag$,
                     $tag${body}$tag$,
                     $tag${checkboxLabel}$tag$);
                """);
    }
}
