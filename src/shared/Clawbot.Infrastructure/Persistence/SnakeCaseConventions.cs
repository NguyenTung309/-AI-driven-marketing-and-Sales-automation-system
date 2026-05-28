using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Persistence;

internal static class SnakeCaseConventions
{
    public static void ApplySnakeCase(this ModelBuilder builder)
    {
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (!string.IsNullOrEmpty(tableName) && !tableName.StartsWith("AspNet", StringComparison.Ordinal))
            {
                entity.SetTableName(ToSnake(tableName));
            }

            foreach (var prop in entity.GetProperties())
            {
                prop.SetColumnName(ToSnake(prop.GetColumnName()));
            }

            foreach (var key in entity.GetKeys())
            {
                var name = key.GetName();
                if (!string.IsNullOrEmpty(name)) key.SetName(ToSnake(name));
            }

            foreach (var fk in entity.GetForeignKeys())
            {
                var name = fk.GetConstraintName();
                if (!string.IsNullOrEmpty(name)) fk.SetConstraintName(ToSnake(name));
            }

            foreach (var idx in entity.GetIndexes())
            {
                var name = idx.GetDatabaseName();
                if (!string.IsNullOrEmpty(name)) idx.SetDatabaseName(ToSnake(name));
            }
        }
    }

    private static string ToSnake(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new StringBuilder(input.Length + 8);
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && input[i - 1] != '_' && !char.IsUpper(input[i - 1])) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
