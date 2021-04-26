﻿using DeltaKustoLib.CommandModel;
using DeltaKustoLib.SchemaObjects;
using Kusto.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace DeltaKustoLib.KustoModel
{
    public class DatabaseModel
    {
        private static readonly IImmutableSet<Type> INPUT_COMMANDS = new[]
        {
            typeof(CreateFunctionCommand),
            typeof(CreateTableCommand),
            typeof(CreateTablesCommand),
            typeof(AlterMergeTableColumnDocStringsCommand)
        }.ToImmutableHashSet();

        private readonly IImmutableList<CreateFunctionCommand> _functionCommands;
        private readonly IImmutableList<TableModel> _tableModels;

        private DatabaseModel(
            IImmutableList<CreateFunctionCommand> functionCommands,
            IImmutableList<TableModel> tableModels)
        {
            _functionCommands = functionCommands;
            _tableModels = tableModels;
        }

        public static DatabaseModel FromCommands(
            IEnumerable<CommandBase> commands)
        {
            var commandTypeIndex = commands
                .GroupBy(c => c.GetType())
                .ToImmutableDictionary(g => g.Key, g => g as IEnumerable<CommandBase>);
            var commandTypes = commandTypeIndex
                .Keys
                .Select(key => (key, commandTypeIndex[key].First().CommandFriendlyName));
            var createFunctions = GetCommands<CreateFunctionCommand>(commandTypeIndex);
            //  Flatten the .create tables to integrate them with .create table
            var createPluralTables = GetCommands<CreateTablesCommand>(commandTypeIndex)
                .SelectMany(c => c.Tables.Select(t => new CreateTableCommand(
                    t.TableName,
                    t.Columns,
                    c.Folder,
                    c.DocString)));
            var createTables = GetCommands<CreateTableCommand>(commandTypeIndex)
                .Concat(createPluralTables)
                .ToImmutableArray();
            var alterMergeTableColumns =
                GetCommands<AlterMergeTableColumnDocStringsCommand>(commandTypeIndex);
            var alterMergeTableSingleColumn = alterMergeTableColumns
                .SelectMany(a => a.Columns.Select(
                    c => new AlterMergeTableColumnDocStringsCommand(a.TableName, new[] { c })));

            ValidateCommandTypes(commandTypes);
            ValidateDuplicates(createFunctions, f => f.FunctionName.Name);
            ValidateDuplicates(createTables, t => t.TableName.Name);
            ValidateDuplicates(
                alterMergeTableSingleColumn,
                a => $"{a.TableName.Name}_{a.Columns.First().ColumnName}");

            var tableModels = TableModel.FromCommands(createTables, alterMergeTableColumns);

            return new DatabaseModel(createFunctions, tableModels);
        }

        public static DatabaseModel FromDatabaseSchema(DatabaseSchema databaseSchema)
        {
            var functions = databaseSchema
                .Functions
                .Values
                .Select(s => CreateFunctionCommand.FromFunctionSchema(s));
            var tables = databaseSchema
                .Tables
                .Values
                .Select(s => TableModel.FromTableSchema(s));

            return new DatabaseModel(
                functions.ToImmutableArray(),
                tables.ToImmutableArray());
        }

        public IImmutableList<CommandBase> ComputeDelta(DatabaseModel targetModel)
        {
            var functionCommands =
                CreateFunctionCommand.ComputeDelta(_functionCommands, targetModel._functionCommands);
            var tableCommands =
                TableModel.ComputeDelta(_tableModels, targetModel._tableModels);
            var deltaCommands = functionCommands
                .Concat(tableCommands);

            return deltaCommands.ToImmutableArray();
        }

        private static IImmutableList<T> GetCommands<T>(
            IImmutableDictionary<Type, IEnumerable<CommandBase>> commandTypeIndex)
        {
            if (commandTypeIndex.ContainsKey(typeof(T)))
            {
                return commandTypeIndex[typeof(T)]
                    .Cast<T>()
                    .ToImmutableArray();
            }
            else
            {
                return ImmutableArray<T>.Empty;
            }
        }

        private static void ValidateCommandTypes(IEnumerable<(Type type, string friendlyName)> commandTypes)
        {
            var extraCommandTypes = commandTypes
                .Select(p => p.type)
                .Except(INPUT_COMMANDS);

            if (extraCommandTypes.Any())
            {
                var typeToNameMap = commandTypes
                    .ToImmutableDictionary(p => p.type, p => p.friendlyName);

                throw new DeltaException(
                    "Unsupported command types:  "
                    + $"{string.Join(", ", extraCommandTypes.Select(t => typeToNameMap[t]))}");
            }
        }

        private static void ValidateDuplicates<T>(
            IEnumerable<T> commands,
            Func<T, string> keyExtractor)
            where T : CommandBase
        {
            var duplicates = commands
                .GroupBy(o => keyExtractor(o))
                .Where(g => g.Count() > 1)
                .Select(g => new
                {
                    Name = g.Key,
                    CommandFriendlyName = g.First().CommandFriendlyName,
                    Count = g.Count()
                });
            var duplicate = duplicates.FirstOrDefault();

            if (duplicate != null)
            {
                var duplicateText = string.Join(
                    ", ",
                    duplicates.Select(d => $"(Name = '{d.Name}', Count = {d.Count})"));

                throw new DeltaException(
                    $"{duplicate.CommandFriendlyName} have duplicates:  {{ {duplicateText} }}");
            }
        }
    }
}