using Megasware128.InternalToPublic;

[assembly: InternalToPublic("System.Linq.Expressions", "System.Linq.Expressions.Error", PublicType = "System.Linq.Expressions.Expression")]
[assembly: InternalToPublic("Newtonsoft.Json", "Newtonsoft.Json.Utilities.DateTimeUtils", PublicType = "Newtonsoft.Json.JsonSerializer")]

var error = Error.ReducibleMustOverrideReduce();

Console.WriteLine(error);

Console.WriteLine(DateTimeUtils.ToUniversalTicks(DateTime.Now));
