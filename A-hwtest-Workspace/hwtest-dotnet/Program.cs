// See https://aka.ms/new-console-template for more information

using Kusto.Language;
using Kusto.Language.Syntax;
using System;
using System.IO;

class Program
{
    static void Main()
    {
        Console.WriteLine("Hello, World!");
        var script = "";
        string filePath = "C:/Users/Hongjing-Data/VSCode-Workspace/delta-kusto/A-hwtest-Workspace/hwtest-dotnet/script.kql";
        try
        {
            // Read the entire file content
            script = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            // Handle any errors that might occur
            Console.WriteLine("An error occurred: " + ex.Message);
        }
        Console.WriteLine(script);
        var code = KustoCode.Parse(script);
        var syntax = code.Syntax;

        // var list = syntax
        //     .GetDescendants<SkippedTokens>(null);

        // foreach (var function in list)
        // {
        //     Console.WriteLine("hwtest");
        //     Console.WriteLine(function.Name.SimpleName);
        // }
        Console.WriteLine("hwtest count:");
        Console.WriteLine(syntax.ChildCount);
        for (int i = 0; i < syntax.ChildCount; i++)
        {
            Console.WriteLine("hwtest:" + i);
            var child = syntax.GetChild(i);
            Console.WriteLine(child != null ? child.Kind : "null");
            Console.WriteLine(child != null ? child : "null");
        }
    }
}