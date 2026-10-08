using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;

namespace NzbDrone.Core.Datastore
{
    // VersionInfo records which migration numbers ran, not what they did. A database carried
    // over from another Readarr fork can therefore claim to be fully migrated while missing
    // columns, because that fork used some of the same numbers for different changes. This
    // compares the live schema against the columns the repositories will actually query.
    // It reports and never repairs: a skipped migration may have done more than add a column.
    public static class SchemaVerifier
    {
        private const string SqliteColumns =
            "SELECT m.\"name\" AS \"TableName\", p.\"name\" AS \"ColumnName\" " +
            "FROM sqlite_master m JOIN pragma_table_info(m.\"name\") p " +
            "WHERE m.\"type\" = 'table'";

        private const string PostgresColumns =
            "SELECT \"table_name\" AS \"TableName\", \"column_name\" AS \"ColumnName\" " +
            "FROM information_schema.columns WHERE \"table_schema\" = current_schema()";

        public static List<MissingColumn> FindMissingColumns(IDatabase database)
        {
            return Compare(ReadColumns(database));
        }

        public static List<MissingColumn> Compare(IDictionary<string, HashSet<string>> actualColumns)
        {
            var missing = new List<MissingColumn>();

            foreach (var mapping in TableMapping.Mapper.TableMap)
            {
                // TableMapping spans the main, log and cache databases, and each holds only its
                // own tables, so an absent table belongs to one of the others.
                if (!actualColumns.TryGetValue(mapping.Value, out var columns))
                {
                    continue;
                }

                missing.AddRange(ExpectedColumns(mapping.Key)
                    .Where(c => !columns.Contains(c))
                    .Select(c => new MissingColumn(mapping.Value, c)));
            }

            return missing.OrderBy(m => m.Table).ThenBy(m => m.Column).ToList();
        }

        private static IEnumerable<string> ExpectedColumns(Type type)
        {
            // The same selection BasicRepository makes when it builds its column lists.
            var excluded = TableMapping.Mapper.ExcludeProperties(type).Select(x => x.Name).ToHashSet();

            return type.GetProperties()
                .Where(x => x.IsMappableProperty() && !excluded.Contains(x.Name))
                .Select(x => x.Name)
                .Distinct();
        }

        private static Dictionary<string, HashSet<string>> ReadColumns(IDatabase database)
        {
            var sql = database.DatabaseType == DatabaseType.SQLite ? SqliteColumns : PostgresColumns;

            using (var conn = database.OpenConnection())
            {
                return conn.Query<ColumnRow>(sql)
                    .GroupBy(r => r.TableName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key,
                                  g => g.Select(r => r.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase),
                                  StringComparer.OrdinalIgnoreCase);
            }
        }

        private class ColumnRow
        {
            public string TableName { get; set; }
            public string ColumnName { get; set; }
        }
    }

    public class MissingColumn
    {
        public MissingColumn(string table, string column)
        {
            Table = table;
            Column = column;
        }

        public string Table { get; }
        public string Column { get; }

        public override string ToString()
        {
            return $"{Table}.{Column}";
        }
    }
}
