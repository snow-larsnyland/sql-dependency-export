using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Serialization;
using Dapper;

namespace SQLDependencyMapper
{
    public class DependencyTree
    {
        public Dictionary<string, DependecyNode> StringIndex;
        public Dictionary<int, DependecyNode> IdIndex;

        public DependencyTree(IEnumerable<DependecyNode> nodes)
        {
            Roots = nodes.Where(o => o.Parents.Count == 0).ToList();
            StringIndex = nodes.ToDictionary(x => $"{x.SqlObject.SCHEMA}.{x.SqlObject.NAME}");
            IdIndex = nodes.ToDictionary(x => x.Id);
        }

        public List<DependecyNode> Roots { get; private set; }
    }

    public class DependecyNode
    {
        public DependecyNode(string name, SqlObject obj)
        {
            Id = Program.IdCounter++;
            SqlObject = obj;

            Children = new HashSet<int>();
            Parents = new HashSet<int>();
        }

        public int Id { get; private set; }
        public string Name => $"{SqlObject.SCHEMA}.{SqlObject.NAME}";
        public SqlObject SqlObject { get; private set; }

        public HashSet<int> Children { get; private set; }
        public HashSet<int> Parents { get; private  set; }
    }

    class Program
    {
        public static int IdCounter = 0;
        public static string InitialCatalog = "SnowLicenseManager";
        const string dependsQuery = "sp_depends";
        const string tablesQuery = "SELECT TABLE_CATALOG, TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES;";
        const string routinesQuery = "SELECT ROUTINE_CATALOG, ROUTINE_SCHEMA, ROUTINE_NAME, ROUTINE_TYPE, ROUTINE_BODY, ROUTINE_DEFINITION, CREATED, LAST_ALTERED FROM INFORMATION_SCHEMA.ROUTINES;";

        static void Main(string[] args)
        {
            var connectionStrbuilder = new SqlConnectionStringBuilder();

            connectionStrbuilder.DataSource = ".";
            connectionStrbuilder.InitialCatalog = InitialCatalog;
            connectionStrbuilder.UserID = "sa";
            connectionStrbuilder.Password = "password123!";

            IEnumerable<DependecyNode> tables;
            IEnumerable<DependecyNode> routines;

            using (var connection = new SqlConnection(connectionStrbuilder.ConnectionString))
            {
                tables = connection.Query<Table>(tablesQuery).Select(o => new DependecyNode($"{o.TABLE_SCHEMA}.{o.TABLE_NAME}", new SqlObject(o)));
                routines = connection.Query<Routine>(routinesQuery).Select(o => new DependecyNode($"{o.ROUTINE_SCHEMA}.{o.ROUTINE_NAME}", new SqlObject(o)));
            }

            var dTree = new DependencyTree(tables.Union(routines));

            foreach(var item in tables)
            {
                var itemName = item.Name;

                using (var connection = new SqlConnection(connectionStrbuilder.ConnectionString))
                {
                    var res = connection.QueryMultiple(dependsQuery, new { objName = itemName }, commandType: CommandType.StoredProcedure);

                    List<ObjReference> localRefs = new List<ObjReference>();
                    List<ObjReference> extRefs = new List<ObjReference>();

                    try
                    { 
                        localRefs = res.Read<ObjReference>().ToList();
                        extRefs = res.Read<ObjReference>().ToList();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{itemName} has no dependencies");
                    }

                    if (localRefs.Count == 0 && extRefs.Count == 0)
                        continue;

                    var firstLocal = localRefs.First();

                    if(extRefs.Count == 0 && !firstLocal.IsLocal)
                    {
                        extRefs = localRefs;
                        localRefs = new List<ObjReference>();
                    }

                    foreach(var r in localRefs)
                    {
                        if (r.name == itemName)
                            continue;

                        if (dTree.StringIndex.ContainsKey(r.name))
                        {
                            dTree.StringIndex[r.name].Parents.Add(item.Id);
                            item.Children.Add(dTree.StringIndex[r.name].Id);
                        }
                    }

                    foreach (var r in extRefs)
                    {
                        if (r.name == itemName)
                            continue;

                        if (dTree.StringIndex.ContainsKey(r.name))
                        {
                            dTree.StringIndex[r.name].Children.Add(item.Id);
                            item.Parents.Add(dTree.StringIndex[r.name].Id);
                        }
                    }

                    // Check by text if it is referenced by routines ( Dynamic queries )
                    var textMatches = GetTextMatches(routines, item);

                    foreach(var routine in textMatches)
                    {
                        item.Parents.Add(routine.Id);
                        routine.Children.Add(item.Id);
                    }
                }
            }

            Grapher.Write(dTree);
        }

