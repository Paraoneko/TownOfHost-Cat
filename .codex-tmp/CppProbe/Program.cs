using Cpp2IL.Core;
using Cpp2IL.Core.Model.Contexts;

var amongUs = args.Length > 0 && !args[0].Contains("::") ? args[0] : @"D:\SteamLibrary\steamapps\common\AmongUs";
var assemblyPath = Path.Combine(amongUs, "GameAssembly.dll");
var metadataPath = Path.Combine(amongUs, "Among Us_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
var unityPlayerPath = Path.Combine(amongUs, "UnityPlayer.dll");
var gameDataPath = Path.Combine(amongUs, "Among Us_Data");

Console.WriteLine("assembly=" + assemblyPath);
Console.WriteLine("metadata=" + metadataPath);

Cpp2IlApi.Init(Path.Combine(AppContext.BaseDirectory, "lib"));
var unityVersion = Cpp2IlApi.DetermineUnityVersion(unityPlayerPath, gameDataPath);
Console.WriteLine("unity=" + unityVersion);

Cpp2IlApi.InitializeLibCpp2Il(assemblyPath, metadataPath, unityVersion, allowUserToInputAddresses: false);
var app = Cpp2IlApi.CurrentAppContext;
Console.WriteLine("instructionSet=" + app.InstructionSet.GetType().FullName);
Console.WriteLine("assemblies=" + app.Assemblies.Count);

var defaultTargets = new (string type, string method)[]
{
    ("EndGameResult", "Create"),
    ("ProgressionScreen", "DoAnimations"),
    ("ProgressionScreen", "AnimateXpAndLevelUp"),
    ("ProgressionScreen", "AnimatePodsAndBeans"),
    ("CurrencyEarned", "ShowMultiplier"),
    ("AmongUs.Data.Player.PlayerStatsData", "SaveStats"),
    ("AchievementManager", "UpdateAchievementsAndStats"),
};

var targetArgs = args.Where(a => a.Contains("::")).ToArray();
IEnumerable<(string type, string method)> targets = targetArgs.Length == 0
    ? defaultTargets
    : targetArgs.Select(a =>
    {
        var parts = a.Split(new[] { "::" }, 2, StringSplitOptions.None);
        return (type: parts[0], method: parts[1]);
    });

foreach (var target in targets)
{
    var type = app.AllTypes.FirstOrDefault(t => t.FullName == target.type);
    if (type is null)
    {
        Console.WriteLine("missing type " + target.type);
        continue;
    }

    foreach (var method in type.Methods.Where(m => m.DefaultName == target.method))
    {
        Dump(method);
    }
}

return 0;

static void Dump(MethodAnalysisContext method)
{
    Console.WriteLine();
    Console.WriteLine("METHOD " + method.FullNameWithSignature);
    Console.WriteLine($"  ptr=0x{method.UnderlyingPointer:X} rva=0x{method.Rva:X}");

    try
    {
        method.EnsureRawBytes();
        Console.WriteLine("  rawBytes=" + method.RawBytes.Length);
    }
    catch (Exception ex)
    {
        Console.WriteLine("  rawBytesError=" + ex.GetType().Name + ": " + ex.Message);
    }

    try
    {
        var asm = Cpp2IlApi.CurrentAppContext.InstructionSet.PrintAssembly(method);
        var lineCount = int.TryParse(Environment.GetEnvironmentVariable("CPP_PROBE_LINES"), out var parsed)
            ? parsed
            : 80;
        var lines = asm.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Take(lineCount);
        foreach (var line in lines)
        {
            Console.WriteLine("  " + line);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("  asmError=" + ex.GetType().Name + ": " + ex.Message);
    }
}
