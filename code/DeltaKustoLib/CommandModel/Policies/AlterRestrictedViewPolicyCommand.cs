﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Kusto.Language.Syntax;

namespace DeltaKustoLib.CommandModel.Policies
{
    /// <summary>
    /// Models <see cref="https://learn.microsoft.com/en-us/azure/data-explorer/kusto/management/alter-table-restricted-view-access-policy-command"/>
    /// </summary>
    [Command(19100, "Alter restricted view Policy")]
    public class AlterRestrictedViewPolicyCommand : TableOnlyPolicyCommandBase
    {
        public bool IsEnabled { get; }

        public override string CommandFriendlyName => ".alter table policy restricted_view_access";

        public override string ScriptPath =>
            $"tables/policies/streamingingestion/create/{TableName}";

        public AlterRestrictedViewPolicyCommand(
            EntityName tableName,
            bool isEnabled)
            : base(tableName)
        {
            IsEnabled = isEnabled;
        }

        public override string ToScript(ScriptingContext? context = null)
        {
            return $".alter table {TableName} policy restricted_view_access {IsEnabled}";
        }

        internal static CommandBase? FromCode(CommandBlock commandBlock)
        {
            var nameReferences = commandBlock.GetDescendants<NameReference>();
            var tableNameReference = nameReferences.Last();
            var policyText = QuotedText.FromLiteral(
                commandBlock.GetUniqueDescendant<LiteralExpression>(
                    "StreamingIngestion",
                    e => e.NameInParent == "StreamingIngestionPolicy"));
            var policy = Deserialize<JsonDocument>(policyText.Text);

            if (policy == null)
            {
                throw new DeltaException(
                    $"Can't extract policy objects from {policyText.ToScript()}");
            }

            return new AlterRestrictedViewPolicyCommand(
                EntityName.FromCode(tableNameReference.Name),
                true);
        }

        internal static IEnumerable<CommandBase> ComputeDelta(
            AlterRestrictedViewPolicyCommand? currentCommand,
            AlterRestrictedViewPolicyCommand? targetCommand)
        {
            var hasCurrent = currentCommand != null;
            var hasTarget = targetCommand != null;

            if (hasCurrent && !hasTarget)
            {
                // No target, we remove the current policy
                throw new NotImplementedException();
                //yield return new DeleteStreamingIngestionPolicyCommand(
                //    currentCommand!.EntityType,
                //    currentCommand!.EntityName);
            }
            else if (hasTarget)
            {
                if (!hasCurrent || !currentCommand!.Equals(targetCommand!))
                {
                    // There is a target and either no current or the current is different
                    yield return targetCommand!;
                }
            }
            else
            {   // Both target and current are null: no delta
            }
        }
    }
}