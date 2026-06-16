using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SqlPartial.Generator.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SqlMethodAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SQLPG030";
    private const string Category = "Design";

    private static readonly LocalizableString Title = "Missing SqlProviderName property";
    private static readonly LocalizableString MessageFormat = "The type '{0}' must define a 'string SqlProviderName' property (static or instance) to use the [Sql] attribute on its parameters";
    private static readonly LocalizableString Description = "When using the [Sql] attribute to generate provider-specific overloads, the containing type must provide a way to determine the current DBMS provider at runtime.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;

        // Check if any parameter has the [Sql] attribute
        bool hasSqlAttribute = method.Parameters.Any(p =>
            p.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "SqlPartial.SqlAttribute" ||
                a.AttributeClass?.Name == "SqlAttribute"));

        if (!hasSqlAttribute)
            return;

        var containingType = method.ContainingType;
        if (containingType == null)
            return;

        // Check if the type (or its bases/interfaces) has SqlProviderName
        bool hasProviderName = false;

        if (method.IsExtensionMethod && method.Parameters.Length > 0)
        {
            // For extension methods, check the extended type (first parameter)
            if (method.Parameters[0].Type is INamedTypeSymbol extendedType)
            {
                hasProviderName = HasSqlProviderName(extendedType, false); // Extensions use instance properties
            }
        }

        if (!hasProviderName)
        {
            hasProviderName = HasSqlProviderName(containingType, method.IsStatic);
        }

        if (!hasProviderName)
        {
            // Report error on the first parameter that has the attribute
            var firstParam = method.Parameters.First(p =>
                p.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == "SqlPartial.SqlAttribute" ||
                    a.AttributeClass?.Name == "SqlAttribute"));

            var attribute = firstParam.GetAttributes()
                .First(a =>
                    a.AttributeClass?.ToDisplayString() == "SqlPartial.SqlAttribute" ||
                    a.AttributeClass?.Name == "SqlAttribute");

            var location = attribute.ApplicationSyntaxReference?
                .GetSyntax()
                .GetLocation() ?? firstParam.Locations[0];

            var diagnostic = Diagnostic.Create(Rule, location, containingType.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool HasSqlProviderName(INamedTypeSymbol type, bool mustBeStatic)
    {
        // Search in current type and all base types / interfaces
        var current = type;
        while (current != null)
        {
            if (CheckType(current, mustBeStatic)) return true;

            // Also check interfaces
            if (current.AllInterfaces.Any(i => CheckType(i, mustBeStatic))) return true;

            current = current.BaseType;
        }

        return false;
    }

    private static bool CheckType(ITypeSymbol type, bool mustBeStatic)
    {
        return type.GetMembers("SqlProviderName")
            .OfType<IPropertySymbol>()
            .Any(p => (!mustBeStatic || p.IsStatic) && p.Type.SpecialType == SpecialType.System_String);
    }
}
