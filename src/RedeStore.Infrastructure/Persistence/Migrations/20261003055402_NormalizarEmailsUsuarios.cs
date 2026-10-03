using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RedeStore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizarEmailsUsuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A API passa a gravar e buscar e-mails sempre em minúsculas e sem espaços nas pontas.
            // Aqui os e-mails já cadastrados entram nesse formato. Se o banco já tiver contas
            // duplicadas que só diferem por maiúsculas/espaços (ex.: "Joao@x.com" e "joao@x.com"),
            // apenas uma por grupo é normalizada (a que já estiver no formato certo; senão a de menor Id),
            // pois o índice único não permite duas iguais. As demais ficam como estão e precisam ser
            // revisadas manualmente (consulta em docs/deploy.md).
            migrationBuilder.Sql("""
                WITH candidatos AS (
                    SELECT "Id",
                           lower(btrim("Email")) AS normalizado,
                           row_number() OVER (
                               PARTITION BY lower(btrim("Email"))
                               ORDER BY ("Email" = lower(btrim("Email"))) DESC, "Id"
                           ) AS ordem
                    FROM "Usuarios"
                )
                UPDATE "Usuarios" u
                SET "Email" = c.normalizado
                FROM candidatos c
                WHERE u."Id" = c."Id"
                  AND c.ordem = 1
                  AND u."Email" <> c.normalizado;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem volta: a grafia original dos e-mails não é guardada (e não é necessária).
        }
    }
}
