// Regenerates the application icon from Tandem's own drawing code:
//   dotnet run --project tools/Tandem.IconGen -- src/Tandem.App/Assets/Tandem.ico
using Tandem.Core.Shell;

var output = args.Length > 0 ? args[0] : "Tandem.ico";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllBytes(output, IconFactory.CreateIco(IconFactory.AppBackground, IconFactory.Cream, rim: true));
Console.WriteLine($"Wrote {Path.GetFullPath(output)}");
