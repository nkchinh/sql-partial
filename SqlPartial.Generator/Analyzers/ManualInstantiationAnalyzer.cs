using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SqlPartial.Generator.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ManualInstantiationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SQLPG012";
    private const string Category = "Logic";

    private static readonly LocalizableString Title = "Missing Fallback SQL in manual instantiation";
    private static readonly LocalizableString MessageFormat = "Instantiation of '{0}' is missing a fallback and does not cover all configured providers. Runtime may return empty strings.";
    private static readonly LocalizableString Description = "When manually instantiating SqlStrings or SqlDynamic, you should either provide a fallback value or provide SQL for all configured DBMS providers. If you miss both, the application may return empty SQL strings on unconfigured platforms.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
    }

    private void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        if (context.Operation is not IObjectCreationOperation objectCreation)
            return;

        var type = objectCreation.Type;
        if (type == null) return;

        // 1. Check if it's one of our generated structs by name and interface
        if (type.Name != "SqlStrings" && type.Name != "SqlDynamic")
            return;

        if (!type.AllInterfaces.Any(i => i.Name == "ISqlString"))
            return;

        // 2. Analyze arguments
        var arguments = objectCreation.Arguments;
        if (arguments.Length <= 1)
        {
            // Likely the single-parameter fallback constructor or implicit conversion (which shows up as object creation sometimes)
            // If it's the 1-param constructor, it IS the fallback.
            return;
        }

        bool hasFallback = false;
        bool missingAnyProvider = false;

        foreach (var argument in arguments)
        {
            var parameter = argument.Parameter;
            if (parameter == null) continue;

            bool isEffectivelyNull = argument.ArgumentKind == ArgumentKind.DefaultValue ||
                                     (argument.Value is ILiteralOperation literal &&
                                      literal.ConstantValue.HasValue &&
                                      literal.ConstantValue.Value == null);

            if (string.Equals(parameter.Name, "fallback", StringComparison.OrdinalIgnoreCase))
            {
                if (!isEffectivelyNull)
                {
                    hasFallback = true;
                }
            }
            else
            {
                // It's a DBMS provider parameter (e.g. "postgresql", "sqlserver")
                if (isEffectivelyNull)
                {
                    missingAnyProvider = true;
                }
            }
        }

        // 3. Report if we have a gap
        if (!hasFallback && missingAnyProvider)
        {
            var diagnostic = Diagnostic.Create(Rule, objectCreation.Syntax.GetLocation(), type.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
