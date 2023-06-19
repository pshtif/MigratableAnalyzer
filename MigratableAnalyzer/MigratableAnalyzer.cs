/*
 *	Created by:  Peter @sHTiF Stefcek
 *	
 *	Code analyzer for serialization migration framework.
 */

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;

namespace MigratableAnalyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class MigratableAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "MigratableAnalyzer";

        private const string Category = "Naming";

        private static readonly DiagnosticDescriptor MissingAttributeRule = new DiagnosticDescriptor(
            "MigratableAnalyzer",
            "Class implementing IMigratable needs to have SerializedIdAttribute",
            "Class '{0}' needs to have SerializedIdAttribute",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MissingParameterRule = new DiagnosticDescriptor(
           "MigratableAnalyzer",
           "Class implementing IMigratable has missing parameters for SerializedIdAttribute",
           "Class '{0}' has missing SerializedIdAttribute parameter {1}",
           Category,
           DiagnosticSeverity.Error,
           isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor IncorrectParameterRule = new DiagnosticDescriptor(
            "MigratableAnalyzer",
            "Class implementing IMigratable has incorrect parameters for SerializedIdAttribute",
            "Class '{0}' has incorrect SerializedIdAttribute parameter {1}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor DuplicateVersionRule = new DiagnosticDescriptor(
           "MigratableAnalyzer",
           "Class implementing IMigratable has duplicate version for SerializedIdAttribute",
           "Class '{0}' has duplicate version for SerializedIdAttribute",
           Category,
           DiagnosticSeverity.Error,
           isEnabledByDefault: true);

        // Concurrent mapping using dictionary for best thread safety vs bag etc.
        private static ConcurrentDictionary<string, ConcurrentDictionary<int,int>> _versionMap;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(MissingAttributeRule, MissingParameterRule, IncorrectParameterRule, DuplicateVersionRule); } }

        public override void Initialize(AnalysisContext p_context)
        {
            _versionMap = new ConcurrentDictionary<string, ConcurrentDictionary<int,int>>(); 

            p_context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            p_context.EnableConcurrentExecution();   

            p_context.RegisterSymbolAction(AnalyzeNamedTypeSymbol, SymbolKind.NamedType);
        }

        private static void AnalyzeNamedTypeSymbol(SymbolAnalysisContext p_context)
        {
            var namedTypeSymbol = (INamedTypeSymbol)p_context.Symbol;

            if (!namedTypeSymbol.TypeKind.Equals(TypeKind.Class))
                return;

            var hasInterface = namedTypeSymbol.AllInterfaces.Any(i => i.Name.Equals("IMigratable"));

            if (!hasInterface)
                return;

            var hasAttribute = namedTypeSymbol.GetAttributes().Any(a => a.AttributeClass.Name.Equals("SerializedIdAttribute"));

            if (!hasAttribute)
            {
                var diagnostic = Diagnostic.Create(MissingAttributeRule, namedTypeSymbol.Locations[0], namedTypeSymbol.Name);
                p_context.ReportDiagnostic(diagnostic);
                return;
            }

            var attributeData = namedTypeSymbol.GetAttributes().FirstOrDefault(a => a.AttributeClass.Name.Equals("SerializedIdAttribute"));

            if (!attributeData.NamedArguments.Any(arg => arg.Key == "id"))
            {
                var diagnostic = Diagnostic.Create(MissingParameterRule, namedTypeSymbol.Locations[0], namedTypeSymbol.Name, "id");
                p_context.ReportDiagnostic(diagnostic);
                return;
            }
            
            var idArgument = attributeData.NamedArguments.FirstOrDefault(arg => arg.Key == "id");
            string id = idArgument.Value.Value?.ToString();
            if (id == "")
            {
                var diagnostic = Diagnostic.Create(IncorrectParameterRule, namedTypeSymbol.Locations[0], namedTypeSymbol.Name, "id");
                p_context.ReportDiagnostic(diagnostic);
                return;
            }

            if (!attributeData.NamedArguments.Any(arg => arg.Key == "version"))
            {
                var diagnostic = Diagnostic.Create(MissingParameterRule, namedTypeSymbol.Locations[0], namedTypeSymbol.Name, "version");
                p_context.ReportDiagnostic(diagnostic);
                return;
            }
            else
            {
                var versionArgument = attributeData.NamedArguments.FirstOrDefault(arg => arg.Key == "version");
                int version = (int)versionArgument.Value.Value;
                if (version < 0)
                {
                    var diagnostic = Diagnostic.Create(IncorrectParameterRule, namedTypeSymbol.Locations[0], namedTypeSymbol.Name, "version");
                    p_context.ReportDiagnostic(diagnostic);
                    return;
                } else
                {
                    if (!_versionMap.ContainsKey(id))
                    {
                        var inner = new ConcurrentDictionary<int, int>();
                        _versionMap.TryAdd(id, inner);
                    }
                    
                    if (!_versionMap[id].TryAdd(version, version))
                    {
                        var diagnostic = Diagnostic.Create(DuplicateVersionRule, namedTypeSymbol.Locations[0], namedTypeSymbol.Name);
                        p_context.ReportDiagnostic(diagnostic);
                        return;
                    } 
                }
            }
        }
    }
}