        public static IEnumerable<DependecyNode> GetTextMatches(IEnumerable<DependecyNode> routines, DependecyNode find)
        {
            var matches = new List<DependecyNode>();
            foreach(var item in routines)
            {
                if(item.SqlObject.TEXT.Contains(find.Name, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(item);
                }
            }

            return matches;
        }
    }

    

    public struct ObjReference
    {
        public string name;
        public string type;
        public string updated;
        public string selected;
        public string column;

        public bool IsLocal => !string.IsNullOrEmpty(updated) || !string.IsNullOrEmpty(selected) || !string.IsNullOrEmpty(column);
    }

    public enum SqlObjectType
    {
        UNMAPPED,
        TABLE,
        VIEW,
        PROCEDURE,
        FUNCTION
    }

    public class SqlObject
    {
        public SqlObject()
        {

        }

        public SqlObject(Table table)
        {
            Ref = table;

            CATALOG = table.TABLE_CATALOG;
            SCHEMA = table.TABLE_SCHEMA;
            NAME = table.TABLE_NAME;
            TEXT = string.Empty;

            TYPE = SqlObjectType.UNMAPPED;

            switch (table.TABLE_TYPE)
            {
                case "BASE TABLE":
                    TYPE = SqlObjectType.TABLE;
                    break;
                case "VIEW":
                    TYPE = SqlObjectType.VIEW;
                    break;
            }
        }

        public SqlObject(Routine routine)
        {
            Ref = routine;

            CATALOG = routine.ROUTINE_CATALOG;
            SCHEMA = routine.ROUTINE_SCHEMA;
            NAME = routine.ROUTINE_NAME;
            TEXT = routine.ROUTINE_DEFINITION;

            TYPE = SqlObjectType.UNMAPPED;

            switch (routine.ROUTINE_TYPE)
            {
                case "PROCEDURE":
                    TYPE = SqlObjectType.PROCEDURE;
                    break;
                case "FUNCTION":
                    TYPE = SqlObjectType.FUNCTION;
                    break;
            }
        }

        public string CATALOG { get; private set; }
        public string SCHEMA { get; private set; }
        public string NAME { get; private set; }
        public string TEXT { get; private set; }
        public SqlObjectType TYPE { get; private set; }

        public override int GetHashCode()
        {
            return HashCode.Combine(CATALOG, SCHEMA, NAME, TYPE);
        }
        public override bool Equals(object obj)
        {
            if (obj is SqlObject o)
                return CATALOG == o.CATALOG &&
                       SCHEMA == o.SCHEMA &&
                       NAME == o.NAME &&
                       TYPE == o.TYPE;
            return false;
        }

        public object Ref;
    }

    public struct Table
    {
        public string TABLE_CATALOG;
        public string TABLE_SCHEMA;
        public string TABLE_NAME;
        public string TABLE_TYPE;
    }

    public struct Routine
    {
        public string ROUTINE_CATALOG;
        public string ROUTINE_SCHEMA;
        public string ROUTINE_NAME;
        public string ROUTINE_TYPE;
        public string ROUTINE_BODY;
        public string ROUTINE_DEFINITION;
        public DateTime? CREATED;
        public DateTime? LAST_ALTERED;
    }

}
