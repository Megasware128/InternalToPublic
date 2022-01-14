# Megasware128.InternalToPublic
Source generator to make internal classes from external libraries publicly available to the current project

Note: Currently only supports static classes/methods with references to publicly available types

## Usage
1. Add package to your project
2. Add using: `using Megasware128.InternalToPublic;`
3. Define a type you want to make available. Example: `[assembly: InternalToPublic("Newtonsoft.Json", "Newtonsoft.Json.Utilities.DateTimeUtils")]`
4. Your type should now be available in the namespace: `Megasware128.InternalToPublic`

Note: editors seem to be glitchy but `dotnet build` always works

## Build
- Optional: run `dotnet tool install nuke.globaltool -g`
- Use `nuke --help` or `./build.cmd --help` to see the different commands
- Use `nuke test-app` to test the generator
- Use `nuke pack` to build a NuGet package