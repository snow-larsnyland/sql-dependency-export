using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SQLDependencyMapper
{
    public static class Grapher
    {
        public static void Write(DependencyTree dTree)
        {
            string jsonStr = JsonSerializer.Serialize(dTree.IdIndex);

            File.WriteAllText("dependencies.json", jsonStr);
        }
    }
}
