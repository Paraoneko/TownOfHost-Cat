using System.Text.RegularExpressions;
using Mono.Cecil;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: MetaReader <assembly> [pattern]");
    return 2;
}

var pattern = args.Length > 1
    ? args[1]
    : "XP|Xp|Experience|PlayerLevel|Level|Multiplier|Reward|Progress|Account|Stats|Stat|Cosmic|Bean|Beans|Stars|Currency|Wallet|Grant";

var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
var dumpAllWhenTypeMatches = args.Contains("--dump-type");
var module = ModuleDefinition.ReadModule(args[0]);

foreach (var type in module.Types.SelectMany(Flatten))
{
    var lines = new List<string>();
    var typeMatches = regex.IsMatch(type.FullName);
    if (typeMatches)
    {
        lines.Add("TYPE " + type.FullName);
    }

    foreach (var field in type.Fields)
    {
        if (dumpAllWhenTypeMatches && typeMatches || regex.IsMatch(field.Name))
        {
            lines.Add($"  FIELD {field.FieldType.FullName} {field.Name}");
        }
    }

    foreach (var property in type.Properties)
    {
        if (dumpAllWhenTypeMatches && typeMatches || regex.IsMatch(property.Name))
        {
            lines.Add($"  PROPERTY {property.PropertyType.FullName} {property.Name}");
        }
    }

    foreach (var method in type.Methods)
    {
        if (dumpAllWhenTypeMatches && typeMatches || regex.IsMatch(method.Name))
        {
            lines.Add($"  METHOD {method.ReturnType.FullName} {method.Name}({string.Join(", ", method.Parameters.Select(p => p.ParameterType.FullName + " " + p.Name))})");
        }
    }

    if (lines.Count > 0)
    {
        if (!lines[0].StartsWith("TYPE "))
        {
            Console.WriteLine("TYPE " + type.FullName);
        }

        foreach (var line in lines)
        {
            Console.WriteLine(line);
        }
    }
}

return 0;

static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
{
    yield return type;

    foreach (var nested in type.NestedTypes)
    {
        foreach (var child in Flatten(nested))
        {
            yield return child;
        }
    }
}
